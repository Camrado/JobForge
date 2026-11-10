using JobForge.Core;

namespace JobForge.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiValidator(this IServiceCollection services)
    {
        services.AddJobForgeCore();

        return services;
    }
}
