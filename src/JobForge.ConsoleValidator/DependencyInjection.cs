using JobForge.ConsoleValidator.Output;
using JobForge.ConsoleValidator.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace JobForge.ConsoleValidator;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the console validator needs.
    /// In 1B, swap the IFileValidator line to the concurrent implementation.
    /// </summary>
    public static IServiceCollection AddConsoleValidator(this IServiceCollection services)
    {
        services.AddSingleton<ILineValidator, LineValidator>();
        services.AddSingleton<IFileValidator, ConcurrentFileValidator>();
        services.AddSingleton<IReportPrinter, ConsoleReportPrinter>();
        services.AddSingleton<ValidatorApp>();

        return services;
    }
}
