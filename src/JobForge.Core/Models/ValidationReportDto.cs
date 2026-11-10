namespace JobForge.Core.Models;

public sealed record ValidationReportDto(
    int Total,
    int Valid,
    int Invalid,
    IReadOnlyDictionary<ValidationErrorType, int> ErrorCounts,
    IReadOnlyList<InvalidLineDto> Samples);
