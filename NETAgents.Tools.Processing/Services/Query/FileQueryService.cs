using System.Text.Json;
using System.Text.RegularExpressions;
using NETAgents.Tools.Processing.Cache;

namespace NETAgents.Tools.Processing.Services.Query;

public class FileQueryService : BaseQueryService
{
    public FileQueryService(ILogger<FileQueryService> logger, string cacheDirectory) 
        : base(logger, cacheDirectory)
    {
    }

    public async Task<QueryResult<ProcessedFileEntry>> GetAllProcessedFilesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteQueryAsync(
            async (ct) => await GetAllCachedEntriesAsync(ct),
            "GetAllProcessedFiles",
            cancellationToken
        );
    }

    public async Task<QueryResult<ProcessedFileEntry>> QueryFilesAsync(
        FileQueryRequest request, 
        CancellationToken cancellationToken = default)
    {
        return await ExecuteFilteredQueryAsync(
            async (ct) => await GetAllCachedEntriesAsync(ct),
            files => ApplyFileFilters(files, request),
            "FileQuery",
            cancellationToken: cancellationToken
        );
    }

    private static IEnumerable<ProcessedFileEntry> ApplyFileFilters(
        IEnumerable<ProcessedFileEntry> files, 
        FileQueryRequest request)
    {
        IEnumerable<ProcessedFileEntry> query = files.AsEnumerable();

        if (!string.IsNullOrEmpty(request.FilePath))
            query = query.Where(f => f.FilePath == request.FilePath);
        
        if (!string.IsNullOrEmpty(request.FilePattern))
        {
            Regex regex = new Regex(request.FilePattern.Replace("*", ".*"), RegexOptions.IgnoreCase);
            query = query.Where(f => regex.IsMatch(f.FilePath));
        }
        
        if (request.Level.HasValue)
            query = query.Where(f => f.LevelData.ContainsKey(request.Level.Value));
        
        if (request.ProcessedAfter.HasValue)
            query = query.Where(f => f.ProcessedAt >= request.ProcessedAfter.Value);
        
        if (request.ProcessedBefore.HasValue)
            query = query.Where(f => f.ProcessedAt <= request.ProcessedBefore.Value);
        
        if (request.Status.HasValue)
            query = query.Where(f => f.Status == request.Status.Value);

        return query;
    }

    private async Task<List<ProcessedFileEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken)
    {
        List<ProcessedFileEntry> entries = new List<ProcessedFileEntry>();
        
        try
        {
            string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
            _logger.LogDebug("Found {Count} cache files in directory: {CacheDirectory}", cacheFiles.Length, _cacheDirectory);
            
            foreach (string cacheFile in cacheFiles)
            {
                try
                {
                    string jsonContent = await File.ReadAllTextAsync(cacheFile, cancellationToken);
                    ProcessedFileEntry? entry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
                    if (entry != null)
                    {
                        entries.Add(entry);
                        _logger.LogDebug("Loaded cache entry for file: {FilePath}", entry.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load cache file {CacheFile}", cacheFile);
                }
            }
            
            _logger.LogDebug("Successfully loaded {Count} cache entries", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache entries from directory: {CacheDirectory}", _cacheDirectory);
        }
        
        return entries;
    }
}
