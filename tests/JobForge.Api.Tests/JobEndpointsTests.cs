using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobForge.Api.Dtos;
using JobForge.Api.Models;
using JobForge.Api.Tests.Fakes;
using JobForge.Core.Dtos;
using JobForge.Core.Models;
using Xunit;

namespace JobForge.Api.Tests;

/// <summary>
/// Calls the real endpoints over HTTP (in memory) with a fake validator. Checkpoints 7–10.
/// </summary>
public class JobEndpointsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly ValidationReportDto Report = new(
        Total: 5,
        Valid: 2,
        Invalid: 3,
        ErrorCounts: new Dictionary<ValidationErrorType, int>
        {
            [ValidationErrorType.MalformedJson] = 1,
            [ValidationErrorType.InvalidAmount] = 2,
        },
        Samples: [new InvalidLineDto(2, [ValidationErrorType.InvalidAmount])]);

    // ---------- Submit ----------

    [Fact]
    public async Task Post_ValidDataset_Returns202_WithLocationAndJobStatus()
    {
        using var api = new JobApiFactory(FakeFileValidator.Returning(Report));
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/jobs", new { datasetId = "orders" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await ReadAsync<JobStatusDto>(response);
        Assert.Equal($"/jobs/{job.Id}", response.Headers.Location?.ToString());
        Assert.Equal("orders", job.DatasetId);
        Assert.Contains(job.Status, new[] { JobStatusType.Queued, JobStatusType.Running, JobStatusType.Succeeded });
    }

    [Fact]
    public async Task HappyPath_JobSucceeds_AndReportMatchesTheValidatorsReport()
    {
        using var api = new JobApiFactory(FakeFileValidator.Returning(Report));
        var client = api.CreateClient();

        var id = await SubmitAsync(client, "orders");
        await WaitForStatusAsync(client, id, JobStatusType.Succeeded);

        var response = await client.GetAsync($"/jobs/{id}/report");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSameReport(Report, await ReadAsync<ValidationReportDto>(response));
    }

    [Theory]
    [InlineData("""{"datasetId":"nope"}""")]
    [InlineData("""{"datasetId":"  "}""")]
    [InlineData("""{}""")]
    public async Task Post_UnknownOrMissingDataset_Returns400(string body)
    {
        var validator = FakeFileValidator.Returning(Report);
        using var api = new JobApiFactory(validator);
        var client = api.CreateClient();

        var response = await client.PostAsync("/jobs", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(validator.Calls);
    }

    [Fact]
    public async Task UnknownJobId_Returns404_OnStatusReportAndCancel()
    {
        using var api = new JobApiFactory(FakeFileValidator.Returning(Report));
        var client = api.CreateClient();
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/jobs/{unknown}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/jobs/{unknown}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/jobs/{unknown}/cancel", null)).StatusCode);
    }

    // ---------- Checkpoint 7: report only after success ----------

    [Fact]
    public async Task Report_WhileJobIsRunning_Returns409_ThenReturns200AfterItSucceeds()
    {
        var gate = new ValidationGate(Report);
        using var api = new JobApiFactory(new FakeFileValidator((_, ct) => gate.WaitAsync(ct)));
        var client = api.CreateClient();

        var id = await SubmitAsync(client, "orders");
        await gate.Started.WaitAsync(Timeout);

        Assert.Equal(JobStatusType.Running, (await GetStatusAsync(client, id)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/jobs/{id}/report")).StatusCode);

        gate.Release();
        await WaitForStatusAsync(client, id, JobStatusType.Succeeded);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/jobs/{id}/report")).StatusCode);
    }

    // ---------- Checkpoint 8: a full queue rejects work ----------

    [Fact]
    public async Task QueueFull_FourthJobGets503WithRetryAfter_AndTheAcceptedJobsStillFinish()
    {
        var gate = new ValidationGate(Report);
        var calls = 0;
        // Only the first job waits on the gate; the rest finish immediately.
        var validator = new FakeFileValidator((_, ct) => Interlocked.Increment(ref calls) == 1
            ? gate.WaitAsync(ct)
            : Task.FromResult(Report));
        using var api = new JobApiFactory(validator);
        var client = api.CreateClient();

        var first = await SubmitAsync(client, "large-10x");
        await gate.Started.WaitAsync(Timeout);
        var second = await SubmitAsync(client, "large-10x");
        var third = await SubmitAsync(client, "large-10x");

        Assert.Equal(JobStatusType.Queued, (await GetStatusAsync(client, second)).Status);
        Assert.Equal(JobStatusType.Queued, (await GetStatusAsync(client, third)).Status);

        var rejected = await client.PostAsJsonAsync("/jobs", new { datasetId = "large-10x" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        Assert.Null(rejected.Headers.Location);

        gate.Release();
        await WaitForStatusAsync(client, first, JobStatusType.Succeeded);
        await WaitForStatusAsync(client, second, JobStatusType.Succeeded);
        await WaitForStatusAsync(client, third, JobStatusType.Succeeded);
        Assert.Equal(3, validator.Calls.Count);
    }

    // ---------- Checkpoint 9: cancel queued and running work ----------

    [Fact]
    public async Task CancelQueuedJob_Returns200Cancelled_AndItIsNeverValidated()
    {
        var gate = new ValidationGate(Report);
        var calls = 0;
        var validator = new FakeFileValidator((_, ct) => Interlocked.Increment(ref calls) == 1
            ? gate.WaitAsync(ct)
            : Task.FromResult(Report));
        using var api = new JobApiFactory(validator);
        var client = api.CreateClient();

        var running = await SubmitAsync(client, "orders");
        await gate.Started.WaitAsync(Timeout);
        var queued = await SubmitAsync(client, "orders");

        var cancel = await client.PostAsync($"/jobs/{queued}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(JobStatusType.Cancelled, (await ReadAsync<JobStatusDto>(cancel)).Status);

        gate.Release();
        await WaitForStatusAsync(client, running, JobStatusType.Succeeded);

        // A later job proves the worker has already moved past the cancelled one.
        var later = await SubmitAsync(client, "orders");
        await WaitForStatusAsync(client, later, JobStatusType.Succeeded);

        Assert.Equal(JobStatusType.Cancelled, (await GetStatusAsync(client, queued)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/jobs/{queued}/report")).StatusCode);
        Assert.Equal(2, validator.Calls.Count); // the running job and the later one, never the cancelled one
    }

    [Fact]
    public async Task CancelRunningJob_Returns200Cancelled_ValidatorSeesTheToken_AndStatusStaysCancelled()
    {
        var gate = new ValidationGate(Report);
        using var api = new JobApiFactory(new FakeFileValidator((_, ct) => gate.WaitAsync(ct)));
        var client = api.CreateClient();

        var id = await SubmitAsync(client, "orders");
        await gate.Started.WaitAsync(Timeout);

        var cancel = await client.PostAsync($"/jobs/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(JobStatusType.Cancelled, (await ReadAsync<JobStatusDto>(cancel)).Status);

        gate.Release(); // too late: the job is already decided
        await WaitUntilAsync(() => gate.SawCancellation, "the validator to see its token cancelled");

        Assert.Equal(JobStatusType.Cancelled, (await GetStatusAsync(client, id)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/jobs/{id}/report")).StatusCode);
    }

    [Fact]
    public async Task Cancel_IsIdempotent_AndCancellingASucceededJobKeepsItSucceeded()
    {
        var gate = new ValidationGate(Report);
        var calls = 0;
        var validator = new FakeFileValidator((_, ct) => Interlocked.Increment(ref calls) == 1
            ? gate.WaitAsync(ct)
            : Task.FromResult(Report));
        using var api = new JobApiFactory(validator);
        var client = api.CreateClient();

        var cancelled = await SubmitAsync(client, "orders");
        await gate.Started.WaitAsync(Timeout);
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PostAsync($"/jobs/{cancelled}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(JobStatusType.Cancelled, (await ReadAsync<JobStatusDto>(response)).Status);
        }

        var succeeded = await SubmitAsync(client, "orders");
        await WaitForStatusAsync(client, succeeded, JobStatusType.Succeeded);

        var late = await client.PostAsync($"/jobs/{succeeded}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal(JobStatusType.Succeeded, (await ReadAsync<JobStatusDto>(late)).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/jobs/{succeeded}/report")).StatusCode);
    }

    // ---------- Checkpoint 10: one failure doesn't stop the worker ----------

    [Fact]
    public async Task FailingJob_ShowsFailedWithError_AndALaterJobStillSucceeds()
    {
        var calls = 0;
        var validator = new FakeFileValidator((_, _) => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("boom")
            : Task.FromResult(Report));
        using var api = new JobApiFactory(validator);
        var client = api.CreateClient();

        var failed = await SubmitAsync(client, "orders");
        var failedStatus = await WaitForStatusAsync(client, failed, JobStatusType.Failed);
        Assert.Equal("boom", failedStatus.Error);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync($"/jobs/{failed}/report")).StatusCode);

        var next = await SubmitAsync(client, "orders");
        await WaitForStatusAsync(client, next, JobStatusType.Succeeded);
    }

    // ---------- JSON shape ----------

    [Fact]
    public async Task StatusJson_UsesTextStatuses_AndNeverExposesTheFilePath()
    {
        using var api = new JobApiFactory(FakeFileValidator.Returning(Report));
        var client = api.CreateClient();

        var id = await SubmitAsync(client, "orders");
        await WaitForStatusAsync(client, id, JobStatusType.Succeeded);

        var json = await client.GetStringAsync($"/jobs/{id}");
        Assert.Contains("\"status\":\"Succeeded\"", json);
        Assert.DoesNotContain("test-data", json);
        Assert.DoesNotContain(".ndjson", json);
        Assert.DoesNotContain("filePath", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Helpers ----------

    private static async Task<Guid> SubmitAsync(HttpClient client, string datasetId)
    {
        var response = await client.PostAsJsonAsync("/jobs", new { datasetId });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await ReadAsync<JobStatusDto>(response)).Id;
    }

    private static async Task<JobStatusDto> GetStatusAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/jobs/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<JobStatusDto>(response);
    }

    private static async Task<JobStatusDto> WaitForStatusAsync(HttpClient client, Guid id, JobStatusType expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        var job = await GetStatusAsync(client, id);

        while (job.Status != expected)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Job {id} expected {expected} within {Timeout.TotalSeconds}s, last status was {job.Status}.");
            }

            await Task.Delay(50);
            job = await GetStatusAsync(client, id);
        }

        return job;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out after {Timeout.TotalSeconds}s waiting for {what}.");
            }

            await Task.Delay(50);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private static void AssertSameReport(ValidationReportDto expected, ValidationReportDto actual)
    {
        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.Valid, actual.Valid);
        Assert.Equal(expected.Invalid, actual.Invalid);
        Assert.Equal(expected.ErrorCounts, actual.ErrorCounts);
        Assert.Equal(expected.Samples.Count, actual.Samples.Count);
        for (var i = 0; i < expected.Samples.Count; i++)
        {
            Assert.Equal(expected.Samples[i].LineNumber, actual.Samples[i].LineNumber);
            Assert.Equal(expected.Samples[i].Errors, actual.Samples[i].Errors);
        }
    }
}
