using JobForge.Api.Models;
using JobForge.Api.Persistence;
using JobForge.Core.Dtos;
using JobForge.Core.Models;
using Npgsql;
using Xunit;

namespace JobForge.Api.Tests.Persistence;

/// <summary>
/// Milestone 2, checkpoints 1 to 4, against a real PostgreSQL. Every test starts with an empty jobs table.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JobStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static readonly ValidationReportDto Report = new(
        Total: 10,
        Valid: 5,
        Invalid: 5,
        ErrorCounts: new Dictionary<ValidationErrorType, int>
        {
            [ValidationErrorType.InvalidAmount] = 2,
            [ValidationErrorType.InvalidId] = 1,
            [ValidationErrorType.UnsupportedCurrency] = 1,
            [ValidationErrorType.MalformedJson] = 1,
        },
        Samples:
        [
            new InvalidLineDto(2, [ValidationErrorType.InvalidAmount]),
            new InvalidLineDto(3, [ValidationErrorType.InvalidId, ValidationErrorType.UnsupportedCurrency]),
            new InvalidLineDto(7, [ValidationErrorType.MalformedJson]),
        ]);

    private readonly PostgresJobStore _store = new(postgres.DataSource);

    public Task InitializeAsync() => ExecuteAsync("TRUNCATE jobs");

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Checkpoint 1: round-trip a job and its report ----------

    [Fact]
    public async Task Report_RoundTrips_WithEveryErrorCategoryAndSample()
    {
        var id = await AddRunningAsync();

        Assert.True(await _store.TrySucceedAsync(id, Report, Ct));

        var job = await _store.GetAsync(id, Ct);
        Assert.NotNull(job);
        Assert.Equal(JobStatusType.Succeeded, job.Status);

        var actual = job.Report;
        Assert.NotNull(actual);
        Assert.Equal(Report.Total, actual.Total);
        Assert.Equal(Report.Valid, actual.Valid);
        Assert.Equal(Report.Invalid, actual.Invalid);
        Assert.Equal(Report.ErrorCounts.OrderBy(p => p.Key), actual.ErrorCounts.OrderBy(p => p.Key));

        Assert.Equal(Report.Samples.Count, actual.Samples.Count);
        for (var i = 0; i < Report.Samples.Count; i++)
        {
            Assert.Equal(Report.Samples[i].LineNumber, actual.Samples[i].LineNumber);
            Assert.Equal(Report.Samples[i].Errors, actual.Samples[i].Errors);
        }
    }

    [Fact]
    public async Task Report_IsStoredWithEnumNames_NotNumbers()
    {
        var id = await AddRunningAsync();
        await _store.TrySucceedAsync(id, Report, Ct);

        var json = await ScalarAsync<string>("SELECT report::text FROM jobs WHERE id = @id", ("id", id));

        Assert.Contains("\"InvalidAmount\"", json);
        Assert.Contains("\"UnsupportedCurrency\"", json);
    }

    [Fact]
    public async Task Get_OfNewJob_ReturnsQueuedJobWithItsFields()
    {
        var id = Guid.NewGuid();
        Assert.True(await _store.TryAddAsync(id, "orders", "/data/orders.ndjson", 100, Ct));

        var job = await _store.GetAsync(id, Ct);

        Assert.NotNull(job);
        Assert.Equal(id, job.Id);
        Assert.Equal("orders", job.DatasetId);
        Assert.Equal("/data/orders.ndjson", job.FilePath);
        Assert.Equal(JobStatusType.Queued, job.Status);
        Assert.False(job.CancelRequested);
        Assert.Null(job.Report);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Get_OfUnknownId_ReturnsNull()
    {
        Assert.Null(await _store.GetAsync(Guid.NewGuid(), Ct));
    }

    // ---------- Claiming ----------

    [Fact]
    public async Task Claim_TakesOldestQueuedJob_AndMarksItRunning()
    {
        var first = await AddQueuedAsync();
        var second = await AddQueuedAsync();

        var claimed = await _store.TryClaimNextAsync(Ct);

        Assert.NotNull(claimed);
        Assert.Equal(first, claimed.Id);
        Assert.Equal(JobStatusType.Running, claimed.Status);
        Assert.Equal(JobStatusType.Queued, (await _store.GetAsync(second, Ct))!.Status);
    }

    [Fact]
    public async Task Claim_WhenNothingIsQueued_ReturnsNull()
    {
        Assert.Null(await _store.TryClaimNextAsync(Ct));
    }

    // ---------- Checkpoint 2: guards refuse wrong transitions ----------

    [Theory]
    [InlineData(JobStatusType.Running)]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task Claim_IgnoresJobsThatAreNotQueued(JobStatusType state)
    {
        var id = await CreateJobInAsync(state);
        var before = await SnapshotAsync(id);

        Assert.Null(await _store.TryClaimNextAsync(Ct));
        Assert.Equal(before, await SnapshotAsync(id));
    }

    [Theory]
    [InlineData(JobStatusType.Queued)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task Succeed_FromWrongState_ReturnsFalse_AndStoresNoReport(JobStatusType state)
    {
        var id = await CreateJobInAsync(state);
        var before = await SnapshotAsync(id);

        Assert.False(await _store.TrySucceedAsync(id, Report, Ct));

        var after = await SnapshotAsync(id);
        Assert.False(after.HasReport);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Succeed_WhenCancelWasRequested_ReturnsFalse_AndStoresNoReport()
    {
        var id = await AddRunningWithCancelRequestAsync();

        Assert.False(await _store.TrySucceedAsync(id, Report, Ct));

        var after = await SnapshotAsync(id);
        Assert.Equal("Running", after.Status);
        Assert.False(after.HasReport);
    }

    [Theory]
    [InlineData(JobStatusType.Queued)]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task Fail_FromWrongState_ReturnsFalse_AndLeavesJobUnchanged(JobStatusType state)
    {
        var id = await CreateJobInAsync(state);
        var before = await SnapshotAsync(id);

        Assert.False(await _store.TryFailAsync(id, "boom", Ct));
        Assert.Equal(before, await SnapshotAsync(id));
    }

    [Theory]
    [InlineData(JobStatusType.Running)]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task CancelQueued_FromWrongState_ReturnsFalse_AndLeavesJobUnchanged(JobStatusType state)
    {
        var id = await CreateJobInAsync(state);
        var before = await SnapshotAsync(id);

        Assert.False(await _store.TryCancelQueuedAsync(id, Ct));
        Assert.Equal(before, await SnapshotAsync(id));
    }

    [Theory]
    [InlineData(JobStatusType.Queued)]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task RequestCancel_FromWrongState_ReturnsFalse_AndFlagStaysFalse(JobStatusType state)
    {
        var id = await CreateJobInAsync(state);

        Assert.False(await _store.TryRequestCancelAsync(id, Ct));
        Assert.False((await SnapshotAsync(id)).CancelRequested);
    }

    [Theory]
    [InlineData(JobStatusType.Queued)]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task FinishCancel_FromWrongState_ReturnsFalse_AndLeavesJobUnchanged(JobStatusType state)
    {
        var id = await CreateJobInAsync(state);
        var before = await SnapshotAsync(id);

        Assert.False(await _store.TryFinishCancelAsync(id, Ct));
        Assert.Equal(before, await SnapshotAsync(id));
    }

    [Fact]
    public async Task FinishCancel_WithoutCancelRequest_ReturnsFalse_AndJobKeepsRunning()
    {
        var id = await AddRunningAsync();

        Assert.False(await _store.TryFinishCancelAsync(id, Ct));
        Assert.Equal("Running", (await SnapshotAsync(id)).Status);
    }

    [Theory]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public async Task TerminalJob_CannotBeChangedByAnyTransition(JobStatusType terminal)
    {
        var id = await CreateJobInAsync(terminal);
        var before = await SnapshotAsync(id);

        Assert.False(await _store.TrySucceedAsync(id, Report, Ct));
        Assert.False(await _store.TryFailAsync(id, "late failure", Ct));
        Assert.False(await _store.TryCancelQueuedAsync(id, Ct));
        Assert.False(await _store.TryRequestCancelAsync(id, Ct));
        Assert.False(await _store.TryFinishCancelAsync(id, Ct));
        Assert.Null(await _store.TryClaimNextAsync(Ct));
        await _store.RecoverInterruptedAsync(Ct);

        Assert.Equal(before, await SnapshotAsync(id));
    }

    // ---------- Valid transitions ----------

    [Fact]
    public async Task Succeed_FromRunning_StoresStatusAndReportTogether()
    {
        var id = await AddRunningAsync();

        Assert.True(await _store.TrySucceedAsync(id, Report, Ct));

        var after = await SnapshotAsync(id);
        Assert.Equal("Succeeded", after.Status);
        Assert.True(after.HasReport);
        Assert.NotNull(after.FinishedAt);
    }

    [Fact]
    public async Task Fail_FromRunning_StoresTheError_AndNoReport()
    {
        var id = await AddRunningAsync();

        Assert.True(await _store.TryFailAsync(id, "disk exploded", Ct));

        var after = await SnapshotAsync(id);
        Assert.Equal("Failed", after.Status);
        Assert.Equal("disk exploded", after.Error);
        Assert.False(after.HasReport);
        Assert.NotNull(after.FinishedAt);
    }

    [Fact]
    public async Task CancelQueued_FromQueued_SetsCancelled()
    {
        var id = await AddQueuedAsync();

        Assert.True(await _store.TryCancelQueuedAsync(id, Ct));

        var after = await SnapshotAsync(id);
        Assert.Equal("Cancelled", after.Status);
        Assert.False(after.HasReport);
        Assert.NotNull(after.FinishedAt);
    }

    [Fact]
    public async Task CancelRequest_ThenFinish_KeepsRunningUntilFinished_ThenCancels()
    {
        var id = await AddRunningAsync();

        Assert.True(await _store.TryRequestCancelAsync(id, Ct));

        var requested = await SnapshotAsync(id);
        Assert.Equal("Running", requested.Status);
        Assert.True(requested.CancelRequested);

        Assert.True(await _store.TryFinishCancelAsync(id, Ct));

        var finished = await SnapshotAsync(id);
        Assert.Equal("Cancelled", finished.Status);
        Assert.False(finished.HasReport);
    }

    [Fact]
    public async Task CancelRequest_IsSafeToRepeat()
    {
        var id = await AddRunningAsync();

        Assert.True(await _store.TryRequestCancelAsync(id, Ct));
        Assert.True(await _store.TryRequestCancelAsync(id, Ct));

        Assert.True((await SnapshotAsync(id)).CancelRequested);
    }

    [Fact]
    public async Task Succeed_ThenCancelRequest_CancelLoses_AndJobStaysSucceeded()
    {
        var id = await AddRunningAsync();
        Assert.True(await _store.TrySucceedAsync(id, Report, Ct));

        Assert.False(await _store.TryRequestCancelAsync(id, Ct));

        var after = await SnapshotAsync(id);
        Assert.Equal("Succeeded", after.Status);
        Assert.True(after.HasReport);
        Assert.False(after.CancelRequested);
    }

    // ---------- Admission control ----------

    [Fact]
    public async Task Add_WhenQueueIsFull_ReturnsFalse_AndLeavesNoRow()
    {
        Assert.True(await _store.TryAddAsync(Guid.NewGuid(), "orders", "/data/orders.ndjson", 2, Ct));
        Assert.True(await _store.TryAddAsync(Guid.NewGuid(), "orders", "/data/orders.ndjson", 2, Ct));

        var rejected = Guid.NewGuid();
        Assert.False(await _store.TryAddAsync(rejected, "orders", "/data/orders.ndjson", 2, Ct));

        Assert.Null(await _store.GetAsync(rejected, Ct));
        Assert.Equal(2L, await CountAsync("Queued"));
    }

    [Fact]
    public async Task Add_AfterAJobIsClaimed_HasRoomAgain()
    {
        await _store.TryAddAsync(Guid.NewGuid(), "orders", "/data/orders.ndjson", 2, Ct);
        await _store.TryAddAsync(Guid.NewGuid(), "orders", "/data/orders.ndjson", 2, Ct);
        await _store.TryClaimNextAsync(Ct);

        Assert.True(await _store.TryAddAsync(Guid.NewGuid(), "orders", "/data/orders.ndjson", 2, Ct));
    }

    // ---------- Checkpoint 3: admission holds under concurrency ----------

    [Fact]
    public async Task Add_With20ParallelCalls_AcceptsExactlyTwo_EveryTime()
    {
        for (var round = 0; round < 20; round++)
        {
            await ExecuteAsync("TRUNCATE jobs");

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                Task.Run(() => _store.TryAddAsync(Guid.NewGuid(), "orders", "/data/orders.ndjson", 2, Ct))));

            Assert.Equal(2, results.Count(accepted => accepted));
            Assert.Equal(2L, await CountAsync("Queued"));
        }
    }

    // ---------- Checkpoint 4: status and report appear together ----------

    [Fact]
    public async Task ConcurrentReaders_NeverSeeSucceededWithoutReport_OrReportWithoutSucceeded()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 30; i++)
        {
            ids.Add(await AddRunningAsync());
        }

        var violations = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var stop = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var id in ids)
                {
                    var job = await _store.GetAsync(id, Ct);
                    if ((job!.Status == JobStatusType.Succeeded) != (job.Report is not null))
                    {
                        violations.Add($"{id}: status {job.Status}, report present: {job.Report is not null}");
                    }
                }
            }
        })).ToList();

        foreach (var id in ids)
        {
            Assert.True(await _store.TrySucceedAsync(id, Report, Ct));
        }

        stop.Cancel();
        await Task.WhenAll(readers);

        Assert.Empty(violations);
        Assert.All(ids, id => Assert.Equal(JobStatusType.Succeeded, _store.GetAsync(id, Ct).Result!.Status));
    }

    [Fact]
    public async Task Database_RefusesSucceededRowWithoutReport()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO jobs (id, dataset_id, file_path, status) VALUES (gen_random_uuid(), 'orders', '/x', 'Succeeded')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_RefusesReportOnAJobThatIsNotSucceeded()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO jobs (id, dataset_id, file_path, status, report) VALUES (gen_random_uuid(), 'orders', '/x', 'Queued', '{}'::jsonb)"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_RefusesUnknownStatus()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO jobs (id, dataset_id, file_path, status) VALUES (gen_random_uuid(), 'orders', '/x', 'Bogus')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    // ---------- Recovery (used from checkpoint 8 on) ----------

    [Fact]
    public async Task Recover_RequeuesInterruptedJob_AndHonoursSavedCancelRequest()
    {
        // AddRunningAsync claims the oldest queued job, so the plain queued job is added last.
        var interrupted = await AddRunningAsync();
        var cancelRequested = await AddRunningWithCancelRequestAsync();
        var succeeded = await CreateJobInAsync(JobStatusType.Succeeded);
        var queued = await AddQueuedAsync();

        await _store.RecoverInterruptedAsync(Ct);

        var requeued = await SnapshotAsync(interrupted);
        Assert.Equal("Queued", requeued.Status);
        Assert.Null(requeued.StartedAt);

        var cancelled = await SnapshotAsync(cancelRequested);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.False(cancelled.HasReport);

        Assert.Equal("Queued", (await SnapshotAsync(queued)).Status);
        Assert.Equal("Succeeded", (await SnapshotAsync(succeeded)).Status);
    }

    // ---------- Helpers ----------

    private async Task<Guid> AddQueuedAsync()
    {
        var id = Guid.NewGuid();
        Assert.True(await _store.TryAddAsync(id, "orders", "/data/orders.ndjson", 100, Ct));
        return id;
    }

    // Adds a job and claims it at once. The table must hold no older Queued job, so the claim picks this one.
    private async Task<Guid> AddRunningAsync()
    {
        var id = await AddQueuedAsync();
        var claimed = await _store.TryClaimNextAsync(Ct);
        Assert.Equal(id, claimed?.Id);
        return id;
    }

    private async Task<Guid> AddRunningWithCancelRequestAsync()
    {
        var id = await AddRunningAsync();
        Assert.True(await _store.TryRequestCancelAsync(id, Ct));
        return id;
    }

    private async Task<Guid> CreateJobInAsync(JobStatusType status)
    {
        switch (status)
        {
            case JobStatusType.Queued:
                return await AddQueuedAsync();
            case JobStatusType.Running:
                return await AddRunningAsync();
            case JobStatusType.Succeeded:
            {
                var id = await AddRunningAsync();
                Assert.True(await _store.TrySucceedAsync(id, Report, Ct));
                return id;
            }
            case JobStatusType.Failed:
            {
                var id = await AddRunningAsync();
                Assert.True(await _store.TryFailAsync(id, "setup failure", Ct));
                return id;
            }
            case JobStatusType.Cancelled:
            {
                var id = await AddQueuedAsync();
                Assert.True(await _store.TryCancelQueuedAsync(id, Ct));
                return id;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    // The row as stored, read with plain SQL so the tests do not depend on the store's own reading code.
    private sealed record RowSnapshot(
        string Status,
        bool CancelRequested,
        bool HasReport,
        string? Error,
        DateTime? StartedAt,
        DateTime? FinishedAt);

    private async Task<RowSnapshot> SnapshotAsync(Guid id)
    {
        await using var command = postgres.DataSource.CreateCommand("""
            SELECT status, cancel_requested, report IS NOT NULL, error, started_at, finished_at
            FROM jobs WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));

        return new RowSnapshot(
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5));
    }

    private async Task<long> CountAsync(string status) =>
        await ScalarAsync<long>("SELECT count(*) FROM jobs WHERE status = @status", ("status", status));

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = postgres.DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = postgres.DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
