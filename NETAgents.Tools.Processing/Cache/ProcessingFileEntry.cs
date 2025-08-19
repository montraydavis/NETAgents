using System.Text.RegularExpressions;
using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Models.Ast;

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
    public JobProcessingStatus Status { get; set; }
    public Dictionary<JobProcessingLevel, ProcessedLevelData> LevelData { get; set; } = new();
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
    public JobProcessingLevel? Level { get; set; }
    public DateTime? ProcessedAfter { get; set; }
    public DateTime? ProcessedBefore { get; set; }
    public JobProcessingStatus? Status { get; set; }
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






public interface ICacheService : IDisposable
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
    public Dictionary<JobProcessingLevel, int> LevelCounts { get; set; } = new();
}

// Supporting classes for indexing
public class DomainResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}