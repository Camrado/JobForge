using System.Text.Json.Serialization;
using JobForge.Api;
using JobForge.Api.Endpoints;
using JobForge.Api.Jobs;
using JobForge.Api.Persistence;

try
{
    DotNetEnv.Env.TraversePath().NoClobber().Load();
}
catch (FileNotFoundException)
{
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApiValidator();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Tests run without a database for now. Once they use Testcontainers, drop this condition.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddPostgres(builder.Configuration);
}

builder.Services.AddHostedService<JobWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapJobEndpoints();

app.Run();

public partial class Program;
