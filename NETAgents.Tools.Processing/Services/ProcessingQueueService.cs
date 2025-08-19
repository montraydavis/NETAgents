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
    bool ShouldRetryJob(FileProcessingJob job, string errorMessage);
    Task EnqueueRetryJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default);
    Task<QueueStatistics> GetStatisticsAsync();
    Task CleanupStaleJobsAsync(CancellationToken cancellationToken = default);
    void CompleteChannel();
}

public record QueueStatistics(
    int PendingJobs,
    int ActiveJobs,
    int CompletedJobs,
    int FailedJobs,
    TimeSpan? AverageProcessingTime
);

public class ProcessingQueueService : IProcessingQueueService, IDisposable
{
    private readonly Channel<FileProcessingJob> _queue;
    private readonly ILogger<ProcessingQueueService> _logger;
    private readonly ConcurrentDictionary<Guid, FileProcessingJob> _activeJobs;
    private readonly ConcurrentDictionary<Guid, FileProcessingJob> _completedJobs;
    private readonly ProcessingOptions _options;
    private readonly Timer _cleanupTimer;
    private readonly object _statsLock = new();
    private volatile bool _disposed;

    public ProcessingQueueService(ILogger<ProcessingQueueService> logger, ProcessingOptions options)
    {
        _logger = logger;
        _options = options;
        _activeJobs = new ConcurrentDictionary<Guid, FileProcessingJob>();
        _completedJobs = new ConcurrentDictionary<Guid, FileProcessingJob>();
        
        // Create a bounded channel with much larger capacity to handle many jobs
        var capacity = Math.Max(_options.MaxConcurrentProcessing * 100, 1000); // Increased from 10 to 100
        var channelOptions = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false // Better performance
        };
        
        _queue = Channel.CreateBounded<FileProcessingJob>(channelOptions);
        
