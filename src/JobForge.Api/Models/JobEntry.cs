using JobForge.Core.Dtos;

namespace JobForge.Api.Models;

public sealed class JobEntry
{
    private readonly Lock _lock = new();
    
    public Guid Id { get; }
    public string DatasetId { get; }
    public string FilePath { get; }
    public JobStatusType Status { get; private set; }
    public ValidationReportDto? Report { get; private set; }
    public string? Error { get; private set; }
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    public JobEntry(Guid id, string datasetId, string filePath)
    {
        Id = id;
        DatasetId = datasetId;
        FilePath = filePath;
    }
}