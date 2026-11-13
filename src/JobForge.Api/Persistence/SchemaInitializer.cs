using Npgsql;

namespace JobForge.Api.Persistence;

public sealed class SchemaInitializer(NpgsqlDataSource dataSource, ILogger<SchemaInitializer> logger) : IHostedService
{
    private const string CreateSchemaSql = """
        CREATE TABLE IF NOT EXISTS jobs (
            id               uuid        PRIMARY KEY,
            dataset_id       text        NOT NULL,
            file_path        text        NOT NULL,
            status           text        NOT NULL,
            cancel_requested boolean     NOT NULL DEFAULT false,
            report           jsonb       NULL,
            error            text        NULL,
            created_at       timestamptz NOT NULL DEFAULT now(),
            started_at       timestamptz NULL,
            finished_at      timestamptz NULL,
            CONSTRAINT jobs_status_valid
                CHECK (status IN ('Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled')),
            CONSTRAINT jobs_report_only_when_succeeded
                CHECK ((status = 'Succeeded') = (report IS NOT NULL))
        );

        CREATE INDEX IF NOT EXISTS jobs_status_created_at ON jobs (status, created_at);
        """;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(CreateSchemaSql);
        await command.ExecuteNonQueryAsync(cancellationToken);

        logger.LogInformation("Database schema is ready");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
