namespace JobForge.Api.Models;

public enum JobStatusType
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}