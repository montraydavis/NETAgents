using System.Threading.Channels;
using System.Collections.Concurrent;
using NETAgents.Tools.Processing.Models;
using NETAgents.Models.Processing;

namespace NETAgents.Tools.Processing.Services;

public class ProcessingQueueService : IProcessingQueueService, IDisposable
{
    private readonly Channel<FileProcessingJob> _queue;
    private readonly ILogger<ProcessingQueueService> _logger;
    private readonly ConcurrentDictionary<Guid, FileProcessingJob> _activeJobs;
    private readonly ConcurrentDictionary<Guid, FileProcessingJob> _completedJobs;
    private readonly ProcessingOptions _options;
    private Timer? _cleanupTimer;
    private readonly CancellationTokenSource _cleanupCancellationTokenSource = new();
    private readonly object _statsLock = new();
    private volatile bool _disposed;

    public ProcessingQueueService(ILogger<ProcessingQueueService> logger, ProcessingOptions options)
    {
        _logger = logger;
        _options = options;
        _activeJobs = new ConcurrentDictionary<Guid, FileProcessingJob>();
        _completedJobs = new ConcurrentDictionary<Guid, FileProcessingJob>();
        
        // Create a bounded channel with much larger capacity to handle many jobs
        int capacity = Math.Max(_options.MaxConcurrentProcessing * 100, 1000); // Increased from 10 to 100
        BoundedChannelOptions channelOptions = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false // Better performance
        };
        
        _queue = Channel.CreateBounded<FileProcessingJob>(channelOptions);
        
