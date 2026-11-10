using JobForge.Api.Jobs;
using JobForge.Api.Models;
using JobForge.Api.Tests.Fakes;
using JobForge.Core.Dtos;
using JobForge.Core.Models;
using JobForge.Core.Validation.FileValidator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobForge.Api.Tests;

/// <summary>
/// Runs the real JobWorker against a fake IFileValidator. No web host.
/// </summary>
public class JobWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly ValidationReportDto Report = new(
        Total: 5,
        Valid: 2,
        Invalid: 3,
        ErrorCounts: new Dictionary<ValidationErrorType, int> { [ValidationErrorType.InvalidAmount] = 3 },
        Samples: []);

    private readonly JobRegistry _registry = new();
    private readonly JobQueue _queue = new();

    [Fact]
    public async Task QueuedJob_EndsSucceeded_WithTheValidatorsReport()
    {
        var validator = FakeFileValidator.Returning(Report);

        await RunWorkerAsync(validator, async () =>
        {
            var job = Submit("a.ndjson");

            await WaitForStatusAsync(job, JobStatusType.Succeeded);
            Assert.Same(Report, job.GetSnapshot().Report);
        });
    }

    [Fact]
    public async Task FailingJob_EndsFailed_AndTheNextJobStillSucceeds()
    {
        // Throws for the first file only, so the second job proves the worker survived.
        var validator = new FakeFileValidator((path, _) => path == "bad.ndjson"
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(Report));

        await RunWorkerAsync(validator, async () =>
        {
            var bad = Submit("bad.ndjson");
            await WaitForStatusAsync(bad, JobStatusType.Failed);
            Assert.Equal("boom", bad.GetSnapshot().Error);
            Assert.Null(bad.GetSnapshot().Report);

            var good = Submit("good.ndjson");
            await WaitForStatusAsync(good, JobStatusType.Succeeded);
        });
    }

    [Fact]
    public async Task JobCancelledWhileQueued_IsSkipped_AndNeverValidated()
    {
        var gate = new ValidationGate(Report);
        var validator = new FakeFileValidator((path, ct) => path == "first.ndjson"
            ? gate.WaitAsync(ct)
            : Task.FromResult(Report));

        await RunWorkerAsync(validator, async () =>
        {
            // Hold the worker on the first job, so the second one is still waiting in the queue.
            var first = Submit("first.ndjson");
            await gate.Started.WaitAsync(Timeout);

            var second = Submit("second.ndjson");
            Assert.True(second.TryCancel());

            gate.Release();
            await WaitForStatusAsync(first, JobStatusType.Succeeded);

            // A third job proves the worker already moved past the cancelled one.
            var third = Submit("third.ndjson");
            await WaitForStatusAsync(third, JobStatusType.Succeeded);

            Assert.Equal(JobStatusType.Cancelled, second.GetSnapshot().Status);
            Assert.DoesNotContain("second.ndjson", validator.Calls);
        });
    }

    [Fact]
    public async Task JobCancelledWhileRunning_EndsCancelled_ValidatorSeesTheToken_AndTheNextJobRuns()
    {
        var gate = new ValidationGate(Report);
        var validator = new FakeFileValidator((path, ct) => path == "slow.ndjson"
            ? gate.WaitAsync(ct)
            : Task.FromResult(Report));

        await RunWorkerAsync(validator, async () =>
        {
            var slow = Submit("slow.ndjson");
            await gate.Started.WaitAsync(Timeout);

            Assert.True(slow.TryCancel());

            var next = Submit("next.ndjson");
            await WaitForStatusAsync(next, JobStatusType.Succeeded);

            Assert.Equal(JobStatusType.Cancelled, slow.GetSnapshot().Status);
            Assert.Null(slow.GetSnapshot().Report);
            Assert.True(gate.SawCancellation);
        });
    }

    [Fact]
    public async Task UnknownJobId_IsSkipped_AndTheNextJobStillSucceeds()
    {
        var validator = FakeFileValidator.Returning(Report);

        await RunWorkerAsync(validator, async () =>
        {
            Assert.True(_queue.TryEnqueue(Guid.NewGuid()));

            var job = Submit("a.ndjson");
            await WaitForStatusAsync(job, JobStatusType.Succeeded);

            Assert.Single(validator.Calls);
        });
    }

    [Fact]
    public async Task StopAsync_WhileAJobIsRunning_FinishesWithoutHanging()
    {
        var gate = new ValidationGate(Report);
        var validator = new FakeFileValidator((_, ct) => gate.WaitAsync(ct));
        using var worker = new JobWorker(_registry, _queue, validator, NullLogger<JobWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var job = Submit("slow.ndjson");
        await gate.Started.WaitAsync(Timeout);

        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.True(gate.SawCancellation);
        // Shutdown doesn't decide the job's outcome: it stays Running (state is in memory and dies with the app).
        Assert.Equal(JobStatusType.Running, job.GetSnapshot().Status);
    }

    // ---------- Helpers ----------

    /// <summary>Starts the worker, runs the test body, and always stops the worker afterwards.</summary>
    private async Task RunWorkerAsync(IFileValidator validator, Func<Task> body)
    {
        using var worker = new JobWorker(_registry, _queue, validator, NullLogger<JobWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await body();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        }
    }

    /// <summary>Registers a job and puts its ID on the queue, the same order the POST endpoint will use.</summary>
    private JobEntry Submit(string filePath)
    {
        var job = new JobEntry(Guid.NewGuid(), "orders", filePath);
        _registry.Add(job);
        Assert.True(_queue.TryEnqueue(job.Id), "The job queue was unexpectedly full.");
        return job;
    }

    private static async Task WaitForStatusAsync(JobEntry job, JobStatusType expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        var status = job.GetSnapshot().Status;

        while (status != expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Job {job.Id} ({job.FilePath}) expected {expected} within {Timeout.TotalSeconds}s, last status was {status}.");
            }

            await Task.Delay(50);
            status = job.GetSnapshot().Status;
        }
    }
}
