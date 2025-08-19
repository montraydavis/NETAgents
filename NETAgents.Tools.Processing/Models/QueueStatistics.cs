namespace NETAgents.Tools.Processing.Models;

public record QueueStatistics(
    int PendingJobs,
    int ActiveJobs,
    int CompletedJobs,
    int FailedJobs,
    TimeSpan? AverageProcessingTime
);
