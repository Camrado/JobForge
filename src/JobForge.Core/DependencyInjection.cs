using JobForge.Core.Validation.FileValidator;
using JobForge.Core.Validation.LineValidator;
using Microsoft.Extensions.DependencyInjection;

namespace JobForge.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddJobForgeCore(this IServiceCollection services)
    {
        services.AddSingleton<ILineValidator, LineValidator>();
        services.AddSingleton<IFileValidator, ConcurrentFileValidator>();

        return services;
    }
}
