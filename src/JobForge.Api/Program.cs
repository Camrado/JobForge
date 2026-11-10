using System.Text.Json.Serialization;
using JobForge.Api;
using JobForge.Api.Endpoints;
using JobForge.Api.Jobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApiValidator();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHostedService<JobWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapJobEndpoints();

app.Run();
