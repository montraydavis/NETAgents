using System.Threading.Channels;
using System.Collections.Concurrent;
using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services;

public interface IProcessingQueueService
{
    Task EnqueueJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default);
    Task<FileProcessingJob?> DequeueJobAsync(CancellationToken cancellationToken = default);
    Task<int> GetQueueLengthAsync();
    Task<IEnumerable<FileProcessingJob>> GetActiveJobsAsync();
    void CompleteJob(FileProcessingJob job, bool success, string? result = null, string? errorMessage = null);
    void RetryJob(FileProcessingJob job, string errorMessage);
}

public class ProcessingQueueService : IProcessingQueueService
{
    private readonly Channel<FileProcessingJob> _queue;
    private readonly ILogger<ProcessingQueueService> _logger;
    private readonly ConcurrentDictionary<Guid, FileProcessingJob> _activeJobs;
    private readonly ProcessingOptions _options;

    public ProcessingQueueService(ILogger<ProcessingQueueService> logger, ProcessingOptions options)
    {
        _logger = logger;
        _options = options;
        _activeJobs = new ConcurrentDictionary<Guid, FileProcessingJob>();
        
        // Create a bounded channel with backpressure handling
        var channelOptions = new BoundedChannelOptions(_options.MaxConcurrentProcessing * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        _queue = Channel.CreateBounded<FileProcessingJob>(channelOptions);
    }

    public async Task EnqueueJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default)
    {
        try
        {
            await _queue.Writer.WriteAsync(job, cancellationToken);
            _logger.LogInformation("Job {JobId} for file {FilePath} enqueued", job.Id, job.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue job {JobId} for file {FilePath}", job.Id, job.FilePath);
            throw;
        }
    }

    public async Task<FileProcessingJob?> DequeueJobAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _queue.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_queue.Reader.TryRead(out var job))
                {
                    job.Status = ProcessingStatus.Processing;
                    job.StartedAt = DateTime.UtcNow;
                    _activeJobs.TryAdd(job.Id, job);
                    
                    _logger.LogInformation("Job {JobId} for file {FilePath} dequeued and started processing", job.Id, job.FilePath);
                    return job;
                }
            }
            
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dequeuing job");
            throw;
        }
    }

    public async Task<int> GetQueueLengthAsync()
    {
        return _queue.Reader.Count;
    }

    public Task<IEnumerable<FileProcessingJob>> GetActiveJobsAsync()
    {
        return Task.FromResult(_activeJobs.Values.AsEnumerable());
    }

    public void CompleteJob(FileProcessingJob job, bool success, string? result = null, string? errorMessage = null)
    {
        job.CompletedAt = DateTime.UtcNow;
        job.ProcessingDuration = job.CompletedAt - job.StartedAt;
        
        if (success)
        {
            job.Status = ProcessingStatus.Completed;
            job.Result = result;
            _logger.LogInformation("Job {JobId} for file {FilePath} completed successfully in {Duration}ms", 
                job.Id, job.FilePath, job.ProcessingDuration?.TotalMilliseconds);
        }
        else
        {
            job.Status = ProcessingStatus.Failed;
            job.ErrorMessage = errorMessage;
            _logger.LogError("Job {JobId} for file {FilePath} failed: {Error}", 
                job.Id, job.FilePath, errorMessage);
        }
        
        _activeJobs.TryRemove(job.Id, out _);
    }

    public void RetryJob(FileProcessingJob job, string errorMessage)
    {
        job.RetryCount++;
        job.ErrorMessage = errorMessage;
        
        if (job.RetryCount >= _options.MaxRetryAttempts)
        {
            job.Status = ProcessingStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingDuration = job.CompletedAt - job.StartedAt;
            _activeJobs.TryRemove(job.Id, out _);
            
            _logger.LogError("Job {JobId} for file {FilePath} failed after {RetryCount} retries", 
                job.Id, job.FilePath, job.RetryCount);
        }
        else
        {
            job.Status = ProcessingStatus.Retrying;
            _logger.LogWarning("Job {JobId} for file {FilePath} will be retried (attempt {RetryCount}/{MaxRetries})", 
                job.Id, job.FilePath, job.RetryCount, _options.MaxRetryAttempts);
        }
    }
}
