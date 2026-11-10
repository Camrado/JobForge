using JobForge.Core.Models;

namespace JobForge.Core.Dtos;

public sealed record ValidationReportDto(
    int Total,
    int Valid,
    int Invalid,
    IReadOnlyDictionary<ValidationErrorType, int> ErrorCounts,
    IReadOnlyList<InvalidLineDto> Samples);
