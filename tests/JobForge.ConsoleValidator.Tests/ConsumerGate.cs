using JobForge.ConsoleValidator.Validation;

namespace JobForge.ConsoleValidator.Tests;

/// <summary>
/// Pass <see cref="OnRecordTaken"/> to ConcurrentValidatorOptions.OnRecordTaken.
/// Each consumer waits here after taking a record, until the test releases it.
/// The wait uses the validator's linked token, so a paused consumer wakes up on cancel or failure.
/// Tracks started, active and peak-active calls, and how many waits ended because of the stop signal.
/// </summary>
public sealed class ConsumerGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(0);
    private readonly TaskCompletionSource _thrown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _open;
    private int _throwOnNextRelease;
    private int _started;
    private int _active;
    private int _peakActive;
    private int _canceledWaits;

    public int Started => Volatile.Read(ref _started);
    public int Active => Volatile.Read(ref _active);
    public int PeakActive => Volatile.Read(ref _peakActive);

    /// <summary>Number of paused consumers that exited because their wait saw the stop signal.</summary>
    public int CanceledWaits => Volatile.Read(ref _canceledWaits);

    /// <summary>Completes once a released consumer has thrown the injected exception.</summary>
    public Task Thrown => _thrown.Task;

    public async Task OnRecordTaken(LineRecordDto record, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _started);
        UpdatePeak(Interlocked.Increment(ref _active));

        try
        {
            if (!_open)
            {
                await _gate.WaitAsync(cancellationToken);
            }

            if (Interlocked.Exchange(ref _throwOnNextRelease, 0) == 1)
            {
                _thrown.TrySetResult();
                throw new InvalidOperationException("boom");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _canceledWaits);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    /// <summary>Lets exactly one paused consumer continue.</summary>
    public void ReleaseOne() => _gate.Release();

    /// <summary>Lets exactly one paused consumer continue, and it throws InvalidOperationException("boom").</summary>
    public void ReleaseOneAndThrow()
    {
        Volatile.Write(ref _throwOnNextRelease, 1);
        _gate.Release();
    }

    /// <summary>Opens the gate for good: paused and future consumers pass straight through.</summary>
    public void Open()
    {
        _open = true;
        _gate.Release(1000);
    }

    public void Dispose()
    {
        Open();
        _gate.Dispose();
    }

    private void UpdatePeak(int candidate)
    {
        int current;
        while (candidate > (current = Volatile.Read(ref _peakActive)))
        {
            if (Interlocked.CompareExchange(ref _peakActive, candidate, current) == current)
            {
                return;
            }
        }
    }
}
