using JobForge.Api.Models;
using JobForge.Core.Dtos;

namespace JobForge.Api.Dtos;

// One row of the jobs table, as the store returns it.
public sealed record JobRecordDto(
    Guid Id,
    string DatasetId,
    string FilePath,
    JobStatusType Status,
    bool CancelRequested,
    ValidationReportDto? Report,
    string? Error);
