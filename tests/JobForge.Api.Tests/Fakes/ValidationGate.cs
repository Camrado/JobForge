using JobForge.Core.Dtos;

namespace JobForge.Api.Tests.Fakes;

/// <summary>
/// Holds a validation call until the test releases it. The wait honors the job's token,
/// so a cancel or a shutdown wakes it up. Records whether it saw the token cancelled.
/// </summary>
public sealed class ValidationGate(ValidationReportDto report)
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the worker is inside the validator.</summary>
    public Task Started => _started.Task;

    public bool SawCancellation { get; private set; }

    public void Release() => _release.TrySetResult();

    public async Task<ValidationReportDto> WaitAsync(CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        try
        {
            await _release.Task.WaitAsync(cancellationToken);
            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SawCancellation = true;
            throw;
        }
    }
}
