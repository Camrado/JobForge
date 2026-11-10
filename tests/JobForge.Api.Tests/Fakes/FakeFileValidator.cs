using System.Collections.Concurrent;
using JobForge.Core.Dtos;
using JobForge.Core.Validation.FileValidator;

namespace JobForge.Api.Tests.Fakes;

/// <summary>
/// A scripted IFileValidator. Each call runs the given behavior and is recorded,
/// so tests can check which files were validated and how often.
/// </summary>
public sealed class FakeFileValidator(Func<string, CancellationToken, Task<ValidationReportDto>> behavior) : IFileValidator
{
    private readonly ConcurrentQueue<string> _calls = new();

    public IReadOnlyCollection<string> Calls => _calls;

    public Task<ValidationReportDto> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        _calls.Enqueue(path);
        return behavior(path, cancellationToken);
    }

    /// <summary>Returns the report immediately.</summary>
    public static FakeFileValidator Returning(ValidationReportDto report) =>
        new((_, _) => Task.FromResult(report));

    /// <summary>Throws InvalidOperationException("boom") for every call.</summary>
    public static FakeFileValidator Throwing() =>
        new((_, _) => throw new InvalidOperationException("boom"));
}
