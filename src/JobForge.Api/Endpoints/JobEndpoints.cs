using JobForge.Api.Dtos;
using JobForge.Api.Models;

namespace JobForge.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/jobs");

        jobs.MapPost("/", CreateJob);
        jobs.MapGet("/{id:guid}", GetJobStatus);
        jobs.MapPost("/{id:guid}/cancel", CancelJob);
        jobs.MapGet("/{id:guid}/report", GetJobReport);

        return app;
    }

    private static IResult CreateJob(
        CreateJobRequestDto request,
        DatasetCatalog datasets,
        JobRegistry registry,
        JobQueue queue,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.DatasetId) || !datasets.TryGetPath(request.DatasetId, out var path))
        {
            return Results.BadRequest(new { error = $"Unknown dataset '{request.DatasetId}'." });
        }

        var job = new JobEntry(Guid.NewGuid(), request.DatasetId, path);
        registry.Add(job);

        if (!queue.TryEnqueue(job.Id))
        {
            // Queue full: undo the registration, so a rejected job leaves nothing behind.
            registry.Remove(job.Id);
            http.Response.Headers.RetryAfter = "5";
            return Results.Problem("The job queue is full. Try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted($"/jobs/{job.Id}", ToStatusDto(job));
    }

    private static IResult GetJobStatus(Guid id, JobRegistry registry)
    {
        var job = registry.TryGet(id);
        return job is null ? Results.NotFound() : Results.Ok(ToStatusDto(job));
    }

    private static IResult CancelJob(Guid id, JobRegistry registry)
    {
        var job = registry.TryGet(id);
        if (job is null)
        {
            return Results.NotFound();
        }

        // Safe to repeat: cancelling a finished job does nothing and just returns its current status.
        job.TryCancel();
        return Results.Ok(ToStatusDto(job));
    }

    private static IResult GetJobReport(Guid id, JobRegistry registry)
    {
        var job = registry.TryGet(id);
        if (job is null)
        {
            return Results.NotFound();
        }

        // One snapshot, so status and report always belong together.
        var snapshot = job.GetSnapshot();
        return snapshot.Status == JobStatusType.Succeeded
            ? Results.Ok(snapshot.Report)
            : Results.Conflict(new { error = $"Job is {snapshot.Status}; a report exists only for Succeeded jobs." });
    }

    private static JobStatusDto ToStatusDto(JobEntry job)
    {
        // Read Status and Error together under the job's lock, so they always match.
        var snapshot = job.GetSnapshot();
        return new JobStatusDto(job.Id, job.DatasetId, snapshot.Status, snapshot.Error);
    }
}