        // Setup cleanup timer for stale jobs
        _cleanupTimer = new Timer(async _ => await CleanupStaleJobsAsync(), 
            null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task EnqueueJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        try
        {
            _logger.LogDebug("About to enqueue job {JobId} for file {FilePath}", job.Id, job.FilePath);
            
            // Add a timeout to prevent indefinite blocking
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout
            
            await _queue.Writer.WriteAsync(job, timeoutCts.Token);
            _logger.LogDebug("Successfully enqueued job {JobId} for file {FilePath}", job.Id, job.FilePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Enqueue operation cancelled for job {JobId}", job.Id);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Enqueue timeout for job {JobId} - queue may be full", job.Id);
            throw new TimeoutException($"Failed to enqueue job {job.Id} within timeout period");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed"))
        {
            _logger.LogWarning("Cannot enqueue job {JobId} - queue is closed", job.Id);
            throw new InvalidOperationException("Processing queue has been closed", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue job {JobId} for file {FilePath}", job.Id, job.FilePath);
            throw;
        }
    }

    public async Task<FileProcessingJob?> DequeueJobAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // Use a timeout to avoid infinite blocking
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.DequeueTimeout);
            
            // Try to read a single job instead of all jobs
            if (await _queue.Reader.WaitToReadAsync(timeoutCts.Token))
            {
                if (_queue.Reader.TryRead(out var job))
                {
                    job.Status = ProcessingStatus.Processing;
                    job.StartedAt = DateTime.UtcNow;
                    
                    if (!_activeJobs.TryAdd(job.Id, job))
                    {
                        _logger.LogWarning("Job {JobId} was already in active jobs collection", job.Id);
                        return null;
                    }
                    
                    _logger.LogDebug("Job {JobId} for file {FilePath} dequeued", job.Id, job.FilePath);
                    return job;
                }
            }
            
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Dequeue operation cancelled");
            return null;
        }
        catch (OperationCanceledException)
        {
            // Timeout occurred, return null to allow worker to continue
            _logger.LogDebug("Dequeue timeout - no jobs available");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dequeuing job");
            throw;
        }
    }

    public Task<int> GetQueueLengthAsync()
    {
        ThrowIfDisposed();
        return Task.FromResult(_queue.Reader.Count);
    }

    public Task<IEnumerable<FileProcessingJob>> GetActiveJobsAsync()
    {
        ThrowIfDisposed();
        return Task.FromResult(_activeJobs.Values.AsEnumerable());
    }

    public void CompleteJob(FileProcessingJob job, bool success, string? result = null, string? errorMessage = null)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        job.CompletedAt = DateTime.UtcNow;
        if (job.StartedAt.HasValue)
        {
            job.ProcessingDuration = job.CompletedAt - job.StartedAt;
        }
        
        if (success)
        {
            job.Status = ProcessingStatus.Completed;
            job.Result = result;
            _logger.LogInformation("Job {JobId} completed successfully in {Duration:F2}s", 
                job.Id, job.ProcessingDuration?.TotalSeconds ?? 0);
        }
        else
        {
            job.Status = ProcessingStatus.Failed;
            job.ErrorMessage = errorMessage;
            _logger.LogError("Job {JobId} failed: {Error}", job.Id, errorMessage);
        }
        
        // Move from active to completed
        if (_activeJobs.TryRemove(job.Id, out _))
        {
            _completedJobs.TryAdd(job.Id, job);
        }
    }

    public bool ShouldRetryJob(FileProcessingJob job, string errorMessage)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        job.RetryCount++;
        job.ErrorMessage = errorMessage;
        
        if (job.RetryCount >= _options.MaxRetryAttempts)
        {
            job.Status = ProcessingStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            if (job.StartedAt.HasValue)
            {
                job.ProcessingDuration = job.CompletedAt - job.StartedAt;
            }
            
            // Move from active to completed
            if (_activeJobs.TryRemove(job.Id, out _))
            {
                _completedJobs.TryAdd(job.Id, job);
            }
            
            _logger.LogError("Job {JobId} failed permanently after {RetryCount} attempts", 
                job.Id, job.RetryCount);
            return false;
        }
        
        job.Status = ProcessingStatus.Retrying;
        _logger.LogWarning("Job {JobId} will be retried (attempt {RetryCount}/{MaxRetries})", 
            job.Id, job.RetryCount, _options.MaxRetryAttempts);
        return true;
    }

    public async Task EnqueueRetryJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        // Reset job state for retry
        job.Status = ProcessingStatus.Pending;
        job.StartedAt = null;
        job.CompletedAt = null;
        job.ProcessingDuration = null;
        
        // Remove from active jobs before re-queuing
        _activeJobs.TryRemove(job.Id, out _);
        
        // Calculate exponential backoff delay
        var delay = TimeSpan.FromMilliseconds(
            _options.RetryDelay.TotalMilliseconds * Math.Pow(2, job.RetryCount - 1));
        
        _logger.LogDebug("Scheduling retry for job {JobId} after {Delay:F2}s delay", 
            job.Id, delay.TotalSeconds);
        
        // Wait before re-queuing
        await Task.Delay(delay, cancellationToken);
        
        await EnqueueJobAsync(job, cancellationToken);
    }

    public async Task<QueueStatistics> GetStatisticsAsync()
    {
        ThrowIfDisposed();
        
        var pendingJobs = await GetQueueLengthAsync();
        var activeJobs = _activeJobs.Count;
        
        lock (_statsLock)
        {
            var completedJobs = _completedJobs.Values.Count(j => j.Status == ProcessingStatus.Completed);
            var failedJobs = _completedJobs.Values.Count(j => j.Status == ProcessingStatus.Failed);
            
            var completedWithDuration = _completedJobs.Values
                .Where(j => j.Status == ProcessingStatus.Completed && j.ProcessingDuration.HasValue)
                .ToList();
            
            TimeSpan? averageProcessingTime = null;
            if (completedWithDuration.Any())
            {
                averageProcessingTime = TimeSpan.FromMilliseconds(
                    completedWithDuration.Average(j => j.ProcessingDuration!.Value.TotalMilliseconds));
            }
            
            return new QueueStatistics(pendingJobs, activeJobs, completedJobs, failedJobs, averageProcessingTime);
        }
    }

    public Task CleanupStaleJobsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;
        
        try
        {
            var staleThreshold = DateTime.UtcNow.Subtract(_options.ProcessingTimeout * 2);
            var staleJobs = _activeJobs.Values
                .Where(j => j.StartedAt.HasValue && j.StartedAt.Value < staleThreshold)
                .ToList();
            
            foreach (var staleJob in staleJobs)
            {
                _logger.LogWarning("Cleaning up stale job {JobId} that started at {StartTime}", 
                    staleJob.Id, staleJob.StartedAt);
                
                CompleteJob(staleJob, false, errorMessage: "Job became stale and was cleaned up");
            }
            
            // Cleanup old completed jobs (keep last 1000)
            lock (_statsLock)
            {
                if (_completedJobs.Count > 1000)
                {
                    var oldJobs = _completedJobs.Values
                        .OrderBy(j => j.CompletedAt)
                        .Take(_completedJobs.Count - 1000)
                        .ToList();
                    
                    foreach (var oldJob in oldJobs)
                    {
                        _completedJobs.TryRemove(oldJob.Id, out _);
                    }
                    
                    _logger.LogDebug("Cleaned up {Count} old completed jobs", oldJobs.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stale job cleanup");
        }

        return Task.CompletedTask;
    }

    public void CompleteChannel()
    {
        if (!_disposed)
        {
            _queue.Writer.TryComplete();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessingQueueService));
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        try
        {
            _cleanupTimer?.Dispose();
            _queue.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing ProcessingQueueService");
        }
    }
}