using System.Collections.Concurrent;
using JobForge.ConsoleValidator.Validation;

namespace JobForge.ConsoleValidator.Tests;

/// <summary>
/// Records which line numbers the producer had to wait on because the channel was full.
/// Pass <see cref="OnBlocked"/> to ConcurrentFileValidator's onProducerBlocked hook.
/// </summary>
public sealed class ProducerBlockedProbe
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _blocked = new();

    public void OnBlocked(LineRecordDto record) => Signal(record.LineNumber).TrySetResult();

    /// <summary>Completes once the producer has blocked while writing this line.</summary>
    public Task BlockedOn(int lineNumber) => Signal(lineNumber).Task;

    public bool HasBlockedOn(int lineNumber) => BlockedOn(lineNumber).IsCompleted;

    private TaskCompletionSource Signal(int lineNumber) =>
        _blocked.GetOrAdd(lineNumber, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
