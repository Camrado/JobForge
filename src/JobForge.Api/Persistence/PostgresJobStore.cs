using JobForge.Api.Dtos;
using JobForge.Api.Models;
using JobForge.Core.Dtos;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace JobForge.Api.Persistence;

public sealed class PostgresJobStore(NpgsqlDataSource dataSource) : IJobStore
{
    // Any fixed number works, as long as every admission uses the same one.
    private const long AdmissionLockKey = 1;

    // One options object for writing and reading the report, so the two can never drift apart.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<bool> TryAddAsync(Guid id, string datasetId, string filePath, int maxQueued, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Only one admission at a time may count and insert. The lock is released automatically
        // when the transaction ends, so two parallel requests cannot both see room for one more job.
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", AdmissionLockKey);
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        await using (var countCommand = new NpgsqlCommand("SELECT count(*) FROM jobs WHERE status = 'Queued'", connection, transaction))
        {
            var queued = (long)(await countCommand.ExecuteScalarAsync(ct))!;
            if (queued >= maxQueued)
            {
                return false;
            }
        }

        await using (var insertCommand = new NpgsqlCommand("""
                                                           INSERT INTO jobs (id, dataset_id, file_path, status)
                                                           VALUES (@id, @datasetId, @filePath, 'Queued')
                                                           """, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("id", id);
            insertCommand.Parameters.AddWithValue("datasetId", datasetId);
            insertCommand.Parameters.AddWithValue("filePath", filePath);
            await insertCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<JobRecordDto?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           SELECT id, dataset_id, file_path, status, cancel_requested, report, error
                                                           FROM jobs
                                                           WHERE id = @id
                                                           """);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? MapJob(reader) : null;
    }

    public async Task<JobRecordDto?> TryClaimNextAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           UPDATE jobs SET status = 'Running', started_at = now()
                                                           WHERE id = (SELECT id FROM jobs WHERE status = 'Queued' ORDER BY created_at LIMIT 1)
                                                           RETURNING id, dataset_id, file_path, status, cancel_requested, report, error
                                                           """);

        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? MapJob(reader) : null;
    }

    public async Task<bool> TrySucceedAsync(Guid id, ValidationReportDto report, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           UPDATE jobs SET status = 'Succeeded', report = @report, finished_at = now()
                                                           WHERE id = @id AND status = 'Running' AND cancel_requested = false
                                                           """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.Add(new NpgsqlParameter("report", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(report, JsonOptions)
        });

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> TryFailAsync(Guid id, string error, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           UPDATE jobs SET status = 'Failed', error = @error, finished_at = now()
                                                           WHERE id = @id AND status = 'Running'
                                                           """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("error", error);

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> TryCancelQueuedAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           UPDATE jobs SET status = 'Cancelled', finished_at = now()
                                                           WHERE id = @id AND status = 'Queued'
                                                           """);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> TryRequestCancelAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           UPDATE jobs SET cancel_requested = true
                                                           WHERE id = @id AND status = 'Running'
                                                           """);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> TryFinishCancelAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
                                                           UPDATE jobs SET status = 'Cancelled', finished_at = now()
                                                           WHERE id = @id AND status = 'Running' AND cancel_requested = true
                                                           """);
        command.Parameters.AddWithValue("id", id);

        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // A cancel was already requested for the job that was running: finish it as Cancelled.
        await using (var cancelCommand = new NpgsqlCommand("""
                                                           UPDATE jobs SET status = 'Cancelled', finished_at = now()
                                                           WHERE status = 'Running' AND cancel_requested = true
                                                           """, connection, transaction))
        {
            await cancelCommand.ExecuteNonQueryAsync(ct);
        }

        // Every other Running job was interrupted: put it back in the queue to start again from line 1.
        await using (var requeueCommand = new NpgsqlCommand("""
                                                           UPDATE jobs SET status = 'Queued', started_at = NULL
                                                           WHERE status = 'Running' AND cancel_requested = false
                                                           """, connection, transaction))
        {
            await requeueCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static JobRecordDto MapJob(NpgsqlDataReader reader)
    {
        var reportJson = reader.IsDBNull(5) ? null : reader.GetString(5);

        return new JobRecordDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<JobStatusType>(reader.GetString(3)),
            reader.GetBoolean(4),
            reportJson is null ? null : JsonSerializer.Deserialize<ValidationReportDto>(reportJson, JsonOptions),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }
}
