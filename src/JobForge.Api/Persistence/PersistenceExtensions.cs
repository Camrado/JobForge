using Npgsql;

namespace JobForge.Api.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPostgres(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("JobForge")
            ?? throw new InvalidOperationException(
                "Connection string 'JobForge' is missing. Set ConnectionStrings__JobForge in your .env file.");

        // One data source for the whole app: it owns the connection pool.
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddHostedService<SchemaInitializer>();

        return services;
    }
}
