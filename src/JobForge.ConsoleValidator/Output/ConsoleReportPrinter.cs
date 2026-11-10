using JobForge.Core.Models;

namespace JobForge.ConsoleValidator.Output;

public sealed class ConsoleReportPrinter : IReportPrinter
{
    public void Print(ValidationReportDto report)
    {
        Console.WriteLine($"Total:   {report.Total}");
        Console.WriteLine($"Valid:   {report.Valid}");
        Console.WriteLine($"Invalid: {report.Invalid}");

        Console.WriteLine();
        Console.WriteLine("Errors by category:");
        foreach (var kind in Enum.GetValues<ValidationErrorType>())
        {
            Console.WriteLine($"  {kind}: {report.ErrorCounts.GetValueOrDefault(kind)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Samples ({report.Samples.Count}):");
        foreach (var sample in report.Samples)
        {
            Console.WriteLine($"  Line {sample.LineNumber}: {string.Join(", ", sample.Errors)}");
        }
    }
}
