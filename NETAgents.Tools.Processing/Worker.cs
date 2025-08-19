using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Services;

namespace NETAgents.Tools.Processing;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ProcessingOptions _options;
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly IProcessingQueueService _queueService;
    private readonly IFileProcessorService _fileProcessorService;
    private readonly List<Task> _processingTasks;
    private readonly CancellationTokenSource _processingCts;

    public Worker(
        ILogger<Worker> logger,
        ProcessingOptions options,
        IFileDiscoveryService fileDiscoveryService,
        IProcessingQueueService queueService,
        IFileProcessorService fileProcessorService)
    {
        _logger = logger;
        _options = options;
        _fileDiscoveryService = fileDiscoveryService;
        _queueService = queueService;
        _fileProcessorService = fileProcessorService;
        _processingTasks = new List<Task>();
        _processingCts = new CancellationTokenSource();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting file processing worker service");

        try
        {
            _logger.LogInformation("About to call StartFileDiscoveryAsync...");
            await StartFileDiscoveryAsync(stoppingToken);
            _logger.LogInformation("StartFileDiscoveryAsync completed");
            
            // Start monitoring loop concurrently
            _logger.LogInformation("About to start monitoring loop...");
            var monitoringTask = Task.Run(async () => await RunMonitoringLoopAsync(stoppingToken), stoppingToken);
            _logger.LogInformation("Monitoring loop started");
            
            // Wait for cancellation - the processing tasks will run indefinitely until cancelled
            _logger.LogInformation("About to wait for cancellation...");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker service is shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in worker service");
            throw;
        }
        finally
        {
            _logger.LogInformation("About to call CleanupAsync...");
            await CleanupAsync();
            _logger.LogInformation("CleanupAsync completed");
        }
    }

    private async Task StartFileDiscoveryAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting file discovery service");

        try
        {
            _logger.LogInformation("About to call DiscoverFilesAsync...");
            var initialFiles = await _fileDiscoveryService.DiscoverFilesAsync(stoppingToken);
            _logger.LogInformation("DiscoverFilesAsync completed, found {Count} files", initialFiles.Count());
            
            // Start processing workers BEFORE enqueuing jobs to prevent queue blocking
            _logger.LogInformation("Starting processing workers before enqueuing jobs...");
            await StartProcessingWorkersAsync(stoppingToken);
            _logger.LogInformation("Processing workers started");
            
            _logger.LogInformation("Enqueuing {Count} discovered files", initialFiles.Count());
            
            int enqueued = 0;
            foreach (var job in initialFiles)
            {
                try
                {
                    _logger.LogDebug("About to enqueue job {JobNumber}/{Total} - {JobId}", ++enqueued, initialFiles.Count(), job.Id);
                    _logger.LogInformation("Enqueuing job {JobNumber}/{Total} for file: {FilePath}", enqueued, initialFiles.Count(), job.FilePath);
                    
                    await _queueService.EnqueueJobAsync(job, stoppingToken);
                    _logger.LogDebug("Successfully enqueued job {JobNumber}/{Total} - {JobId}", enqueued, initialFiles.Count(), job.Id);
                    
                    if (enqueued % 10 == 0) // Log every 10th job
                    {
                        _logger.LogInformation("Enqueued {Count}/{Total} jobs", enqueued, initialFiles.Count());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enqueue job {JobNumber} for file {FilePath}", enqueued, job.FilePath);
                    // Continue with other jobs
                }
            }

            _logger.LogInformation("Finished enqueuing {Enqueued} initial files", enqueued);
            
            _logger.LogInformation("About to call StartFileWatcherAsync...");
            await _fileDiscoveryService.StartFileWatcherAsync(stoppingToken);
            _logger.LogInformation("StartFileWatcherAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in StartFileDiscoveryAsync");
            throw;
        }
    }

    private async Task StartProcessingWorkersAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {WorkerCount} background processing workers", _options.MaxConcurrentProcessing);

        for (int i = 0; i < _options.MaxConcurrentProcessing; i++)
        {
            var workerId = i; // Capture the loop variable
            var workerTask = Task.Run(async () => await ProcessFilesAsync(workerId, stoppingToken), stoppingToken);
            _processingTasks.Add(workerTask);
            _logger.LogInformation("Started background worker {WorkerId}", workerId);
        }

        _logger.LogInformation("All {Count} background processing workers started", _processingTasks.Count);
        
        // Give workers a moment to start and log their status
        await Task.Delay(1000, stoppingToken);
        
        // Log the status of processing tasks
        for (int i = 0; i < _processingTasks.Count; i++)
        {
            var task = _processingTasks[i];
            _logger.LogInformation("Worker {WorkerId} task status: {Status}", i, task.Status);
        }
    }

    private async Task ProcessFilesAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Worker {WorkerId} attempting to dequeue job", workerId);
                var job = await _queueService.DequeueJobAsync(stoppingToken);
                
                if (job == null)
                {
                    // No jobs available, wait a bit before trying again
                    _logger.LogDebug("Worker {WorkerId} found no jobs, waiting...", workerId);
                    await Task.Delay(2000, stoppingToken); // Increased wait time to reduce CPU usage
                    continue;
                }

                _logger.LogInformation("Worker {WorkerId} processing job {JobId} for file {FilePath}", workerId, job.Id, job.FilePath);
                await ProcessJobWithResilience(job, workerId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker {WorkerId} cancelled", workerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background worker {WorkerId}", workerId);
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("Background worker {WorkerId} stopped", workerId);
    }

    private async Task ProcessJobWithResilience(FileProcessingJob job, int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {WorkerId} processing job {JobId} for file {FilePath}", 
            workerId, job.Id, job.FilePath);

        try
        {
            // Create a timeout token for this specific job
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);

            var result = await _fileProcessorService.ProcessFileAsync(job, timeoutCts.Token);
            _queueService.CompleteJob(job, true, result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Job {JobId} cancelled due to service shutdown", job.Id);
            _queueService.CompleteJob(job, false, errorMessage: "Service shutdown");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job {JobId} timed out after {Timeout}", job.Id, _options.ProcessingTimeout);
            await HandleJobFailure(job, "Processing timeout", stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId} for file {FilePath}", job.Id, job.FilePath);
            await HandleJobFailure(job, ex.Message, stoppingToken);
        }
    }

    private async Task HandleJobFailure(FileProcessingJob job, string errorMessage, CancellationToken stoppingToken)
    {
        if (_queueService.ShouldRetryJob(job, errorMessage))
        {
            try
            {
                await _queueService.EnqueueRetryJobAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue retry for job {JobId}", job.Id);
                _queueService.CompleteJob(job, false, errorMessage: $"Retry failed: {ex.Message}");
            }
        }
    }

    private async Task RunMonitoringLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting monitoring loop");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stats = await _queueService.GetStatisticsAsync();
                
                _logger.LogInformation(
                    "Queue Status - Pending: {Pending}, Active: {Active}, Completed: {Completed}, Failed: {Failed}, Avg Processing: {AvgTime:F2}s",
                    stats.PendingJobs, stats.ActiveJobs, stats.CompletedJobs, stats.FailedJobs, 
                    stats.AverageProcessingTime?.TotalSeconds ?? 0);

                // Check for failed processing tasks and restart them
                await CheckAndRestartFailedWorkers(stoppingToken);

                // Cleanup stale jobs periodically
                await _queueService.CleanupStaleJobsAsync(stoppingToken);

                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in monitoring loop");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private Task CheckAndRestartFailedWorkers(CancellationToken stoppingToken)
    {
        var failedTasks = _processingTasks.Where(t => t.IsFaulted || t.IsCompleted).ToList();
        
        if (failedTasks.Any())
        {
            _logger.LogWarning("Found {Count} failed/completed worker tasks, restarting them", failedTasks.Count);
            
            foreach (var failedTask in failedTasks)
            {
                _processingTasks.Remove(failedTask);
                
                if (failedTask.IsFaulted)
                {
                    _logger.LogError("Worker task failed: {Exception}", failedTask.Exception);
                }
                
                // Start a new worker to replace the failed one
                var workerId = _processingTasks.Count;
                var newWorkerTask = Task.Run(async () => await ProcessFilesAsync(workerId, stoppingToken), stoppingToken);
                _processingTasks.Add(newWorkerTask);
                
                _logger.LogInformation("Restarted worker {WorkerId}", workerId);
            }
        }

        return Task.CompletedTask;
    }

    private async Task CleanupAsync()
    {
        _logger.LogInformation("Cleaning up worker service");

        try
        {
            await _fileDiscoveryService.StopFileWatcherAsync();
            
            _queueService.CompleteChannel();
            _processingCts.Cancel();

            if (_processingTasks.Any())
            {
                _logger.LogInformation("Waiting for background processing tasks to complete");
                
                // Wait with timeout for graceful shutdown
                var timeout = Task.Delay(TimeSpan.FromSeconds(30));
                var completedTask = await Task.WhenAny(Task.WhenAll(_processingTasks), timeout);
                
                if (completedTask == timeout)
                {
                    _logger.LogWarning("Some processing tasks did not complete within timeout, forcing cancellation");
                    _processingCts.Cancel(); // Force cancellation of remaining tasks
                }
                else
                {
                    _logger.LogInformation("All processing tasks completed successfully");
                }
            }

            _logger.LogInformation("Worker service cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping worker service");
        
        _processingCts.Cancel();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _processingCts?.Dispose();
        (_queueService as IDisposable)?.Dispose();
        base.Dispose();
    }
}