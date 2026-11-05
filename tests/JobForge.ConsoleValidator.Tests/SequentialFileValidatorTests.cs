using JobForge.ConsoleValidator.Output;
using JobForge.ConsoleValidator.Validation;
using Xunit;

namespace JobForge.ConsoleValidator.Tests;

public class SequentialFileValidatorTests : IDisposable
{
    private readonly SequentialFileValidator _sut = new(new LineValidator());
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task FiveLineFixture_ProducesExpectedReport()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "orders.ndjson");

        var report = await _sut.ValidateAsync(path, CancellationToken.None);

        Assert.Equal(5, report.Total);
        Assert.Equal(2, report.Valid);
        Assert.Equal(3, report.Invalid);
        Assert.Equal(1, report.ErrorCounts[ValidationErrorType.InvalidAmount]);
        Assert.Equal(1, report.ErrorCounts[ValidationErrorType.InvalidId]);
        Assert.Equal(1, report.ErrorCounts[ValidationErrorType.UnsupportedCurrency]);
        Assert.Equal(1, report.ErrorCounts[ValidationErrorType.MalformedJson]);

        Assert.Equal([2, 3, 4], report.Samples.Select(s => s.LineNumber));
        Assert.Equal(2, report.Samples[1].Errors.Count);
    }

    [Fact]
    public async Task EmptyFile_ProducesEmptyReport()
    {
        var path = CreateTempFile();

        var report = await _sut.ValidateAsync(path, CancellationToken.None);

        Assert.Equal(0, report.Total);
        Assert.Equal(0, report.Valid);
        Assert.Equal(0, report.Invalid);
        Assert.All(report.ErrorCounts.Values, count => Assert.Equal(0, count));
        Assert.Empty(report.Samples);
    }

    [Fact]
    public async Task TwentyFiveInvalidLines_KeepsOnlyFirstTwentySamples()
    {
        var lines = Enumerable.Repeat("""{"id":"a","currency":"USD","amount":-1}""", 25);
        var path = CreateTempFile(lines);

        var report = await _sut.ValidateAsync(path, CancellationToken.None);

        Assert.Equal(25, report.Total);
        Assert.Equal(25, report.Invalid);
        Assert.Equal(25, report.ErrorCounts[ValidationErrorType.InvalidAmount]);
        Assert.Equal(Enumerable.Range(1, 20), report.Samples.Select(s => s.LineNumber));
    }

    [Fact]
    public async Task MissingFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ndjson");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sut.ValidateAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task CanceledToken_ThrowsOperationCanceled()
    {
        var path = CreateTempFile(["""{"id":"a","currency":"USD","amount":1}"""]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ValidateAsync(path, cts.Token));
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
}
