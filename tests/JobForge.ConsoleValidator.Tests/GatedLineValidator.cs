using JobForge.ConsoleValidator.Validation;

namespace JobForge.ConsoleValidator.Tests;

/// <summary>
/// Test double for ILineValidator. Every Validate call blocks at a gate until the test releases it,
/// so a test can hold the consumers still and fill the channel behind them.
/// Tracks started calls, active calls and the peak number of active calls.
/// Validate is synchronous, so a blocked call holds a thread-pool thread. That's fine for 4 consumers.
/// </summary>
public sealed class GatedLineValidator : ILineValidator, IDisposable
{
    // Safety net: a call never waits longer than this, so a broken test can't leave threads stuck forever.
    private static readonly TimeSpan MaxGateWait = TimeSpan.FromSeconds(10);

    private readonly LineValidator _inner = new();
    private readonly SemaphoreSlim _gate = new(0);
    private volatile bool _open;
    private int _throwOnNextRelease;
    private int _started;
    private int _active;
    private int _peakActive;
    private readonly TaskCompletionSource _thrown = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Started => Volatile.Read(ref _started);
    public int Active => Volatile.Read(ref _active);
    public int PeakActive => Volatile.Read(ref _peakActive);

    /// <summary>Completes once a released call has thrown the injected exception.</summary>
    public Task Thrown => _thrown.Task;

    public IReadOnlyList<ValidationErrorType> Validate(string line)
    {
        Interlocked.Increment(ref _started);
        var nowActive = Interlocked.Increment(ref _active);
        UpdatePeak(nowActive);

        try
        {
            if (!_open)
            {
                _gate.Wait(MaxGateWait);
            }

            if (Interlocked.Exchange(ref _throwOnNextRelease, 0) == 1)
            {
                _thrown.TrySetResult();
                throw new InvalidOperationException("boom");
            }

            return _inner.Validate(line);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    /// <summary>Lets exactly one blocked call continue.</summary>
    public void ReleaseOne() => _gate.Release();

    /// <summary>Lets exactly one blocked call continue, and that call throws InvalidOperationException("boom").</summary>
    public void ReleaseOneAndThrow()
    {
        Volatile.Write(ref _throwOnNextRelease, 1);
        _gate.Release();
    }

    /// <summary>Opens the gate for good: all blocked and future calls pass straight through.</summary>
    public void Open()
    {
        _open = true;
        _gate.Release(1000);
    }

    /// <summary>Polls until at least <paramref name="count"/> calls have started, or fails after the timeout.</summary>
    public async Task WaitForStartedAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Started < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} started Validate calls, saw {Started}.");
            }

            await Task.Delay(10);
        }
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
