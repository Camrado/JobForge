using JobForge.Api.Models;

namespace JobForge.Api.Dtos;

public sealed record JobStatusDto(Guid Id, string DatasetId, JobStatusType Status, string? Error);
