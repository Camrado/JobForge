using JobForge.Core.Dtos;
using JobForge.Core.Models;
using JobForge.Core.Report;
using JobForge.Core.Validation.LineValidator;

namespace JobForge.Core.Validation.FileValidator;

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
