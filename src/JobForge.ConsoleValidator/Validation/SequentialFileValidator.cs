using JobForge.ConsoleValidator.Output;

namespace JobForge.ConsoleValidator.Validation;

public sealed class SequentialFileValidator(ILineValidator lineValidator) : IFileValidator
{
    public async Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        var accumulator = new ReportAccumulator();

        using var reader = new StreamReader(path);
        string? line;

        var lineNumber = 0;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            lineNumber++;
            accumulator.Add(lineNumber, lineValidator.Validate(line));
        }

        return accumulator.ToReport();
    }
}
