using NETAgents.Models.Processing;
using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services
{
    public interface IFileDiscoveryService : IDisposable
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
        private readonly object _processedFilesLock = new();
    
        // Store event handler references for proper removal
        private FileSystemEventHandler? _fileCreatedHandler;
        private FileSystemEventHandler? _fileChangedHandler;
        private volatile bool _disposed;

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
                string[] files = Directory.GetFiles(_options.InputDirectory, _options.FilePattern);
                _logger.LogInformation("Found {Count} files matching pattern", files.Length);
            
                List<string> filteredFiles = files.Where(f => !IsFileProcessed(f)).ToList();
                _logger.LogInformation("After filtering processed files: {Count} files", filteredFiles.Count);
            
                List<FileProcessingJob> jobs = filteredFiles.Select(CreateJobFromFile).ToList();
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
                // Store event handler references for proper removal
                _fileCreatedHandler = (sender, e) => 
                {
                    if (_disposed) return;
                
                    _logger.LogDebug("File created event triggered: {FilePath}", e.FullPath);
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await OnFileCreated(e, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in file created event handler for {FilePath}", e.FullPath);
                        }
                    }, cancellationToken);
                };
            
                _fileChangedHandler = (sender, e) => 
                {
                    if (_disposed) return;
                
                    _logger.LogDebug("File changed event triggered: {FilePath}", e.FullPath);
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await OnFileChanged(e, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in file changed event handler for {FilePath}", e.FullPath);
                        }
                    }, cancellationToken);
                };
            
                // Subscribe to events
                _fileWatcher.Created += _fileCreatedHandler;
                _fileWatcher.Changed += _fileChangedHandler;

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
            if (_disposed) return Task.CompletedTask;
        
            _disposed = true;
        
            if (_fileWatcher != null)
            {
                try
                {
                    // Stop raising events first
                    _fileWatcher.EnableRaisingEvents = false;
                
                    // Remove event handlers using stored references
                    if (_fileCreatedHandler != null)
                    {
                        _fileWatcher.Created -= _fileCreatedHandler;
                        _fileCreatedHandler = null;
                    }
                
                    if (_fileChangedHandler != null)
                    {
                        _fileWatcher.Changed -= _fileChangedHandler;
                        _fileChangedHandler = null;
                    }
                
                    // Dispose the FileSystemWatcher
                    _fileWatcher.Dispose();
                    _fileWatcher = null;
                
                    _logger.LogInformation("File watcher stopped and event handlers removed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping file watcher");
                }
            }

            return Task.CompletedTask;
        }

        private void AddProcessedFileWithEviction(string filePath)
        {
            lock (_processedFilesLock)
            {
                _processedFiles.Add(filePath);
            
                // Check if we need to evict old entries
                if (_processedFiles.Count > _options.MaxProcessedFiles)
                {
                    // Remove oldest entries (simple approach - remove first entries)
                    // In a more sophisticated implementation, we could track timestamps
                    var filesToRemove = _processedFiles.Take(_processedFiles.Count - _options.MaxProcessedFiles).ToList();
                    foreach (var file in filesToRemove)
                    {
                        _processedFiles.Remove(file);
                    }
                
                    _logger.LogDebug("Evicted {Count} old processed files to maintain collection size limit", filesToRemove.Count);
                }
            }
        }

        private bool IsFileProcessed(string filePath)
        {
            lock (_processedFilesLock)
            {
                return _processedFiles.Contains(filePath);
            }
        }

        private async Task OnFileCreated(FileSystemEventArgs e, CancellationToken cancellationToken)
        {
            try
            {
                // Wait a bit to ensure file is fully written
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            
                if (File.Exists(e.FullPath) && !IsFileProcessed(e.FullPath))
                {
                    FileProcessingJob job = CreateJobFromFile(e.FullPath);
                    await _queueService.EnqueueJobAsync(job, cancellationToken).ConfigureAwait(false);
                    AddProcessedFileWithEviction(e.FullPath);
                
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
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            
                if (File.Exists(e.FullPath) && !IsFileProcessed(e.FullPath))
                {
                    FileProcessingJob job = CreateJobFromFile(e.FullPath);
                    await _queueService.EnqueueJobAsync(job, cancellationToken).ConfigureAwait(false);
                    AddProcessedFileWithEviction(e.FullPath);
                
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

                FileProcessingJob job = new FileProcessingJob
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

        public void Dispose()
        {
            if (_disposed) return;
        
            try
            {
                StopFileWatcherAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during FileDiscoveryService disposal");
            }
        }
    }
}
