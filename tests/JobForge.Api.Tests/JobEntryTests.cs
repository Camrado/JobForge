using JobForge.Api.Models;
using JobForge.Core.Dtos;
using JobForge.Core.Models;
using Xunit;

namespace JobForge.Api.Tests;

public class JobEntryTests
{
    private static readonly ValidationReportDto Report = new(
        Total: 5,
        Valid: 2,
        Invalid: 3,
        ErrorCounts: new Dictionary<ValidationErrorType, int> { [ValidationErrorType.InvalidAmount] = 3 },
        Samples: []);

    // ---------- Initial state ----------

    [Fact]
    public void NewJob_IsQueued_WithNoReportOrError()
    {
        var job = CreateJob();

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Queued, snapshot.Status);
        Assert.Null(snapshot.Report);
        Assert.Null(snapshot.Error);
        Assert.False(job.Token.IsCancellationRequested);
    }

    // ---------- Valid transitions ----------

    [Fact]
    public void TryStart_OnQueuedJob_SetsRunning()
    {
        var job = CreateJob();

        Assert.True(job.TryStart());
        Assert.Equal(JobStatusType.Running, job.GetSnapshot().Status);
    }

    [Fact]
    public void TrySucceed_OnRunningJob_SetsSucceededAndStoresReport()
    {
        var job = CreateRunningJob();

        Assert.True(job.TrySucceed(Report));

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Succeeded, snapshot.Status);
        Assert.Same(Report, snapshot.Report);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void TryFail_OnRunningJob_SetsFailedAndStoresError()
    {
        var job = CreateRunningJob();

        Assert.True(job.TryFail("disk on fire"));

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Failed, snapshot.Status);
        Assert.Equal("disk on fire", snapshot.Error);
        Assert.Null(snapshot.Report);
    }

    [Fact]
    public void TryCancel_OnQueuedJob_SetsCancelledAndCancelsToken()
    {
        var job = CreateJob();

        Assert.True(job.TryCancel());

        Assert.Equal(JobStatusType.Cancelled, job.GetSnapshot().Status);
        Assert.True(job.Token.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_OnRunningJob_SetsCancelledAndCancelsToken()
    {
        var job = CreateRunningJob();

        Assert.True(job.TryCancel());

        Assert.Equal(JobStatusType.Cancelled, job.GetSnapshot().Status);
        Assert.True(job.Token.IsCancellationRequested);
    }

    // ---------- Invalid transitions return false and change nothing ----------

    [Fact]
    public void TryStart_Twice_SecondReturnsFalse_AndStaysRunning()
    {
        var job = CreateRunningJob();

        Assert.False(job.TryStart());
        Assert.Equal(JobStatusType.Running, job.GetSnapshot().Status);
    }

    [Fact]
    public void TrySucceed_OnQueuedJob_ReturnsFalse_AndStaysQueued()
    {
        var job = CreateJob();

        Assert.False(job.TrySucceed(Report));

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Queued, snapshot.Status);
        Assert.Null(snapshot.Report);
    }

    [Fact]
    public void TryFail_OnQueuedJob_ReturnsFalse_AndStaysQueued()
    {
        var job = CreateJob();

        Assert.False(job.TryFail("boom"));

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Queued, snapshot.Status);
        Assert.Null(snapshot.Error);
    }

    [Theory]
    [InlineData(JobStatusType.Succeeded)]
    [InlineData(JobStatusType.Failed)]
    [InlineData(JobStatusType.Cancelled)]
    public void FinishedJob_RejectsEveryTransition_AndKeepsItsState(JobStatusType finalStatus)
    {
        var job = CreateFinishedJob(finalStatus);
        var before = job.GetSnapshot();

        Assert.False(job.TryStart());
        Assert.False(job.TrySucceed(Report with { Total = 99 }));
        Assert.False(job.TryFail("too late"));
        Assert.False(job.TryCancel());

        Assert.Equal(before, job.GetSnapshot());
    }

    // ---------- Races between cancel and finish ----------

    [Fact]
    public void CancelThenSucceed_StaysCancelled_WithNoReport()
    {
        var job = CreateRunningJob();

        Assert.True(job.TryCancel());
        Assert.False(job.TrySucceed(Report));

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Cancelled, snapshot.Status);
        Assert.Null(snapshot.Report);
    }

    [Fact]
    public void SucceedThenCancel_StaysSucceeded_KeepsReport_AndTokenIsNotCancelled()
    {
        var job = CreateRunningJob();

        Assert.True(job.TrySucceed(Report));
        Assert.False(job.TryCancel());

        var snapshot = job.GetSnapshot();
        Assert.Equal(JobStatusType.Succeeded, snapshot.Status);
        Assert.Same(Report, snapshot.Report);
        Assert.False(job.Token.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_Twice_SecondReturnsFalse_AndDoesNotThrow()
    {
        var job = CreateRunningJob();

        Assert.True(job.TryCancel());
        Assert.False(job.TryCancel());

        Assert.Equal(JobStatusType.Cancelled, job.GetSnapshot().Status);
    }

    [Fact]
    public void TryStart_AfterCancelWhileQueued_ReturnsFalse_SoTheWorkerSkipsIt()
    {
        var job = CreateJob();
        job.TryCancel();

        Assert.False(job.TryStart());
        Assert.Equal(JobStatusType.Cancelled, job.GetSnapshot().Status);
    }

    [Fact]
    public async Task SucceedAndCancelAtTheSameTime_ExactlyOneWins_AndStateIsConsistent()
    {
        for (var i = 0; i < 200; i++)
        {
            var job = CreateRunningJob();
            using var startTogether = new Barrier(2);

            var succeed = Task.Run(() =>
            {
                startTogether.SignalAndWait();
                return job.TrySucceed(Report);
            });
            var cancel = Task.Run(() =>
            {
                startTogether.SignalAndWait();
                return job.TryCancel();
            });

            var results = await Task.WhenAll(succeed, cancel).WaitAsync(TimeSpan.FromSeconds(5));
            var succeeded = results[0];
            var cancelled = results[1];

            Assert.True(succeeded ^ cancelled, $"Iteration {i}: succeeded={succeeded}, cancelled={cancelled}");

            var snapshot = job.GetSnapshot();
            if (succeeded)
            {
                Assert.Equal(JobStatusType.Succeeded, snapshot.Status);
                Assert.Same(Report, snapshot.Report);
            }
            else
            {
                Assert.Equal(JobStatusType.Cancelled, snapshot.Status);
                Assert.Null(snapshot.Report);
            }
        }
    }

    // ---------- Helpers ----------

    private static JobEntry CreateJob() => new(Guid.NewGuid(), "orders", "/data/orders.ndjson");

    private static JobEntry CreateRunningJob()
    {
        var job = CreateJob();
        Assert.True(job.TryStart());
        return job;
    }

    private static JobEntry CreateFinishedJob(JobStatusType finalStatus)
    {
        var job = CreateRunningJob();
        var finished = finalStatus switch
        {
            JobStatusType.Succeeded => job.TrySucceed(Report),
            JobStatusType.Failed => job.TryFail("boom"),
            JobStatusType.Cancelled => job.TryCancel(),
            _ => throw new ArgumentOutOfRangeException(nameof(finalStatus)),
        };
        Assert.True(finished);
        return job;
    }
}
