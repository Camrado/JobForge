using JobForge.Api.Dtos;
using JobForge.Core.Dtos;

namespace JobForge.Api.Persistence;

public interface IJobStore
{
    // Adds a Queued job unless maxQueued jobs are already waiting. False means the queue is full.
    Task<bool> TryAddAsync(Guid id, string datasetId, string filePath, int maxQueued, CancellationToken ct);

    // Returns the job, or null if the ID is unknown.
    Task<JobRecordDto?> GetAsync(Guid id, CancellationToken ct);

    // Takes the oldest Queued job and marks it Running. Null if none is waiting.
    Task<JobRecordDto?> TryClaimNextAsync(CancellationToken ct);

    // Running and not cancel-requested to Succeeded, with the report, in one update.
    Task<bool> TrySucceedAsync(Guid id, ValidationReportDto report, CancellationToken ct);

    // Running to Failed, with the error message.
    Task<bool> TryFailAsync(Guid id, string error, CancellationToken ct);

    // Queued to Cancelled.
    Task<bool> TryCancelQueuedAsync(Guid id, CancellationToken ct);

    // Running: sets cancel_requested. The status stays Running.
    Task<bool> TryRequestCancelAsync(Guid id, CancellationToken ct);

    // Running with cancel_requested to Cancelled.
    Task<bool> TryFinishCancelAsync(Guid id, CancellationToken ct);

    // Startup recovery. Single-process only: every Running job belongs to the dead process.
    Task RecoverInterruptedAsync(CancellationToken ct);
}
