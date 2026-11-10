using JobForge.Api.Dtos;
using JobForge.Core.Dtos;

namespace JobForge.Api.Models;

public sealed class JobEntry
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    
    public Guid Id { get; }
    public string DatasetId { get; }
    public string FilePath { get; }
    public JobStatusType Status { get; private set; }
    public ValidationReportDto? Report { get; private set; }
    public string? Error { get; private set; }

    public CancellationToken Token { get; }

    public JobEntry(Guid id, string datasetId, string filePath)
    {
        Id = id;
        DatasetId = datasetId;
        FilePath = filePath;
        Token = _cts.Token;
    }

    public bool TryStart()
    {
        lock (_lock)
        {
            if (Status != JobStatusType.Queued)
            {
                return false;
            }

            Status = JobStatusType.Running;
            return true;
        }
    }

    public bool TrySucceed(ValidationReportDto report)
    {
        lock (_lock)
        {
            if (Status != JobStatusType.Running)
            {
                return false;
            }

            Status = JobStatusType.Succeeded;
            Report = report;
            _cts.Dispose();
            
            return true;
        }
    }

    public bool TryFail(string error)
    {
        lock (_lock)
        {
            if (Status != JobStatusType.Running)
            {
                return false;
            }

            Status = JobStatusType.Failed;
            Error = error;
            _cts.Dispose();
            
            return true;
        }
    }

    // Marks the job Cancelled right away, even if it's still running.
    // The worker may finish a moment later, but its result is ignored.
    public bool TryCancel()
    {
        lock (_lock)
        {
            if (Status is not (JobStatusType.Running or JobStatusType.Queued))
            {
                return false;
            }
            
            Status = JobStatusType.Cancelled;
        }
        
        _cts.Cancel();
        _cts.Dispose();
        return true;
    }
    
    public JobSnapshotDto GetSnapshot()
    {
        lock (_lock)
        {
            return new JobSnapshotDto(Status, Report, Error);
        }
    }
}