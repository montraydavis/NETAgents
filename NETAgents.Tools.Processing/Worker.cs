using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Services;
using NETAgents.Tools.Processing.Cache;

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
            await StartFileDiscoveryAsync(stoppingToken);
            var monitoringTask = Task.Run(async () => await RunMonitoringLoopAsync(stoppingToken), stoppingToken);
            await Task.Delay(Timeout.Infinite, stoppingToken);
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
            await CleanupAsync();
        }
    }

    private async Task StartFileDiscoveryAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting file discovery service with cache checking");

        var initialFiles = await _fileDiscoveryService.DiscoverFilesAsync(stoppingToken);
        await StartProcessingWorkersAsync(stoppingToken);

        var newFilesCount = 0;
        var cachedFilesCount = 0;

        foreach (var file in initialFiles)
        {
            try
            {
                // Calculate current file hash
                var currentHash = await CalculateFileHashAsync(file.FilePath);

                // Check if file is already processed and up-to-date
                if (await _cacheService.IsFileProcessedAsync(file.FilePath, currentHash, stoppingToken))
                {
                    cachedFilesCount++;
                    _logger.LogDebug("Skipping already processed file: {FilePath}", file.FilePath);
                    continue;
                }

                // File needs processing
                var multiLevelJob = ConvertToMultiLevelJob(file);
                await _queueService.EnqueueJobAsync(multiLevelJob, stoppingToken);
                newFilesCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking cache for file {FilePath}", file.FilePath);
                // Fallback to processing the file
                var multiLevelJob = ConvertToMultiLevelJob(file);
                await _queueService.EnqueueJobAsync(multiLevelJob, stoppingToken);
                newFilesCount++;
            }
        }

        _logger.LogInformation("Discovery complete - New files to process: {NewFiles}, Cached files skipped: {CachedFiles}",
            newFilesCount, cachedFilesCount);

        await _fileDiscoveryService.StartFileWatcherAsync(stoppingToken);
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
            RequiredLevels = new List<ProcessingLevel>
            {
                ProcessingLevel.Ast,
                ProcessingLevel.DomainKeywords
            }
        };
    }

    private Task StartProcessingWorkersAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {WorkerCount} cached multi-level processing workers", _options.MaxConcurrentProcessing);

        for (int i = 0; i < _options.MaxConcurrentProcessing; i++)
        {
            var workerId = i;
            var workerTask = Task.Run(async () => await ProcessFilesAsync(workerId, stoppingToken), stoppingToken);
            _processingTasks.Add(workerTask);
        }

        return Task.CompletedTask;
    }

    private async Task ProcessFilesAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cached multi-level worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queueService.DequeueJobAsync(stoppingToken);

                if (job is not MultiLevelProcessingJob multiLevelJob)
                {
                    if (job != null)
                    {
                        multiLevelJob = ConvertToMultiLevelJob(job);
                    }
                    else
                    {
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }
                }

                _logger.LogInformation("Worker {WorkerId} processing cached multi-level job {JobId} for file {FilePath}",
                    workerId, multiLevelJob.Id, multiLevelJob.FilePath);

                await ProcessCachedMultiLevelJobAsync(multiLevelJob, workerId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in cached multi-level worker {WorkerId}", workerId);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task ProcessCachedMultiLevelJobAsync(MultiLevelProcessingJob job, int workerId, CancellationToken stoppingToken)
    {
        var startTime = DateTime.UtcNow;
        job.StartedAt = startTime;
        job.Status = ProcessingStatus.Processing;

        try
        {
            // Check if we have partial results in cache
            var cachedEntry = await _cacheService.GetProcessedFileAsync(job.FilePath, stoppingToken);
            if (cachedEntry != null)
            {
                // Load any successfully processed levels from cache
                foreach (var level in job.RequiredLevels)
                {
                    if (cachedEntry.LevelData.TryGetValue(level, out var cachedLevelData) && cachedLevelData.IsSuccess)
                    {
                        job.Results[level] = new ProcessingResult
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
            foreach (var level in job.RequiredLevels)
            {
                if (job.Results.ContainsKey(level) && job.Results[level].IsSuccess)
                {
                    _logger.LogDebug("Level {Level} already available for job {JobId}, skipping", level, job.Id);
                    continue;
                }

                _logger.LogInformation("Worker {WorkerId} processing {Level} for job {JobId}", workerId, level, job.Id);

                job.CurrentLevel = level;
                var result = await _processorService.ProcessLevelAsync(job, level, stoppingToken);
                job.Results[level] = result;

                if (!result.IsSuccess)
                {
                    _logger.LogError("Level {Level} failed for job {JobId}: {Error}", level, job.Id, result.ErrorMessage);
                    await HandleJobFailure(job, $"Level {level} failed: {result.ErrorMessage}", stoppingToken);
                    return;
                }

                _logger.LogInformation("Worker {WorkerId} completed {Level} for job {JobId} in {Duration:F2}s",
                    workerId, level, job.Id, result.ProcessingDuration.TotalSeconds);
            }

            // All levels completed successfully
            if (job.IsMultiLevelComplete)
            {
                // Final validation before caching - ensure all results contain valid JSON
                foreach (var result in job.Results.Values)
                {
                    if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(result.Content);
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
                await _cacheService.StoreProcessedFileAsync(job, stoppingToken);

                var aggregatedResult = AggregateResults(job);
                _queueService.CompleteJob(job, true, aggregatedResult);

                var totalDuration = DateTime.UtcNow - startTime;
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
            await HandleJobFailure(job, ex.Message, stoppingToken);
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

                await _queueService.EnqueueRetryJobAsync(job, stoppingToken);
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

                await CheckAndRestartFailedWorkers(stoppingToken);
                await _queueService.CleanupStaleJobsAsync(stoppingToken);

                // Note: Cache cleanup removed to prevent deadlocks
                // Cache cleanup can be done via scheduled tasks or manual cleanup

                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cached multi-level monitoring loop");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private Task CheckAndRestartFailedWorkers(CancellationToken stoppingToken)
    {
        var failedTasks = _processingTasks.Where(t => t.IsFaulted || t.IsCompleted).ToList();

        foreach (var failedTask in failedTasks)
        {
            _processingTasks.Remove(failedTask);

            var workerId = _processingTasks.Count;
            var newWorkerTask = Task.Run(async () => await ProcessFilesAsync(workerId, stoppingToken), stoppingToken);
            _processingTasks.Add(newWorkerTask);
        }

        return Task.CompletedTask;
    }

    private async Task<string> CalculateFileHashAsync(string filePath)
    {
        try
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate hash for {FilePath}", filePath);
            // Fallback to file modification time
            var fileInfo = new FileInfo(filePath);
            return fileInfo.LastWriteTimeUtc.Ticks.ToString();
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            await _fileDiscoveryService.StopFileWatcherAsync();
            _queueService.CompleteChannel();
            _processingCts.Cancel();

            if (_processingTasks.Any())
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(30));
                var completedTask = await Task.WhenAny(Task.WhenAll(_processingTasks), timeout);

                if (completedTask == timeout)
                {
                    _processingCts.Cancel();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cached multi-level cleanup");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _processingCts.Cancel();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _processingCts?.Dispose();
        (_queueService as IDisposable)?.Dispose();
        (_cacheService as IDisposable)?.Dispose();
        base.Dispose();
    }
}