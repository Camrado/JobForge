using JobForge.Core.Models;

namespace JobForge.Core.Validation.FileValidator;

/// <summary>
/// Reads a whole NDJSON file and aggregates the per-line results into a report.
/// 1A: SequentialFileValidator. 1B: a concurrent implementation behind the same interface.
/// </summary>
public interface IFileValidator
{
    Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken);
}
