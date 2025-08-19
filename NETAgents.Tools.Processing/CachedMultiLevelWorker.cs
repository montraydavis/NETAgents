using System.Security.Cryptography;
using System.Text.Json;
using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Services;
using NETAgents.Tools.Processing.Cache;
using NETAgents.Models.Processing;

namespace NETAgents.Tools.Processing;

public class CachedMultiLevelWorker : BackgroundService
{
    private readonly ILogger<CachedMultiLevelWorker> _logger;
    private readonly ProcessingOptions _options;
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly IProcessingQueueService _queueService;
    private readonly IMultiLevelFileProcessorService _processorService;
    private readonly ICacheService _cacheService;
    private readonly List<Task> _processingTasks;
    private readonly CancellationTokenSource _processingCts;
    private volatile bool _disposed;

    public CachedMultiLevelWorker(
        ILogger<CachedMultiLevelWorker> logger,
        ProcessingOptions options,
        IFileDiscoveryService fileDiscoveryService,
        IProcessingQueueService queueService,
        IMultiLevelFileProcessorService processorService,
        ICacheService cacheService)
    {
        _logger = logger;
        _options = options;
        _fileDiscoveryService = fileDiscoveryService;
        _queueService = queueService;
        _processorService = processorService;
        _cacheService = cacheService;
        _processingTasks = new List<Task>();
        _processingCts = new CancellationTokenSource();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting cached multi-level file processing worker service");

        try
        {
            await StartFileDiscoveryAsync(stoppingToken).ConfigureAwait(false);
            Task monitoringTask = Task.Run(async () => await RunMonitoringLoopAsync(stoppingToken).ConfigureAwait(false), stoppingToken);
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cached multi-level worker service is shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in cached multi-level worker service");
            throw;
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    private async Task StartFileDiscoveryAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting file discovery service with cache checking");

        IEnumerable<FileProcessingJob> initialFiles = await _fileDiscoveryService.DiscoverFilesAsync(stoppingToken).ConfigureAwait(false);
        await StartProcessingWorkersAsync(stoppingToken).ConfigureAwait(false);

        int newFilesCount = 0;
        int cachedFilesCount = 0;

        foreach (FileProcessingJob file in initialFiles)
        {
            try
            {
                // Calculate current file hash
                string currentHash = await CalculateFileHashAsync(file.FilePath).ConfigureAwait(false);

                // Check if file is already processed and up-to-date
                if (await _cacheService.IsFileProcessedAsync(file.FilePath, currentHash, stoppingToken).ConfigureAwait(false))
                {
                    cachedFilesCount++;
                    _logger.LogDebug("Skipping already processed file: {FilePath}", file.FilePath);
                    continue;
                }

                // File needs processing
                MultiLevelProcessingJob multiLevelJob = ConvertToMultiLevelJob(file);
                await _queueService.EnqueueJobAsync(multiLevelJob, stoppingToken).ConfigureAwait(false);
                newFilesCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking cache for file {FilePath}", file.FilePath);
                // Fallback to processing the file
                MultiLevelProcessingJob multiLevelJob = ConvertToMultiLevelJob(file);
                await _queueService.EnqueueJobAsync(multiLevelJob, stoppingToken).ConfigureAwait(false);
                newFilesCount++;
            }
        }

        _logger.LogInformation("Discovery complete - New files to process: {NewFiles}, Cached files skipped: {CachedFiles}",
            newFilesCount, cachedFilesCount);

        await _fileDiscoveryService.StartFileWatcherAsync(stoppingToken).ConfigureAwait(false);
    }

    private static MultiLevelProcessingJob ConvertToMultiLevelJob(FileProcessingJob originalJob)
    {
        return new MultiLevelProcessingJob
        {
            Id = originalJob.Id,
            FilePath = originalJob.FilePath,
            Content = originalJob.Content,
            Status = originalJob.Status,
            CreatedAt = originalJob.CreatedAt,
            RequiredLevels = new List<JobProcessingLevel>
            {
                JobProcessingLevel.Ast,
                JobProcessingLevel.DomainKeywords
            }
        };
    }

    private Task StartProcessingWorkersAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {WorkerCount} cached multi-level processing workers", _options.MaxConcurrentProcessing);

        for (int i = 0; i < _options.MaxConcurrentProcessing; i++)
        {
            int workerId = i;
            Task workerTask = Task.Run(async () => await ProcessFilesAsync(workerId, stoppingToken).ConfigureAwait(false), stoppingToken);
            _processingTasks.Add(workerTask);
        }

        return Task.CompletedTask;
    }

