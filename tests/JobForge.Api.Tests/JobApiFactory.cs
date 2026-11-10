using JobForge.Core.Validation.FileValidator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobForge.Api.Tests;

/// <summary>
/// Runs the real API in memory with a fake IFileValidator and test-owned datasets.
/// Create one per test, so every test gets a fresh registry, queue and worker.
/// </summary>
public sealed class JobApiFactory(IFileValidator validator) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Added after appsettings.json, so these values win. The fake validator never opens the files.
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Datasets:orders"] = "test-data/orders.ndjson",
            ["Datasets:large-10x"] = "test-data/large-orders-10x.ndjson",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileValidator>();
            services.AddSingleton(validator);
        });
    }
}
