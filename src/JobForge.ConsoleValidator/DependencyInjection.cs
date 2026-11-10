using JobForge.ConsoleValidator.Output;
using JobForge.Core;
using Microsoft.Extensions.DependencyInjection;

namespace JobForge.ConsoleValidator;

public static class DependencyInjection
{
    public static IServiceCollection AddConsoleValidator(this IServiceCollection services)
    {
        services.AddJobForgeCore();
        services.AddSingleton<IReportPrinter, ConsoleReportPrinter>();
        services.AddSingleton<ValidatorApp>();

        return services;
    }
}
