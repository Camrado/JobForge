using JobForge.Api.Models;
using JobForge.Core;

namespace JobForge.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiValidator(this IServiceCollection services)
    {
        services.AddJobForgeCore();
        services.AddSingleton<JobRegistry>();
        services.AddSingleton<DatasetCatalog>();
        services.AddSingleton<JobQueue>();

        return services;
    }
}