    private async Task ProcessFilesAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cached multi-level worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                // Use a combined cancellation token that includes disposal state
                using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken, _processingCts.Token);
                
                FileProcessingJob? job = await _queueService.DequeueJobAsync(combinedCts.Token).ConfigureAwait(false);

                if (job is not MultiLevelProcessingJob multiLevelJob)
                {
                    if (job != null)
                    {
                        multiLevelJob = ConvertToMultiLevelJob(job);
                    }
                    else
                    {
                        // No jobs available, wait with shorter interval and check cancellation more frequently
                        await Task.Delay(2000, combinedCts.Token).ConfigureAwait(false);
                        continue;
                    }
                }

                if (combinedCts.Token.IsCancellationRequested)
                {
                    _logger.LogDebug("Worker {WorkerId} cancellation requested before processing job {JobId}", workerId, multiLevelJob.Id);
                    break;
                }

                _logger.LogInformation("Worker {WorkerId} processing cached multi-level job {JobId} for file {FilePath}",
                    workerId, multiLevelJob.Id, multiLevelJob.FilePath);

                await ProcessCachedMultiLevelJobAsync(multiLevelJob, workerId, combinedCts.Token).ConfigureAwait(false);
                
                // Check cancellation after each job
                if (combinedCts.Token.IsCancellationRequested)
                {
                    _logger.LogDebug("Worker {WorkerId} cancellation requested after processing job {JobId}", workerId, multiLevelJob.Id);
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker {WorkerId} stopped due to service shutdown", workerId);
                break;
            }
            catch (OperationCanceledException) when (_processingCts.Token.IsCancellationRequested)
            {
                _logger.LogInformation("Worker {WorkerId} stopped due to processing cancellation", workerId);
                break;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                _logger.LogInformation("Worker {WorkerId} stopped due to service disposal", workerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in cached multi-level worker {WorkerId}", workerId);
                
                // Implement exponential backoff for error recovery
                int backoffMs = Math.Min(5000, 1000 * (int)Math.Pow(2, Math.Min(3, 1))); // Cap at 5s, start at 2s
                
                try
                {
                    await Task.Delay(backoffMs, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Worker {WorkerId} error recovery cancelled", workerId);
                    break;
                }
            }
        }
        
        _logger.LogInformation("Cached multi-level worker {WorkerId} ended", workerId);
    }

    private async Task ProcessCachedMultiLevelJobAsync(MultiLevelProcessingJob job, int workerId, CancellationToken stoppingToken)
    {
        DateTime startTime = DateTime.UtcNow;
        job.StartedAt = startTime;
        job.Status = JobProcessingStatus.Processing;

        try
        {
            // Check if we have partial results in cache
            ProcessedFileEntry? cachedEntry = await _cacheService.GetProcessedFileAsync(job.FilePath, stoppingToken).ConfigureAwait(false);
            if (cachedEntry != null)
            {
                // Load any successfully processed levels from cache
                foreach (JobProcessingLevel level in job.RequiredLevels)
                {
                    if (cachedEntry.LevelData.TryGetValue(level, out ProcessedLevelData? cachedLevelData) && cachedLevelData.IsSuccess)
                    {
                        job.Results[level] = new JobProcessingResult
                        {
                            IsSuccess = true,
                            Content = cachedLevelData.Content,
                            ProcessedAt = cachedLevelData.ProcessedAt,
                            ProcessingDuration = cachedLevelData.ProcessingDuration
                        };

                        _logger.LogDebug("Loaded {Level} from cache for job {JobId}", level, job.Id);
                    }
                }
            }

            // Process only the levels that aren't cached or failed
            foreach (JobProcessingLevel level in job.RequiredLevels)
            {
                if (job.Results.ContainsKey(level) && job.Results[level].IsSuccess)
                {
                    _logger.LogDebug("Level {Level} already available for job {JobId}, skipping", level, job.Id);
                    continue;
                }

                _logger.LogInformation("Worker {WorkerId} processing {Level} for job {JobId}", workerId, level, job.Id);

                job.CurrentLevel = level;
                JobProcessingResult result = await _processorService.ProcessLevelAsync(job, level, stoppingToken).ConfigureAwait(false);
                job.Results[level] = result;

                if (!result.IsSuccess)
                {
                    _logger.LogError("Level {Level} failed for job {JobId}: {Error}", level, job.Id, result.ErrorMessage);
                    await HandleJobFailure(job, $"Level {level} failed: {result.ErrorMessage}", stoppingToken).ConfigureAwait(false);
                    return;
                }

                _logger.LogInformation("Worker {WorkerId} completed {Level} for job {JobId} in {Duration:F2}s",
                    workerId, level, job.Id, result.ProcessingDuration.TotalSeconds);
            }

            // All levels completed successfully
            if (job.IsMultiLevelComplete)
            {
                // Final validation before caching - ensure all results contain valid JSON
                foreach (JobProcessingResult result in job.Results.Values)
                {
                    if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content))
                    {
                        try
                        {
                            using JsonDocument doc = System.Text.Json.JsonDocument.Parse(result.Content);
                        }
                        catch (System.Text.Json.JsonException jsonEx)
                        {
                            _logger.LogError("Invalid JSON detected before caching for job {JobId}: {Error}", job.Id, jsonEx.Message);
                            _queueService.CompleteJob(job, false, errorMessage: $"Invalid JSON before caching: {jsonEx.Message}");
                            return;
                        }
                    }
                }
                
                // Store in cache
                await _cacheService.StoreProcessedFileAsync(job, stoppingToken).ConfigureAwait(false);

                string aggregatedResult = AggregateResults(job);
                _queueService.CompleteJob(job, true, aggregatedResult);

                TimeSpan totalDuration = DateTime.UtcNow - startTime;
                _logger.LogInformation("Worker {WorkerId} completed and cached all levels for job {JobId} in {Duration:F2}s",
                    workerId, job.Id, totalDuration.TotalSeconds);
            }
            else
            {
                _logger.LogError("Job {JobId} did not complete all required levels", job.Id);
                _queueService.CompleteJob(job, false, errorMessage: "Not all required levels completed");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Cached multi-level job {JobId} cancelled due to service shutdown", job.Id);
            _queueService.CompleteJob(job, false, errorMessage: "Service shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing cached multi-level job {JobId}", job.Id);
            await HandleJobFailure(job, ex.Message, stoppingToken).ConfigureAwait(false);
        }
    }

    private static string AggregateResults(MultiLevelProcessingJob job)
    {
        var aggregated = new
        {
            FilePath = job.FilePath,
            ProcessedLevels = job.Results.Keys.Select(k => k.ToString()).ToArray(),
            Results = job.Results.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => new
                {
                    Success = kvp.Value.IsSuccess,
                    Content = kvp.Value.Content,
                    ProcessingDuration = kvp.Value.ProcessingDuration.TotalSeconds,
                    ProcessedAt = kvp.Value.ProcessedAt
                }
            ),
            TotalDuration = job.Results.Values.Sum(r => r.ProcessingDuration.TotalSeconds)
        };

        return System.Text.Json.JsonSerializer.Serialize(aggregated, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private async Task HandleJobFailure(MultiLevelProcessingJob job, string errorMessage, CancellationToken stoppingToken)
    {
        if (_queueService.ShouldRetryJob(job, errorMessage))
        {
            try
            {
                // Reset only the failed level for retry, keep successful ones
                if (job.Results.ContainsKey(job.CurrentLevel))
                {
                    job.Results.Remove(job.CurrentLevel);
                }

                await _queueService.EnqueueRetryJobAsync(job, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue retry for cached multi-level job {JobId}", job.Id);
                _queueService.CompleteJob(job, false, errorMessage: $"Retry failed: {ex.Message}");
            }
        }
    }

    private async Task RunMonitoringLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Get basic statistics without loading all data
                // var queueStats = await _queueService.GetStatisticsAsync();
                // var cacheStats = await _cacheService.GetStatisticsAsync();

                // _logger.LogInformation(
                //     "Status - Queue: P:{Pending} A:{Active} C:{Completed} F:{Failed} | Cache: Total:{CacheTotal} Processed:{CacheProcessed}",
                //     queueStats.PendingJobs, queueStats.ActiveJobs, queueStats.CompletedJobs, queueStats.FailedJobs,
                //     cacheStats.TotalFiles, cacheStats.ProcessedFiles);

                await CheckAndRestartFailedWorkers(stoppingToken).ConfigureAwait(false);
                await _queueService.CleanupStaleJobsAsync(stoppingToken).ConfigureAwait(false);

                // Note: Cache cleanup removed to prevent deadlocks
                // Cache cleanup can be done via scheduled tasks or manual cleanup

                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cached multi-level monitoring loop");
                await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private Task CheckAndRestartFailedWorkers(CancellationToken stoppingToken)
    {
        if (_disposed || stoppingToken.IsCancellationRequested)
            return Task.CompletedTask;
            
        try
        {
            List<Task> failedTasks = _processingTasks.Where(t => t.IsFaulted || (t.IsCompleted && !t.IsCanceled)).ToList();

            foreach (Task failedTask in failedTasks)
            {
                // Log the failure reason
                if (failedTask.IsFaulted && failedTask.Exception != null)
                {
                    _logger.LogError(failedTask.Exception.GetBaseException(), 
                        "Processing worker task failed and will be restarted");
                }
                else if (failedTask.IsCompleted && !failedTask.IsCanceled)
                {
                    _logger.LogWarning("Processing worker task completed unexpectedly and will be restarted");
                }

                _processingTasks.Remove(failedTask);

                // Only restart if we're not shutting down
                if (!stoppingToken.IsCancellationRequested && !_disposed)
                {
                    int workerId = _processingTasks.Count;
                    _logger.LogInformation("Restarting failed worker as worker {WorkerId}", workerId);
                    
                    Task newWorkerTask = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessFilesAsync(workerId, stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in restarted worker {WorkerId}", workerId);
                        }
                    }, stoppingToken);
                    
                    _processingTasks.Add(newWorkerTask);
                }
            }
            
            // Log current worker health status
            int activeWorkers = _processingTasks.Count(t => !t.IsCompleted);
            int expectedWorkers = _options.MaxConcurrentProcessing;
            
            if (activeWorkers < expectedWorkers && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Worker health check: {ActiveWorkers}/{ExpectedWorkers} workers active", 
                    activeWorkers, expectedWorkers);
            }
            else if (activeWorkers == expectedWorkers)
            {
                _logger.LogDebug("Worker health check: All {ActiveWorkers} workers are healthy", activeWorkers);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during worker health check and restart");
        }

        return Task.CompletedTask;
    }

    private async Task<string> CalculateFileHashAsync(string filePath)
    {
        try
        {
            using SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = await sha256.ComputeHashAsync(stream).ConfigureAwait(false);
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate hash for {FilePath}", filePath);
            // Fallback to file modification time
            FileInfo fileInfo = new FileInfo(filePath);
            return fileInfo.LastWriteTimeUtc.Ticks.ToString();
        }
    }

    private async Task CleanupAsync()
    {
        if (_disposed) return;
        
        _logger.LogInformation("Starting graceful shutdown of CachedMultiLevelWorker");
        
        try
        {
            // Phase 1: Stop file discovery (no new files will be queued)
            _logger.LogDebug("Phase 1: Stopping file discovery service");
            await _fileDiscoveryService.StopFileWatcherAsync().ConfigureAwait(false);
            
            // Phase 2: Complete the queue channel (no new jobs will be accepted)
            _logger.LogDebug("Phase 2: Completing processing queue channel");
            _queueService.CompleteChannel();
            
            // Phase 3: Cancel processing tasks gracefully
            _logger.LogDebug("Phase 3: Cancelling {TaskCount} processing tasks", _processingTasks.Count);
            _processingCts.Cancel();

            // Phase 4: Wait for processing tasks to complete with timeout
            if (_processingTasks.Any())
            {
                _logger.LogDebug("Phase 4: Waiting for processing tasks to complete (30s timeout)");
                
                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                try
                {
                    await Task.WhenAll(_processingTasks).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                    _logger.LogInformation("All processing tasks completed gracefully");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Processing tasks did not complete within timeout, forcing cancellation");
                    // Tasks may still be running but we'll proceed with disposal
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for processing tasks to complete");
                }
            }
            
            // Phase 5: Log final statistics
            try
            {
                var queueStats = await _queueService.GetStatisticsAsync();
                _logger.LogInformation("Final queue statistics - Pending: {Pending}, Active: {Active}, Completed: {Completed}, Failed: {Failed}",
                    queueStats.PendingJobs, queueStats.ActiveJobs, queueStats.CompletedJobs, queueStats.FailedJobs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to retrieve final queue statistics");
            }
            
            _logger.LogInformation("CachedMultiLevelWorker cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cached multi-level cleanup");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CachedMultiLevelWorker stop requested");
        
        if (!_disposed)
        {
            _processingCts.Cancel();
            
            // Use a timeout for the base StopAsync to prevent hanging
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(45)); // Allow slightly more time than cleanup
            
            try
            {
                await base.StopAsync(timeoutCts.Token).ConfigureAwait(false);
                _logger.LogInformation("CachedMultiLevelWorker stopped successfully");
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("CachedMultiLevelWorker stop operation timed out");
            }
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        try
        {
            _logger.LogDebug("Disposing CachedMultiLevelWorker services in dependency order");
            
            // Dispose services in reverse dependency order
            // 1. Worker's own resources first
            _processingCts?.Dispose();
            
            // 2. File discovery service (stops watching for new files)
            _fileDiscoveryService?.Dispose();
            
            // 3. Queue service (stops accepting new jobs and cleans up queues)
            _queueService?.Dispose();
            
            // 4. Processor service (disposes AI model resources)
            _processorService?.Dispose();
            
            // 5. Cache service last (maintains data integrity)
            _cacheService?.Dispose();
            
            _logger.LogDebug("CachedMultiLevelWorker disposal completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during CachedMultiLevelWorker disposal");
        }
        finally
        {
            base.Dispose();
        }
    }
}