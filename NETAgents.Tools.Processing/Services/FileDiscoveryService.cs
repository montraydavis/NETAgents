using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services;

public interface IFileDiscoveryService
{
    Task<IEnumerable<FileProcessingJob>> DiscoverFilesAsync(CancellationToken cancellationToken = default);
    Task StartFileWatcherAsync(CancellationToken cancellationToken = default);
    Task StopFileWatcherAsync();
}

public class FileDiscoveryService : IFileDiscoveryService
{
    private readonly ILogger<FileDiscoveryService> _logger;
    private readonly ProcessingOptions _options;
    private readonly IProcessingQueueService _queueService;
    private FileSystemWatcher? _fileWatcher;
    private readonly HashSet<string> _processedFiles;

    public FileDiscoveryService(
        ILogger<FileDiscoveryService> logger, 
        ProcessingOptions options, 
        IProcessingQueueService queueService)
    {
        _logger = logger;
        _options = options;
        _queueService = queueService;
        _processedFiles = new HashSet<string>();
    }

    public Task<IEnumerable<FileProcessingJob>> DiscoverFilesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DiscoverFilesAsync called");
        
        if (!Directory.Exists(_options.InputDirectory))
        {
            _logger.LogWarning("Input directory {Directory} does not exist", _options.InputDirectory);
            return Task.FromResult(Enumerable.Empty<FileProcessingJob>());
        }

        try
        {
            _logger.LogInformation("Getting files from directory: {Directory} with pattern: {Pattern}", _options.InputDirectory, _options.FilePattern);
            var files = Directory.GetFiles(_options.InputDirectory, _options.FilePattern);
            _logger.LogInformation("Found {Count} files matching pattern", files.Length);
            
            var filteredFiles = files.Where(f => !_processedFiles.Contains(f)).ToList();
            _logger.LogInformation("After filtering processed files: {Count} files", filteredFiles.Count);
            
            var jobs = filteredFiles.Select(CreateJobFromFile).ToList();
            _logger.LogInformation("Created {Count} jobs from files", jobs.Count);

            _logger.LogInformation("Discovered {Count} new files in {Directory}", jobs.Count, _options.InputDirectory);
            return Task.FromResult<IEnumerable<FileProcessingJob>>(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering files in {Directory}", _options.InputDirectory);
            return Task.FromResult(Enumerable.Empty<FileProcessingJob>());
        }
    }

    public Task StartFileWatcherAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("StartFileWatcherAsync called");
        
        if (!_options.EnableFileWatcher || !Directory.Exists(_options.InputDirectory))
        {
            _logger.LogInformation("File watcher disabled or directory doesn't exist. EnableFileWatcher: {EnableFileWatcher}, DirectoryExists: {DirectoryExists}", 
                _options.EnableFileWatcher, Directory.Exists(_options.InputDirectory));
            return Task.CompletedTask;
        }

        try
        {
            _logger.LogInformation("Creating FileSystemWatcher for directory: {Directory}", _options.InputDirectory);
            _fileWatcher = new FileSystemWatcher(_options.InputDirectory)
            {
                Filter = _options.FilePattern.Replace("*", ""),
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _logger.LogInformation("Setting up file watcher event handlers");
            // Use proper async event handlers with cancellation support
            _fileWatcher.Created += (sender, e) => 
            {
                _logger.LogDebug("File created event triggered: {FilePath}", e.FullPath);
                _ = Task.Run(async () => await OnFileCreated(e, cancellationToken), cancellationToken);
            };
            
            _fileWatcher.Changed += (sender, e) => 
            {
                _logger.LogDebug("File changed event triggered: {FilePath}", e.FullPath);
                _ = Task.Run(async () => await OnFileChanged(e, cancellationToken), cancellationToken);
            };

            _logger.LogInformation("File watcher started for {Directory} with pattern {Pattern}", 
                _options.InputDirectory, _options.FilePattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start file watcher for {Directory}", _options.InputDirectory);
        }

        _logger.LogInformation("StartFileWatcherAsync completed");
        return Task.CompletedTask;
    }

    public Task StopFileWatcherAsync()
    {
        if (_fileWatcher != null)
        {
            try
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Created -= null; // Remove event handlers
                _fileWatcher.Changed -= null; // Remove event handlers
                _fileWatcher.Dispose();
                _fileWatcher = null;
                _logger.LogInformation("File watcher stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping file watcher");
            }
        }

        return Task.CompletedTask;
    }

    private async Task OnFileCreated(FileSystemEventArgs e, CancellationToken cancellationToken)
    {
        try
        {
            // Wait a bit to ensure file is fully written
            await Task.Delay(1000, cancellationToken);
            
            if (File.Exists(e.FullPath) && !_processedFiles.Contains(e.FullPath))
            {
                var job = CreateJobFromFile(e.FullPath);
                await _queueService.EnqueueJobAsync(job, cancellationToken);
                _processedFiles.Add(e.FullPath);
                
                _logger.LogInformation("New file detected and queued: {FilePath}", e.FullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file creation event for {FilePath}", e.FullPath);
        }
    }

    private async Task OnFileChanged(FileSystemEventArgs e, CancellationToken cancellationToken)
    {
        try
        {
            // Wait a bit to ensure file is fully written
            await Task.Delay(1000, cancellationToken);
            
            if (File.Exists(e.FullPath) && !_processedFiles.Contains(e.FullPath))
            {
                var job = CreateJobFromFile(e.FullPath);
                await _queueService.EnqueueJobAsync(job, cancellationToken);
                _processedFiles.Add(e.FullPath);
                
                _logger.LogInformation("Modified file detected and queued: {FilePath}", e.FullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change event for {FilePath}", e.FullPath);
        }
    }

    private FileProcessingJob CreateJobFromFile(string filePath)
    {
        try
        {
            _logger.LogDebug("Creating job from file: {FilePath}", filePath);
            
            string text = File.ReadAllText(filePath);
            _logger.LogDebug("Read {Length} characters from file: {FilePath}", text.Length, filePath);
            
            string[] sections = text.Split("---").Select(s => s.Trim().Trim('\n')).Skip(2).ToArray();
            _logger.LogDebug("Split into {Count} sections for file: {FilePath}", sections.Length, filePath);
            
            string content = string.Join("\n", sections);
            _logger.LogDebug("Created content of {Length} characters for file: {FilePath}", content.Length, filePath);

            var job = new FileProcessingJob
            {
                FilePath = filePath,
                Content = content
            };
            
            _logger.LogDebug("Created job {JobId} for file: {FilePath}", job.Id, filePath);
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {FilePath}", filePath);
            throw;
        }
    }
}
