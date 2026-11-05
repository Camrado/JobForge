using JobForge.ConsoleValidator.Output;
using JobForge.ConsoleValidator.Validation;
using Xunit;

namespace JobForge.ConsoleValidator.Tests;

/// <summary>
/// Runs the sequential validator over the large generated files in .files/ (gitignored, local only).
/// Expected numbers come from the seeded generator (seed 42), not from the validator's output.
/// Exclude with: dotnet test --filter "Category!=Large"
/// </summary>
[Trait("Category", "Large")]
public class LargeFileTests
{
    private const string OneMillionFile = "large-orders.ndjson";
    private const string TenMillionFile = "large-orders-10x.ndjson";

    private static readonly (int Line, ValidationErrorType[] Errors)[] ExpectedSamples =
    [
        (16, [ValidationErrorType.UnsupportedCurrency]),
        (34, [ValidationErrorType.InvalidAmount]),
        (36, [ValidationErrorType.UnsupportedCurrency]),
        (38, [ValidationErrorType.MalformedJson]),
        (50, [ValidationErrorType.InvalidAmount]),
        (61, [ValidationErrorType.InvalidId]),
        (83, [ValidationErrorType.UnsupportedCurrency]),
        (86, [ValidationErrorType.UnsupportedCurrency]),
        (95, [ValidationErrorType.UnsupportedCurrency, ValidationErrorType.InvalidAmount]),
        (110, [ValidationErrorType.UnsupportedCurrency, ValidationErrorType.InvalidAmount]),
        (119, [ValidationErrorType.InvalidId]),
        (149, [ValidationErrorType.InvalidAmount]),
        (152, [ValidationErrorType.InvalidId, ValidationErrorType.UnsupportedCurrency]),
        (158, [ValidationErrorType.UnsupportedCurrency]),
        (171, [ValidationErrorType.UnsupportedCurrency]),
        (174, [ValidationErrorType.MalformedJson]),
        (175, [ValidationErrorType.MalformedJson]),
        (179, [ValidationErrorType.InvalidId]),
        (183, [ValidationErrorType.InvalidAmount]),
        (190, [ValidationErrorType.UnsupportedCurrency]),
    ];

    private readonly SequentialFileValidator _sut = new(new LineValidator());

    [LocalFileFact(OneMillionFile)]
    public async Task OneMillionLines_ProducesGeneratorCounts()
    {
        var report = await _sut.ValidateAsync(LocalFiles.PathOf(OneMillionFile)!, CancellationToken.None);

        AssertCounts(report, multiplier: 1);
        AssertSamples(report);
    }

    [LocalFileFact(TenMillionFile)]
    public async Task TenMillionLines_ProducesTenTimesTheCounts_AndSameSamples()
    {
        var report = await _sut.ValidateAsync(LocalFiles.PathOf(TenMillionFile)!, CancellationToken.None);

        AssertCounts(report, multiplier: 10);
        AssertSamples(report);
    }

    private static void AssertCounts(ValidationReportDto report, int multiplier)
    {
        Assert.Equal(1_000_000 * multiplier, report.Total);
        Assert.Equal(900_048 * multiplier, report.Valid);
        Assert.Equal(99_952 * multiplier, report.Invalid);
        Assert.Equal(19_912 * multiplier, report.ErrorCounts[ValidationErrorType.MalformedJson]);
        Assert.Equal(30_207 * multiplier, report.ErrorCounts[ValidationErrorType.InvalidId]);
        Assert.Equal(40_084 * multiplier, report.ErrorCounts[ValidationErrorType.UnsupportedCurrency]);
        Assert.Equal(29_838 * multiplier, report.ErrorCounts[ValidationErrorType.InvalidAmount]);
    }

    private static void AssertSamples(ValidationReportDto report)
    {
        Assert.Equal(ExpectedSamples.Select(s => s.Line), report.Samples.Select(s => s.LineNumber));

        for (var i = 0; i < ExpectedSamples.Length; i++)
        {
            Assert.Equal(ExpectedSamples[i].Errors, report.Samples[i].Errors);
        }
    }
}
