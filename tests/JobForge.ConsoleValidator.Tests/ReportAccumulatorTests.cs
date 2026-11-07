using JobForge.ConsoleValidator.Validation;
using Xunit;

namespace JobForge.ConsoleValidator.Tests;

public class ReportAccumulatorTests
{
    private static readonly ValidationErrorType[] Amount = [ValidationErrorType.InvalidAmount];

    [Fact]
    public void Empty_ProducesZeroReportWithAllCategories()
    {
        var report = new ReportAccumulator().ToReport();

        Assert.Equal(0, report.Total);
        Assert.Equal(Enum.GetValues<ValidationErrorType>().Length, report.ErrorCounts.Count);
        Assert.All(report.ErrorCounts.Values, count => Assert.Equal(0, count));
        Assert.Empty(report.Samples);
    }

    [Fact]
    public void Add_CountsLineOnceAndEveryError()
    {
        var acc = new ReportAccumulator();

        acc.Add(1, []);
        acc.Add(2, [ValidationErrorType.InvalidId, ValidationErrorType.UnsupportedCurrency]);

        var report = acc.ToReport();
        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Valid);
        Assert.Equal(1, report.Invalid);
        Assert.Equal(1, report.ErrorCounts[ValidationErrorType.InvalidId]);
        Assert.Equal(1, report.ErrorCounts[ValidationErrorType.UnsupportedCurrency]);
        Assert.Equal(2, Assert.Single(report.Samples).Errors.Count);
    }

    [Fact]
    public void Add_OutOfOrder_KeepsLowestTwentyLinesSorted()
    {
        var acc = new ReportAccumulator();

        // 50 down to 1: the worst order for a "first 20 wins" approach.
        for (var line = 50; line >= 1; line--)
        {
            acc.Add(line, Amount);
        }

        var report = acc.ToReport();
        Assert.Equal(50, report.ErrorCounts[ValidationErrorType.InvalidAmount]);
        Assert.Equal(Enumerable.Range(1, 20), report.Samples.Select(s => s.LineNumber));
    }

    [Fact]
    public void Merge_CombinesCountsAndPicksLowestTwentyOverall()
    {
        // Simulates two consumers: one got the odd lines, the other the even lines.
        var odd = new ReportAccumulator();
        var even = new ReportAccumulator();
        for (var line = 1; line <= 60; line++)
        {
            (line % 2 == 1 ? odd : even).Add(line, Amount);
        }
        odd.Add(61, []);

        var total = new ReportAccumulator();
        total.Merge(odd);
        total.Merge(even);

        var report = total.ToReport();
        Assert.Equal(61, report.Total);
        Assert.Equal(1, report.Valid);
        Assert.Equal(60, report.Invalid);
        Assert.Equal(60, report.ErrorCounts[ValidationErrorType.InvalidAmount]);
        Assert.Equal(Enumerable.Range(1, 20), report.Samples.Select(s => s.LineNumber));
    }
}
