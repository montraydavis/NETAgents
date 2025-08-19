using System.Security.Cryptography;
using System.Text.Json;
using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Models.Ast;
using NETAgents.Tools.Processing.Services.Query;

namespace NETAgents.Tools.Processing.Cache;

public class FileBasedCacheService : ICacheService, IDisposable
{
    private readonly ILogger<FileBasedCacheService> _logger;
    private readonly string _cacheDirectory;
    private readonly FileQueryService _fileQueryService;
    private readonly AstQueryService _astQueryService;
    private readonly DomainQueryService _domainQueryService;
    private Timer? _cleanupTimer;
    private readonly CancellationTokenSource _cleanupCancellationTokenSource = new();
    private volatile bool _disposed;
    private readonly object _cacheStatsLock = new();
    private long _currentCacheSizeBytes = 0;
    private int _currentCacheEntries = 0;

    public FileBasedCacheService(ILogger<FileBasedCacheService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        
        // Initialize cache directory
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NETAgents",
            "ProcessingCache"
        );
        
        // Ensure directory exists
        Directory.CreateDirectory(_cacheDirectory);
        
        _logger.LogInformation("FileBasedCacheService initialized with cache directory: {CacheDirectory}", _cacheDirectory);
        
        // Initialize specialized query services
        _fileQueryService = new FileQueryService(loggerFactory.CreateLogger<FileQueryService>(), _cacheDirectory);
        _astQueryService = new AstQueryService(loggerFactory.CreateLogger<AstQueryService>(), _cacheDirectory);
        _domainQueryService = new DomainQueryService(loggerFactory.CreateLogger<DomainQueryService>(), _cacheDirectory);
        
        // Setup cleanup timer with proper async handling
        _cleanupTimer = new Timer(CleanupTimerCallback, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        
        // Initialize cache statistics
        InitializeCacheStatistics();
    }

    private void InitializeCacheStatistics()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                _currentCacheEntries = cacheFiles.Length;
                _currentCacheSizeBytes = cacheFiles.Sum(f => new FileInfo(f).Length);
                
                _logger.LogInformation("Cache initialized with {Entries} entries, {Size} bytes", 
                    _currentCacheEntries, _currentCacheSizeBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing cache statistics");
        }
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
                await CleanupStaleEntriesAsync(TimeSpan.FromHours(24), _cleanupCancellationTokenSource.Token).ConfigureAwait(false);
                await PerformCacheEvictionAsync(_cleanupCancellationTokenSource.Token).ConfigureAwait(false);
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

    private async Task PerformCacheEvictionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get current cache statistics
            var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
            
            // Check if we need to evict based on size or entry count
            bool needsEviction = stats.TotalSizeBytes > 500 * 1024 * 1024 || // 500MB
                                stats.TotalFiles > 10000; // 10,000 entries
            
