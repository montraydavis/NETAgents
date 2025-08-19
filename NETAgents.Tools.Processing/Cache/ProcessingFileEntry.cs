using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Models.Ast;
using NETAgents.Tools.Processing.Services.Query;

namespace NETAgents.Tools.Processing.Cache;


public class AstQueryRequest
{
    public string? NodeType { get; set; }
    public string? NodeName { get; set; }
    public string? Namespace { get; set; }
    public string[]? Modifiers { get; set; }
    public string[]? BaseTypes { get; set; }
    public string[]? Attributes { get; set; }
    public string? FilePath { get; set; }
    public string? FilePattern { get; set; }
}

public class ProcessedFileEntry
{
    public Guid Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
    public ProcessingStatus Status { get; set; }
    public Dictionary<ProcessingLevel, ProcessedLevelData> LevelData { get; set; } = new();
}

public class ProcessedLevelData
{
    public string Content { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Parsed and indexed data for fast querying
    public Dictionary<string, object> IndexedData { get; set; } = new();
}

public class FileQueryRequest
{
    public string? FilePath { get; set; }
    public string? FilePattern { get; set; }
    public ProcessingLevel? Level { get; set; }
    public DateTime? ProcessedAfter { get; set; }
    public DateTime? ProcessedBefore { get; set; }
    public ProcessingStatus? Status { get; set; }
    public Dictionary<string, object>? IndexedDataFilters { get; set; }
}

public class NodeQueryRequest
{
    public string? NodeType { get; set; }
    public string? NodeName { get; set; }
    public string? Namespace { get; set; }
    public string[]? Modifiers { get; set; }
    public string[]? BaseTypes { get; set; }
    public string[]? Attributes { get; set; }
    public string? FilePath { get; set; }
    public string? FilePattern { get; set; }
}

public class DomainQueryRequest
{
    public string? DomainName { get; set; }
    public string? DomainPattern { get; set; }
    public string? FilePath { get; set; }
    public string? FilePattern { get; set; }
}

public class QueryResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public TimeSpan QueryDuration { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}






public interface ICacheService
{
    // Storage operations
    Task StoreProcessedFileAsync(MultiLevelProcessingJob job, CancellationToken cancellationToken = default);
    Task<ProcessedFileEntry?> GetProcessedFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> IsFileProcessedAsync(string filePath, string currentFileHash, CancellationToken cancellationToken = default);
    Task InvalidateFileAsync(string filePath, CancellationToken cancellationToken = default);
    
    // Bulk retrieval operations
    Task<QueryResult<ProcessedFileEntry>> GetAllProcessedFilesAsync(CancellationToken cancellationToken = default);
    Task<QueryResult<AstQueryResult>> GetAllAstNodesAsync(CancellationToken cancellationToken = default);
    Task<QueryResult<DomainResult>> GetAllDomainsAsync(CancellationToken cancellationToken = default);
    
    // Query operations
    Task<QueryResult<ProcessedFileEntry>> QueryFilesAsync(FileQueryRequest request, CancellationToken cancellationToken = default);
    Task<QueryResult<AstQueryResult>> QueryAstAsync(AstQueryRequest request, CancellationToken cancellationToken = default);
    Task<QueryResult<DomainResult>> QueryDomainsAsync(DomainQueryRequest request, CancellationToken cancellationToken = default);
    
    // Statistics
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task CleanupStaleEntriesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}

public class CacheStatistics
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int FailedFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateTime OldestEntry { get; set; }
    public DateTime NewestEntry { get; set; }
    public Dictionary<ProcessingLevel, int> LevelCounts { get; set; } = new();
}

public class FileBasedCacheService : ICacheService, IDisposable
{
    private readonly ILogger<FileBasedCacheService> _logger;
    private readonly string _cacheDirectory;
    private readonly FileQueryService _fileQueryService;
    private readonly AstQueryService _astQueryService;
    private readonly DomainQueryService _domainQueryService;
    private readonly Timer _cleanupTimer;
    private volatile bool _disposed;

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
        
        // Setup cleanup timer
        _cleanupTimer = new Timer(async _ => await CleanupStaleEntriesAsync(TimeSpan.FromHours(24)), 
            null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    public async Task StoreProcessedFileAsync(MultiLevelProcessingJob job, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (job == null) throw new ArgumentNullException(nameof(job));
        
        try
        {
            var fileInfo = new FileInfo(job.FilePath);
            var fileHash = await CalculateFileHashAsync(job.FilePath);
            
            var entry = new ProcessedFileEntry
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
            foreach (var (level, result) in job.Results)
            {
                var levelData = new ProcessedLevelData
                {
                    Content = result.Content ?? string.Empty,
                    ProcessedAt = result.ProcessedAt,
                    ProcessingDuration = result.ProcessingDuration,
                    IsSuccess = result.IsSuccess,
                    ErrorMessage = result.ErrorMessage
                };

                // Index the data for fast querying
                levelData.IndexedData = await IndexLevelDataAsync(level, result.Content ?? string.Empty);
                entry.LevelData[level] = levelData;
            }

            // Save to file without locking (file operations are already thread-safe)
            await SaveToCacheAsync(entry);
            
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
            return await LoadFromCacheAsync(filePath);
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
            var cacheKey = GenerateCacheKey(filePath);
            var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            if (!File.Exists(cacheFilePath))
                return false;
            
            var jsonContent = await File.ReadAllTextAsync(cacheFilePath, cancellationToken);
            var cachedEntry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
            
            if (cachedEntry == null)
                return false;
            
            return cachedEntry.Status == ProcessingStatus.Completed && 
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
            await RemoveFromCacheAsync(filePath);
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
        return await _fileQueryService.GetAllProcessedFilesAsync(cancellationToken);
    }

    public async Task<QueryResult<AstQueryResult>> GetAllAstNodesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _astQueryService.GetAllAstNodesAsync(cancellationToken);
    }

    public async Task<QueryResult<DomainResult>> GetAllDomainsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _domainQueryService.GetAllDomainsAsync(cancellationToken);
    }

