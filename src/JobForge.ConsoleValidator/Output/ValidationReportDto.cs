using JobForge.ConsoleValidator.Validation;

namespace JobForge.ConsoleValidator.Output;

public sealed record ValidationReportDto(
    int Total,
    int Valid,
    int Invalid,
    IReadOnlyDictionary<ValidationErrorType, int> ErrorCounts,
    IReadOnlyList<InvalidLineSampleDto> Samples);
