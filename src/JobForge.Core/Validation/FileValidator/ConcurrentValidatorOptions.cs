using JobForge.Core.Models;

namespace JobForge.Core.Validation.FileValidator;

public sealed class ConcurrentValidatorOptions
{
    public int ChannelCapacity { get; init; } = 32;
    public int WorkerCount { get; init; } = 4;

    /// <summary>Test hook: called when the channel is full and the producer is about to wait to write this record.</summary>
    public Action<LineRecordDto>? OnProducerBlocked { get; init; }

    /// <summary>Test hook: awaited by a consumer right after it takes a record, before validating it.</summary>
    public Func<LineRecordDto, CancellationToken, Task>? OnRecordTaken { get; init; }
}
