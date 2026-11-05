using JobForge.ConsoleValidator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddConsoleValidator();

using var host = builder.Build();

// Ctrl+C cancels the run instead of killing the process.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var app = host.Services.GetRequiredService<ValidatorApp>();
return await app.RunAsync(args, cts.Token);