            if (needsEviction)
            {
                _logger.LogInformation("Cache eviction needed - Size: {Size} bytes, Entries: {Entries}", 
                    stats.TotalSizeBytes, stats.TotalFiles);
                
                await PerformLRUEvictionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache eviction");
        }
    }

    private async Task PerformLRUEvictionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get all cache entries with their access times
            var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
            var fileInfos = cacheFiles.Select(f => new FileInfo(f)).ToList();
            
            // Sort by last access time (oldest first)
            var sortedFiles = fileInfos.OrderBy(f => f.LastAccessTimeUtc).ToList();
            
            // Calculate how many to remove
            int targetEntries = 10000; // Target 10,000 entries
            int entriesToRemove = Math.Max(0, sortedFiles.Count - targetEntries);
            
            if (entriesToRemove > 0)
            {
                var filesToRemove = sortedFiles.Take(entriesToRemove).ToList();
                
                foreach (var file in filesToRemove)
                {
                    try
                    {
                        File.Delete(file.FullName);
                        _logger.LogDebug("Evicted cache file: {FileName}", file.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete cache file: {FileName}", file.Name);
                    }
                }
                
                _logger.LogInformation("LRU eviction completed - removed {Count} cache entries", filesToRemove.Count);
                
                // Update cache statistics
                lock (_cacheStatsLock)
                {
                    _currentCacheEntries = Math.Max(0, _currentCacheEntries - filesToRemove.Count);
                    _currentCacheSizeBytes = Math.Max(0, _currentCacheSizeBytes - filesToRemove.Sum(f => f.Length));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LRU cache eviction");
        }
    }

    public async Task StoreProcessedFileAsync(MultiLevelProcessingJob job, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        try
        {
            FileInfo fileInfo = new FileInfo(job.FilePath);
            string fileHash = await CalculateFileHashAsync(job.FilePath).ConfigureAwait(false);
            
            ProcessedFileEntry entry = new ProcessedFileEntry
            {
                Id = job.Id,
                FilePath = job.FilePath,
                FileHash = fileHash,
                ProcessedAt = DateTime.UtcNow,
                LastModified = fileInfo.LastWriteTimeUtc,
                FileSize = fileInfo.Length,
                Status = job.Status
            };

            // Process each level's data
            foreach ((JobProcessingLevel level, JobProcessingResult result) in job.Results)
            {
                ProcessedLevelData levelData = new ProcessedLevelData
                {
                    Content = result.Content ?? string.Empty,
                    ProcessedAt = result.ProcessedAt,
                    ProcessingDuration = result.ProcessingDuration,
                    IsSuccess = result.IsSuccess,
                    ErrorMessage = result.ErrorMessage
                };

                // Index the data for fast querying
                levelData.IndexedData = await IndexLevelDataAsync(level, result.Content ?? string.Empty).ConfigureAwait(false);
                entry.LevelData[level] = levelData;
            }

            // Save to file without locking (file operations are already thread-safe)
            await SaveToCacheAsync(entry).ConfigureAwait(false);
            
            // Update cache statistics
            lock (_cacheStatsLock)
            {
                _currentCacheEntries++;
                // Note: Size will be updated when we actually write the file
            }
            
            _logger.LogInformation("Stored processed file entry for {FilePath} in cache", job.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing processed file entry for {FilePath}", job.FilePath);
            throw;
        }
    }

    public async Task<ProcessedFileEntry?> GetProcessedFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // Don't use locks for simple file reads - file operations are already thread-safe
            return await LoadFromCacheAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cache entry for {FilePath}", filePath);
            return null;
        }
    }

    public async Task<bool> IsFileProcessedAsync(string filePath, string currentFileHash, CancellationToken cancellationToken = default)
    {
        try
        {
            string cacheKey = GenerateCacheKey(filePath);
            string cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            if (!File.Exists(cacheFilePath))
                return false;
            
            string jsonContent = await File.ReadAllTextAsync(cacheFilePath, cancellationToken).ConfigureAwait(false);
            ProcessedFileEntry? cachedEntry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
            
            if (cachedEntry == null)
                return false;
            
            return cachedEntry.Status == JobProcessingStatus.Completed && 
                   cachedEntry.FileHash == currentFileHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if file is processed: {FilePath}", filePath);
            return false;
        }
    }

    public async Task InvalidateFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // Don't use locks for file operations - they are already thread-safe
            await RemoveFromCacheAsync(filePath).ConfigureAwait(false);
            _logger.LogDebug("Invalidated cache entry for {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache entry for {FilePath}", filePath);
        }
    }

    public async Task<QueryResult<ProcessedFileEntry>> GetAllProcessedFilesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _fileQueryService.GetAllProcessedFilesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult<AstQueryResult>> GetAllAstNodesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _astQueryService.GetAllAstNodesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult<DomainResult>> GetAllDomainsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _domainQueryService.GetAllDomainsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult<ProcessedFileEntry>> QueryFilesAsync(FileQueryRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _fileQueryService.QueryFilesAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult<AstQueryResult>> QueryAstAsync(AstQueryRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _astQueryService.QueryAstAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueryResult<DomainResult>> QueryDomainsAsync(DomainQueryRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _domainQueryService.QueryDomainsAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // Get basic statistics from tracked data
            int totalFiles;
            long totalSizeBytes;
            
            lock (_cacheStatsLock)
            {
                totalFiles = _currentCacheEntries;
                totalSizeBytes = _currentCacheSizeBytes;
            }
            
            CacheStatistics stats = new CacheStatistics
            {
                TotalFiles = totalFiles,
                ProcessedFiles = 0, // Will be calculated if needed
                FailedFiles = 0,    // Will be calculated if needed
                TotalSizeBytes = totalSizeBytes
            };

            if (totalFiles > 0)
            {
                // Get file timestamps for oldest/newest entries
                try
                {
                    string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                    if (cacheFiles.Length > 0)
                    {
                        List<FileInfo> fileInfos = cacheFiles.Select(f => new FileInfo(f)).ToList();
                        stats.OldestEntry = fileInfos.Min(f => f.LastWriteTimeUtc);
                        stats.NewestEntry = fileInfos.Max(f => f.LastWriteTimeUtc);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting file timestamps for cache statistics");
                    stats.OldestEntry = DateTime.UtcNow;
                    stats.NewestEntry = DateTime.UtcNow;
                }
                
                // Only load a sample of files for detailed statistics if there are many files
                if (totalFiles <= 100)
                {
                    // For small cache, load all files for detailed stats
                    QueryResult<ProcessedFileEntry> queryResult = await _fileQueryService.GetAllProcessedFilesAsync().ConfigureAwait(false);
                    List<ProcessedFileEntry> entries = queryResult.Items;
                    stats.ProcessedFiles = entries.Count(e => e.Status == JobProcessingStatus.Completed);
                    stats.FailedFiles = entries.Count(e => e.Status == JobProcessingStatus.Failed);
                    
                    foreach (JobProcessingLevel level in Enum.GetValues<JobProcessingLevel>())
                    {
                        stats.LevelCounts[level] = entries.Count(e => e.LevelData.ContainsKey(level));
                    }
                }
                else
                {
                    // For large cache, use file count as approximation
                    stats.ProcessedFiles = totalFiles; // Assume most files are processed successfully
                    stats.FailedFiles = 0; // Assume minimal failures
                    
                    // Set default level counts based on typical processing
                    foreach (JobProcessingLevel level in Enum.GetValues<JobProcessingLevel>())
                    {
                        stats.LevelCounts[level] = totalFiles; // Assume all files have all levels
                    }
                }
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache statistics");
            
            return new CacheStatistics
            {
                TotalFiles = 0,
                ProcessedFiles = 0,
                FailedFiles = 0,
                TotalSizeBytes = 0,
                OldestEntry = DateTime.UtcNow,
                NewestEntry = DateTime.UtcNow,
                LevelCounts = new Dictionary<JobProcessingLevel, int>()
            };
        }
    }

    public async Task CleanupStaleEntriesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        DateTime cutoffTime = DateTime.UtcNow - maxAge;
        int removedCount = 0;
        
        try
        {
            // Don't use locks for file operations - they are already thread-safe
            QueryResult<ProcessedFileEntry> queryResult = await _fileQueryService.GetAllProcessedFilesAsync().ConfigureAwait(false);
            List<ProcessedFileEntry> allFiles = queryResult.Items;
            List<ProcessedFileEntry> staleEntries = allFiles
                .Where(e => e.ProcessedAt < cutoffTime)
                .ToList();

            foreach (ProcessedFileEntry entry in staleEntries)
            {
                await RemoveFromCacheAsync(entry.FilePath).ConfigureAwait(false);
                removedCount++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup stale entries");
        }

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} stale cache entries older than {MaxAge}", removedCount, maxAge);
        }
    }

    // File-based storage methods
    private async Task SaveToCacheAsync(ProcessedFileEntry entry)
    {
        try
        {
            string cacheKey = GenerateCacheKey(entry.FilePath);
            string cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            string jsonContent = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(cacheFilePath, jsonContent).ConfigureAwait(false);
            _logger.LogDebug("Saved cache entry to: {CacheFilePath} for file: {FilePath}", cacheFilePath, entry.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cache entry for {FilePath}", entry.FilePath);
        }
    }

    private async Task<ProcessedFileEntry?> LoadFromCacheAsync(string filePath)
    {
        try
        {
            string cacheKey = GenerateCacheKey(filePath);
            string cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            if (!File.Exists(cacheFilePath))
                return null;

            string jsonContent = await File.ReadAllTextAsync(cacheFilePath).ConfigureAwait(false);
            ProcessedFileEntry? entry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
            
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cache entry for {FilePath}", filePath);
            return null;
        }
    }

    private string GenerateCacheKey(string filePath)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath));
        return Convert.ToHexString(hashBytes).ToLower();
    }



    private Task RemoveFromCacheAsync(string filePath)
    {
        try
        {
            string cacheKey = GenerateCacheKey(filePath);
            string cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            if (File.Exists(cacheFilePath))
            {
                var fileInfo = new FileInfo(cacheFilePath);
                long fileSize = fileInfo.Length;
                
                File.Delete(cacheFilePath);
                
                // Update cache statistics
                lock (_cacheStatsLock)
                {
                    _currentCacheEntries = Math.Max(0, _currentCacheEntries - 1);
                    _currentCacheSizeBytes = Math.Max(0, _currentCacheSizeBytes - fileSize);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache entry for {FilePath}", filePath);
        }
        
        return Task.CompletedTask;
    }



    private async Task<Dictionary<string, object>> IndexLevelDataAsync(JobProcessingLevel level, string content)
    {
        Dictionary<string, object> indexedData = new Dictionary<string, object>();

        try
        {
            switch (level)
            {
                case JobProcessingLevel.Ast:
                    await IndexAstDataAsync(content, indexedData).ConfigureAwait(false);
                    break;
                case JobProcessingLevel.DomainKeywords:
                    await IndexDomainDataAsync(content, indexedData).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to index {Level} data", level);
            // Use a safe logging approach that doesn't interfere with format strings
            _logger.LogWarning("Indexing error details for {Level}: {ErrorMessage}", level, ex.Message);
        }

        return indexedData;
    }

    private async Task IndexAstDataAsync(string astContent, Dictionary<string, object> indexedData)
    {
        try
        {
            // Use more lenient JSON parsing options
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            AstCompilationUnit? astData = JsonSerializer.Deserialize<AstCompilationUnit>(astContent, options);
            if (astData == null) return;

            indexedData["namespace"] = astData.Namespace ?? string.Empty;
            indexedData["usings"] = astData.Usings?.ToArray() ?? new string[0];
            indexedData["classCount"] = astData.Classes?.Count ?? 0;
            indexedData["interfaceCount"] = astData.Interfaces?.Count ?? 0;
            indexedData["enumCount"] = astData.Enums?.Count ?? 0;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse AST JSON for indexing: {ErrorMessage}", ex.Message);
            // Set default values instead of failing
            indexedData["namespace"] = string.Empty;
            indexedData["usings"] = new string[0];
            indexedData["classCount"] = 0;
            indexedData["interfaceCount"] = 0;
            indexedData["enumCount"] = 0;
        }
        
        await Task.CompletedTask;
    }

    private async Task IndexDomainDataAsync(string domainContent, Dictionary<string, object> indexedData)
    {
        try
        {
            // Use more lenient JSON parsing options
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            DomainKeywordsResponse? domainData = JsonSerializer.Deserialize<DomainKeywordsResponse>(domainContent, options);
            if (domainData?.Domains == null) return;

            indexedData["domainCount"] = domainData.Domains.Count;
            indexedData["domainNames"] = domainData.Domains.Select(d => d.Name).ToArray();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse domain keywords JSON for indexing: {ErrorMessage}", ex.Message);
            // Set default values instead of failing
            indexedData["domainCount"] = 0;
            indexedData["domainNames"] = new string[0];
        }
        
        await Task.CompletedTask;
    }



    private async Task<string> CalculateFileHashAsync(string filePath)
    {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await sha256.ComputeHashAsync(stream).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileBasedCacheService));
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing FileBasedCacheService");
        }
    }
}
