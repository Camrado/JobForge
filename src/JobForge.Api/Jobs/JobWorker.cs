using JobForge.Api.Models;
using JobForge.Core.Validation.FileValidator;

namespace JobForge.Api.Jobs;

public sealed class JobWorker(
    JobRegistry jobRegistry,
    JobQueue jobQueue,
    IFileValidator fileValidator,
    ILogger<JobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in jobQueue.ReadAllAsync(stoppingToken))
        {
            var job = jobRegistry.TryGet(jobId);
            if (job is null)
            {
                continue;
            }

            var isStarted = job.TryStart();
            if (!isStarted)
            {
                logger.LogInformation("Job {JobId} skipped: cancelled while queued", job.Id);
                continue;
            }

            logger.LogInformation("Job {JobId} started on dataset {DatasetId}", job.Id, job.DatasetId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Token);

            try
            {
                var report = await fileValidator.ValidateAsync(job.FilePath, cts.Token);
                if (job.TrySucceed(report))
                {
                    logger.LogInformation("Job {JobId} succeeded", job.Id);
                }
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                logger.LogInformation("Job {JobId} cancelled", job.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobId} failed", job.Id);
                job.TryFail(ex.Message);
            }
        }
    }
}