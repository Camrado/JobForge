using JobForge.Core.Models;

namespace JobForge.Core.Validation.LineValidator;

/// <summary>
/// Validates a single NDJSON line. No file I/O, no counters: the same rules are reused in 1B.
/// </summary>
public interface ILineValidator
{
    /// <returns>Every error found on the line. Empty means the line is valid.</returns>
    IReadOnlyList<ValidationErrorType> Validate(string line);
}
