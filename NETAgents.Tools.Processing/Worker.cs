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
            // Start file discovery and watcher
            await StartFileDiscoveryAsync(stoppingToken);

            // Start background processing workers
            await StartProcessingWorkersAsync(stoppingToken);

            // Main monitoring loop
            await RunMonitoringLoopAsync(stoppingToken);
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
            await CleanupAsync();
        }
    }

    private async Task StartFileDiscoveryAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting file discovery service");

        // Initial file discovery
        var initialFiles = await _fileDiscoveryService.DiscoverFilesAsync(stoppingToken);
        foreach (var job in initialFiles)
        {
            await _queueService.EnqueueJobAsync(job, stoppingToken);
        }

        // Start file watcher for new files
        await _fileDiscoveryService.StartFileWatcherAsync(stoppingToken);
    }

    private async Task StartProcessingWorkersAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting {WorkerCount} background processing workers", _options.MaxConcurrentProcessing);

        for (int i = 0; i < _options.MaxConcurrentProcessing; i++)
        {
            var workerTask = Task.Run(async () => await ProcessFilesAsync(i, stoppingToken), stoppingToken);
            _processingTasks.Add(workerTask);
        }

        _logger.LogInformation("All background processing workers started");
    }

    private async Task ProcessFilesAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background worker {WorkerId} started", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Try to dequeue a job
                var job = await _queueService.DequeueJobAsync(stoppingToken);
                
                if (job == null)
                {
                    // No jobs available, wait a bit before trying again
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(job, workerId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in background worker {WorkerId}", workerId);
                await Task.Delay(5000, stoppingToken); // Wait before retrying
            }
        }

        _logger.LogInformation("Background worker {WorkerId} stopped", workerId);
    }

    private async Task ProcessJobAsync(FileProcessingJob job, int workerId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {WorkerId} processing job {JobId} for file {FilePath}", 
            workerId, job.Id, job.FilePath);

        try
        {
            var result = await _fileProcessorService.ProcessFileAsync(job, stoppingToken);
            _queueService.CompleteJob(job, true, result);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning("Job {JobId} timed out: {Error}", job.Id, ex.Message);
            _queueService.CompleteJob(job, false, errorMessage: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId} for file {FilePath}", job.Id, job.FilePath);
            
            // Check if we should retry
            if (job.RetryCount < _options.MaxRetryAttempts)
            {
                _queueService.RetryJob(job, ex.Message);
                
                // Wait before retrying
                await Task.Delay(_options.RetryDelay, stoppingToken);
                
                // Re-queue the job for retry
                await _queueService.EnqueueJobAsync(job, stoppingToken);
            }
            else
            {
                _queueService.CompleteJob(job, false, errorMessage: ex.Message);
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
                // Log queue statistics
                var queueLength = await _queueService.GetQueueLengthAsync();
                var activeJobs = await _queueService.GetActiveJobsAsync();
                var activeJobCount = activeJobs.Count();

                _logger.LogInformation(
                    "Queue Status - Pending: {QueueLength}, Active: {ActiveCount}, Total Workers: {WorkerCount}",
                    queueLength, activeJobCount, _options.MaxConcurrentProcessing);

                // Check if any processing tasks have failed
                var failedTasks = _processingTasks.Where(t => t.IsFaulted).ToList();
                if (failedTasks.Any())
                {
                    _logger.LogError("Some background processing tasks have failed");
                    foreach (var task in failedTasks)
                    {
                        _logger.LogError("Task exception: {Exception}", task.Exception);
                    }
                }

                // Wait before next monitoring cycle
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

    private async Task CleanupAsync()
    {
        _logger.LogInformation("Cleaning up worker service");

        try
        {
            // Stop file watcher
            await _fileDiscoveryService.StopFileWatcherAsync();

            // Cancel processing
            _processingCts.Cancel();

            // Wait for processing tasks to complete
            if (_processingTasks.Any())
            {
                _logger.LogInformation("Waiting for background processing tasks to complete");
                await Task.WhenAll(_processingTasks);
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
        base.Dispose();
    }
}