        // Setup cleanup timer with proper async handling
        _cleanupTimer = new Timer(CleanupTimerCallback, null, _options.CollectionCleanupInterval, _options.CollectionCleanupInterval);
    }

    private void CleanupTimerCallback(object? state)
    {
        if (_disposed || _cleanupCancellationTokenSource.Token.IsCancellationRequested)
            return;

        // Fire-and-forget pattern with proper exception handling
        _ = Task.Run(async () =>
        {
            try
            {
                await CleanupStaleJobsAsync(_cleanupCancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cleanup timer callback");
            }
        }, _cleanupCancellationTokenSource.Token);
    }

    public async Task EnqueueJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        try
        {
            _logger.LogDebug("About to enqueue job {JobId} for file {FilePath}", job.Id, job.FilePath);
            
            // Add a timeout to prevent indefinite blocking
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout
            
            await _queue.Writer.WriteAsync(job, timeoutCts.Token).ConfigureAwait(false);
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
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.DequeueTimeout);
            
            // Try to read a single job instead of all jobs
            if (await _queue.Reader.WaitToReadAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                if (_queue.Reader.TryRead(out FileProcessingJob? job))
                {
                    job.Status = JobProcessingStatus.Processing;
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
            job.Status = JobProcessingStatus.Completed;
            job.Result = result;
            _logger.LogInformation("Job {JobId} completed successfully in {Duration:F2}s", 
                job.Id, job.ProcessingDuration?.TotalSeconds ?? 0);
        }
        else
        {
            job.Status = JobProcessingStatus.Failed;
            job.ErrorMessage = errorMessage;
            _logger.LogError("Job {JobId} failed: {Error}", job.Id, errorMessage);
        }
        
        // Move from active to completed with bounded collection management
        if (_activeJobs.TryRemove(job.Id, out _))
        {
            AddToCompletedJobsWithEviction(job);
        }
    }

    private void AddToCompletedJobsWithEviction(FileProcessingJob job)
    {
        // Add the job to completed jobs
        _completedJobs.TryAdd(job.Id, job);
        
        // Check if we need to evict old entries
        if (_completedJobs.Count > _options.MaxCompletedJobs)
        {
            // Remove oldest entries (LRU eviction based on completion time)
            var oldestJobs = _completedJobs.Values
                .OrderBy(j => j.CompletedAt)
                .Take(_completedJobs.Count - _options.MaxCompletedJobs)
                .ToList();
            
            foreach (var oldJob in oldestJobs)
            {
                _completedJobs.TryRemove(oldJob.Id, out _);
            }
            
            _logger.LogDebug("Evicted {Count} old completed jobs to maintain collection size limit", oldestJobs.Count);
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
            job.Status = JobProcessingStatus.Failed;
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
        
        job.Status = JobProcessingStatus.Retrying;
        _logger.LogWarning("Job {JobId} will be retried (attempt {RetryCount}/{MaxRetries})", 
            job.Id, job.RetryCount, _options.MaxRetryAttempts);
        return true;
    }

    public async Task EnqueueRetryJobAsync(FileProcessingJob job, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        // Reset job state for retry
        job.Status = JobProcessingStatus.Pending;
        job.StartedAt = null;
        job.CompletedAt = null;
        job.ProcessingDuration = null;
        
        // Remove from active jobs before re-queuing
        _activeJobs.TryRemove(job.Id, out _);
        
        // Calculate exponential backoff delay
        TimeSpan delay = TimeSpan.FromMilliseconds(
            _options.RetryDelay.TotalMilliseconds * Math.Pow(2, job.RetryCount - 1));
        
        _logger.LogDebug("Scheduling retry for job {JobId} after {Delay:F2}s delay", 
            job.Id, delay.TotalSeconds);
        
        // Wait before re-queuing
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        
        await EnqueueJobAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueueStatistics> GetStatisticsAsync()
    {
        ThrowIfDisposed();
        
        int pendingJobs = await GetQueueLengthAsync();
        int activeJobs = _activeJobs.Count;
        
        lock (_statsLock)
        {
            int completedJobs = _completedJobs.Values.Count(j => j.Status == JobProcessingStatus.Completed);
            int failedJobs = _completedJobs.Values.Count(j => j.Status == JobProcessingStatus.Failed);
            
            List<FileProcessingJob> completedWithDuration = _completedJobs.Values
                .Where(j => j.Status == JobProcessingStatus.Completed && j.ProcessingDuration.HasValue)
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
            // Cleanup stale active jobs
            DateTime staleThreshold = DateTime.UtcNow.Subtract(_options.ProcessingTimeout * 2);
            List<FileProcessingJob> staleJobs = _activeJobs.Values
                .Where(j => j.StartedAt.HasValue && j.StartedAt.Value < staleThreshold)
                .ToList();
            
            foreach (FileProcessingJob staleJob in staleJobs)
            {
                _logger.LogWarning("Cleaning up stale job {JobId} that started at {StartTime}", 
                    staleJob.Id, staleJob.StartedAt);
                
                CompleteJob(staleJob, false, errorMessage: "Job became stale and was cleaned up");
            }
            
            // Monitor active jobs collection size
            if (_activeJobs.Count > _options.MaxActiveJobs)
            {
                _logger.LogWarning("Active jobs collection size ({Current}) exceeds limit ({Limit})", 
                    _activeJobs.Count, _options.MaxActiveJobs);
                
                // Force cleanup of oldest active jobs if severely over limit
                if (_activeJobs.Count > _options.MaxActiveJobs * 2)
                {
                    var oldestActiveJobs = _activeJobs.Values
                        .OrderBy(j => j.StartedAt)
                        .Take(_activeJobs.Count - _options.MaxActiveJobs)
                        .ToList();
                    
                    foreach (var oldJob in oldestActiveJobs)
                    {
                        _logger.LogWarning("Force cleaning up old active job {JobId} due to collection size limit", oldJob.Id);
                        CompleteJob(oldJob, false, errorMessage: "Job cleaned up due to collection size limit");
                    }
                }
            }
            
            // Log collection statistics
            _logger.LogDebug("Collection cleanup completed - Active: {ActiveCount}, Completed: {CompletedCount}", 
                _activeJobs.Count, _completedJobs.Count);
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
            // Stop timer before disposal to prevent new callbacks
            _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            
            // Cancel and dispose cancellation token source
            _cleanupCancellationTokenSource.Cancel();
            _cleanupCancellationTokenSource.Dispose();
            
            // Complete the channel
            _queue.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing ProcessingQueueService");
        }
    }
}