    public async Task<QueryResult<ProcessedFileEntry>> QueryFilesAsync(FileQueryRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _fileQueryService.QueryFilesAsync(request, cancellationToken);
    }

    public async Task<QueryResult<AstQueryResult>> QueryAstAsync(AstQueryRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _astQueryService.QueryAstAsync(request, cancellationToken);
    }

    public async Task<QueryResult<DomainResult>> QueryDomainsAsync(DomainQueryRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _domainQueryService.QueryDomainsAsync(request, cancellationToken);
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            // Don't use locks for file operations - they are already thread-safe
            // Use Directory.GetFiles for faster statistics without loading all content
            var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
            var totalFiles = cacheFiles.Length;
            
            var stats = new CacheStatistics
            {
                TotalFiles = totalFiles,
                ProcessedFiles = 0, // Will be calculated if needed
                FailedFiles = 0,    // Will be calculated if needed
                TotalSizeBytes = 0  // Will be calculated if needed
            };

            if (totalFiles > 0)
            {
                // Calculate basic file statistics without deserializing
                var fileInfos = cacheFiles.Select(f => new FileInfo(f)).ToList();
                stats.TotalSizeBytes = fileInfos.Sum(f => f.Length);
                stats.OldestEntry = fileInfos.Min(f => f.LastWriteTimeUtc);
                stats.NewestEntry = fileInfos.Max(f => f.LastWriteTimeUtc);
                
                // Only load a sample of files for detailed statistics if there are many files
                if (totalFiles <= 100)
                {
                    // For small cache, load all files for detailed stats
                    var queryResult = await _fileQueryService.GetAllProcessedFilesAsync();
                    var entries = queryResult.Items;
                    stats.ProcessedFiles = entries.Count(e => e.Status == ProcessingStatus.Completed);
                    stats.FailedFiles = entries.Count(e => e.Status == ProcessingStatus.Failed);
                    
                    foreach (var level in Enum.GetValues<ProcessingLevel>())
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
                    foreach (var level in Enum.GetValues<ProcessingLevel>())
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
                LevelCounts = new Dictionary<ProcessingLevel, int>()
            };
        }
    }

    public async Task CleanupStaleEntriesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        var cutoffTime = DateTime.UtcNow - maxAge;
        var removedCount = 0;
        
        try
        {
            // Don't use locks for file operations - they are already thread-safe
            var queryResult = await _fileQueryService.GetAllProcessedFilesAsync();
            var allFiles = queryResult.Items;
            var staleEntries = allFiles
                .Where(e => e.ProcessedAt < cutoffTime)
                .ToList();

            foreach (var entry in staleEntries)
            {
                await RemoveFromCacheAsync(entry.FilePath);
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
            var cacheKey = GenerateCacheKey(entry.FilePath);
            var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            var jsonContent = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(cacheFilePath, jsonContent);
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
            var cacheKey = GenerateCacheKey(filePath);
            var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            if (!File.Exists(cacheFilePath))
                return null;

            var jsonContent = await File.ReadAllTextAsync(cacheFilePath);
            var entry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
            
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
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath));
        return Convert.ToHexString(hashBytes).ToLower();
    }



    private Task RemoveFromCacheAsync(string filePath)
    {
        try
        {
            var cacheKey = GenerateCacheKey(filePath);
            var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache entry for {FilePath}", filePath);
        }
        
        return Task.CompletedTask;
    }



    private async Task<Dictionary<string, object>> IndexLevelDataAsync(ProcessingLevel level, string content)
    {
        var indexedData = new Dictionary<string, object>();

        try
        {
            switch (level)
            {
                case ProcessingLevel.Ast:
                    await IndexAstDataAsync(content, indexedData);
                    break;
                case ProcessingLevel.DomainKeywords:
                    await IndexDomainDataAsync(content, indexedData);
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
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var astData = JsonSerializer.Deserialize<AstCompilationUnit>(astContent, options);
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
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var domainData = JsonSerializer.Deserialize<DomainKeywordsResponse>(domainContent, options);
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
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
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
            _cleanupTimer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing FileBasedCacheService");
        }
    }
}

// Supporting classes for indexing
public class DomainResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}