using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services;

public interface IProcessingQueueService : IDisposable
{
    Task EnqueueJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default);
    Task<FileProcessingJob?> DequeueJobAsync(CancellationToken cancellationToken = default);
    Task<int> GetQueueLengthAsync();
    Task<IEnumerable<FileProcessingJob>> GetActiveJobsAsync();
    void CompleteJob(FileProcessingJob job, bool success, string? result = null, string? errorMessage = null);
    bool ShouldRetryJob(FileProcessingJob job, string errorMessage);
    Task EnqueueRetryJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default);
    Task<QueueStatistics> GetStatisticsAsync();
    Task CleanupStaleJobsAsync(CancellationToken cancellationToken = default);
    void CompleteChannel();
}
