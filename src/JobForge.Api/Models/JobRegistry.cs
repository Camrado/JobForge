using System.Collections.Concurrent;

namespace JobForge.Api.Models;

public sealed class JobRegistry
{
    private ConcurrentDictionary<Guid, JobEntry> Jobs { get; } = new();

    public void Add(JobEntry jobEntry)
    {
        var isSuccess = Jobs.TryAdd(jobEntry.Id, jobEntry);

        if (!isSuccess)
        {
            throw new InvalidOperationException($"Job with id {jobEntry.Id} was not added");
        }
    }

    public bool Remove(Guid jobId)
    {
        return Jobs.TryRemove(jobId, out _);
    }

    public JobEntry? TryGet(Guid jobId)
    {
        Jobs.TryGetValue(jobId, out var job);
        return job;
    }
}