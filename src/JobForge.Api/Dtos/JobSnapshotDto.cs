using JobForge.Api.Models;
using JobForge.Core.Dtos;

namespace JobForge.Api.Dtos;

public sealed record JobSnapshotDto(
    JobStatusType Status,
    ValidationReportDto? Report,
    string? Error);