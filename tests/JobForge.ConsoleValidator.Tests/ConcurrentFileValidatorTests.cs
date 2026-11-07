using JobForge.ConsoleValidator.Output;
using JobForge.ConsoleValidator.Validation;
using Xunit;

namespace JobForge.ConsoleValidator.Tests;

public class ConcurrentFileValidatorTests : IDisposable
{
    // Every await on the validator uses this, so a hang fails the test instead of freezing the run.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string ValidLine = """{"id":"a","currency":"USD","amount":1}""";

    private readonly SequentialFileValidator _sequential = new(new LineValidator());
    private readonly List<string> _tempFiles = [];

    // ---------- Checkpoint 3: same result as the sequential validator ----------

    [Fact]
    public async Task SameReport_FiveLineFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "orders.ndjson");

        await AssertSameReportAsync(path, new LineValidator());
    }

    [Fact]
    public async Task SameReport_TwentyFiveInvalidLines()
    {
        var path = CreateTempFile(Enumerable.Repeat("""{"id":"a","currency":"USD","amount":-1}""", 25));

        await AssertSameReportAsync(path, new LineValidator());
    }

    [Fact]
    public async Task SameReport_SeededFile_WithRandomDelaysPerLine()
    {
        var path = CreateTempFile(GenerateSeededLines(count: 2000, seed: 42));

        // Different small delays per line, so completion order differs from file order.
        await AssertSameReportAsync(path, new RandomDelayLineValidator());
    }

    [LocalFileFact("large-orders.ndjson")]
    [Trait("Category", "Large")]
    public async Task SameReport_LargeLocalFile()
    {
        var path = LocalFiles.PathOf("large-orders.ndjson")!;

        await AssertSameReportAsync(path, new LineValidator(), TimeSpan.FromSeconds(30));
    }

    // ---------- Checkpoint 4: backpressure and the concurrency limit ----------

    [Fact]
    public async Task Backpressure_AtMostFourActive_AndNothingLost()
    {
        var path = CreateTempFile(Enumerable.Repeat(ValidLine, 200));
        using var gate = new GatedLineValidator();
        var sut = new ConcurrentFileValidator(gate);

        var run = sut.ValidateAsync(path, CancellationToken.None);

        await gate.WaitForStartedAsync(4, Timeout);
        await Task.Delay(200); // give a 5th call the chance to start, if the limit were broken

        Assert.Equal(4, gate.Started);
        Assert.True(gate.PeakActive <= 4, $"Peak active was {gate.PeakActive}");
        Assert.False(run.IsCompleted);

        // Release one consumer: it finishes its record, takes another and blocks again.
        gate.ReleaseOne();
        await gate.WaitForStartedAsync(5, Timeout);
        Assert.True(gate.PeakActive <= 4, $"Peak active was {gate.PeakActive}");

        gate.Open();
        var report = await run.WaitAsync(Timeout);

        Assert.Equal(200, report.Total);
        Assert.Equal(200, report.Valid);
        Assert.True(gate.PeakActive <= 4, $"Peak active was {gate.PeakActive}");
        // Not asserted: that the producer's 33rd write is pending. See the note in the report.
    }

    // ---------- Checkpoint 5: cancel while the producer is blocked ----------

    [Fact]
    public async Task CancelWhileBlocked_ThrowsOperationCanceled_AndReturnsNoReport()
    {
        var path = CreateTempFile(Enumerable.Repeat(ValidLine, 200));
        using var gate = new GatedLineValidator();
        var sut = new ConcurrentFileValidator(gate);
        using var callerCts = new CancellationTokenSource();

        var run = sut.ValidateAsync(path, callerCts.Token);
        await gate.WaitForStartedAsync(4, Timeout);

        await callerCts.CancelAsync();
        // A blocked Validate call can't see the token, so let the consumer threads go.
        gate.Open();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
        Assert.Equal(0, gate.Active);
    }

    // ---------- Checkpoint 6: a consumer fails while the producer is blocked ----------

    [Fact]
    public async Task ConsumerFailsWhileBlocked_CallerSeesTheOriginalException()
    {
        var path = CreateTempFile(Enumerable.Repeat(ValidLine, 200));
        using var gate = new GatedLineValidator();
        var sut = new ConcurrentFileValidator(gate);

        var run = sut.ValidateAsync(path, CancellationToken.None);
        await gate.WaitForStartedAsync(4, Timeout);

        gate.ReleaseOneAndThrow();
        await gate.Thrown.WaitAsync(Timeout);
        // The other three calls are blocked and can't see the stop signal, so let them go.
        gate.Open();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(Timeout));
        Assert.Equal("boom", ex.Message);
        Assert.Equal(0, gate.Active);
    }

    // ---------- Edge cases ----------

    [Fact]
    public async Task MissingFile_ThrowsFileNotFound_WithoutHanging()
    {
        var sut = new ConcurrentFileValidator(new LineValidator());
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ndjson");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.ValidateAsync(path, CancellationToken.None).WaitAsync(Timeout));
    }

    [Fact]
    public async Task EmptyFile_ProducesEmptyReport()
    {
        var sut = new ConcurrentFileValidator(new LineValidator());
        var path = CreateTempFile();

        var report = await sut.ValidateAsync(path, CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(0, report.Total);
        Assert.Equal(0, report.Valid);
        Assert.Equal(0, report.Invalid);
        Assert.All(report.ErrorCounts.Values, count => Assert.Equal(0, count));
        Assert.Empty(report.Samples);
    }

    // ---------- Helpers ----------

    private async Task AssertSameReportAsync(string path, ILineValidator concurrentLineValidator, TimeSpan? timeout = null)
    {
        var expected = await _sequential.ValidateAsync(path, CancellationToken.None);
        var actual = await new ConcurrentFileValidator(concurrentLineValidator)
            .ValidateAsync(path, CancellationToken.None)
            .WaitAsync(timeout ?? Timeout);

        AssertSameReport(expected, actual);
    }

    private static void AssertSameReport(ValidationReportDto expected, ValidationReportDto actual)
    {
        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.Valid, actual.Valid);
        Assert.Equal(expected.Invalid, actual.Invalid);

        foreach (var errorType in Enum.GetValues<ValidationErrorType>())
        {
            Assert.Equal(expected.ErrorCounts[errorType], actual.ErrorCounts[errorType]);
        }

        Assert.Equal(expected.Samples.Count, actual.Samples.Count);
        for (var i = 0; i < expected.Samples.Count; i++)
        {
            Assert.Equal(expected.Samples[i].LineNumber, actual.Samples[i].LineNumber);
            Assert.Equal(expected.Samples[i].Errors, actual.Samples[i].Errors);
        }
    }

    private static IEnumerable<string> GenerateSeededLines(int count, int seed)
    {
        var random = new Random(seed);
        for (var i = 1; i <= count; i++)
        {
            yield return random.Next(10) switch
            {
                0 => """{"id":"a","currency":"USD","amount":-5}""",
                1 => """{"id":" ","currency":"GBP","amount":3}""",
                2 => """{"id":"a","currency":"usd","amount":0}""",
                3 => """{"id":"a","currency":"USD","amount":""",
                _ => $$"""{"id":"ord-{{i}}","currency":"EUR","amount":{{random.Next(1, 1000)}}}""",
            };
        }
    }

    private string CreateTempFile(IEnumerable<string>? lines = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ndjson");
        File.WriteAllLines(path, lines ?? []);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    /// <summary>The real rules, plus a random 0–2 ms pause per line so lines finish out of file order.</summary>
    private sealed class RandomDelayLineValidator : ILineValidator
    {
        private readonly LineValidator _inner = new();

        public IReadOnlyList<ValidationErrorType> Validate(string line)
        {
            Thread.Sleep(Random.Shared.Next(0, 3));
            return _inner.Validate(line);
        }
    }
}
