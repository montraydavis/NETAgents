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

    public async Task<IEnumerable<FileProcessingJob>> DiscoverFilesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_options.InputDirectory))
        {
            _logger.LogWarning("Input directory {Directory} does not exist", _options.InputDirectory);
            return Enumerable.Empty<FileProcessingJob>();
        }

        try
        {
            var files = Directory.GetFiles(_options.InputDirectory, _options.FilePattern)
                .Where(f => !_processedFiles.Contains(f))
                .Select(CreateJobFromFile)
                .ToList();

            _logger.LogInformation("Discovered {Count} new files in {Directory}", files.Count, _options.InputDirectory);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering files in {Directory}", _options.InputDirectory);
            return Enumerable.Empty<FileProcessingJob>();
        }
    }

    public async Task StartFileWatcherAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.EnableFileWatcher || !Directory.Exists(_options.InputDirectory))
        {
            return;
        }

        try
        {
            _fileWatcher = new FileSystemWatcher(_options.InputDirectory)
            {
                Filter = _options.FilePattern.Replace("*", ""),
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _fileWatcher.Created += async (sender, e) => await OnFileCreated(e);
            _fileWatcher.Changed += async (sender, e) => await OnFileChanged(e);

            _logger.LogInformation("File watcher started for {Directory} with pattern {Pattern}", 
                _options.InputDirectory, _options.FilePattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start file watcher for {Directory}", _options.InputDirectory);
        }
    }

    public Task StopFileWatcherAsync()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
            _logger.LogInformation("File watcher stopped");
        }

        return Task.CompletedTask;
    }

    private async Task OnFileCreated(FileSystemEventArgs e)
    {
        try
        {
            // Wait a bit to ensure file is fully written
            await Task.Delay(1000);
            
            if (File.Exists(e.FullPath) && !_processedFiles.Contains(e.FullPath))
            {
                var job = CreateJobFromFile(e.FullPath);
                await _queueService.EnqueueJobAsync(job);
                _processedFiles.Add(e.FullPath);
                
                _logger.LogInformation("New file detected and queued: {FilePath}", e.FullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file creation event for {FilePath}", e.FullPath);
        }
    }

    private async Task OnFileChanged(FileSystemEventArgs e)
    {
        try
        {
            // Wait a bit to ensure file is fully written
            await Task.Delay(1000);
            
            if (File.Exists(e.FullPath) && !_processedFiles.Contains(e.FullPath))
            {
                var job = CreateJobFromFile(e.FullPath);
                await _queueService.EnqueueJobAsync(job);
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
            string text = File.ReadAllText(filePath);
            string[] sections = text.Split("---").Select(s => s.Trim().Trim('\n')).Skip(2).ToArray();
            string content = string.Join("\n", sections);

            return new FileProcessingJob
            {
                FilePath = filePath,
                Content = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {FilePath}", filePath);
            throw;
        }
    }
}
