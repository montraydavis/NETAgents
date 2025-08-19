using System.Diagnostics;
using NETAgents.Tools.Processing.Cache;

namespace NETAgents.Tools.Processing.Services.Query;

public abstract class BaseQueryService
{
    protected readonly ILogger _logger;
    protected readonly string _cacheDirectory;

    protected BaseQueryService(ILogger logger, string cacheDirectory)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory;
    }

    protected async Task<QueryResult<T>> ExecuteQueryAsync<T>(
        Func<CancellationToken, Task<List<T>>> dataProvider,
        string queryType,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var items = await dataProvider(cancellationToken);
            stopwatch.Stop();

            return CreateSuccessResult(items, queryType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {QueryType}", queryType);
            stopwatch.Stop();
            
            return CreateErrorResult<T>(queryType, ex.Message, stopwatch.Elapsed);
        }
    }

    protected async Task<QueryResult<T>> ExecuteFilteredQueryAsync<T>(
        Func<CancellationToken, Task<List<T>>> dataProvider,
        Func<IEnumerable<T>, IEnumerable<T>> filter,
        string queryType,
        Dictionary<string, object>? additionalMetadata = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var allItems = await dataProvider(cancellationToken);
            var filteredItems = filter(allItems).ToList();
            stopwatch.Stop();

            var metadata = new Dictionary<string, object>
            {
                ["QueryType"] = queryType,
                ["FilteredFromTotal"] = allItems.Count,
                ["FilteredCount"] = filteredItems.Count
            };

            if (additionalMetadata != null)
            {
                foreach (var kvp in additionalMetadata)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            return new QueryResult<T>
            {
                Items = filteredItems,
                TotalCount = filteredItems.Count,
                QueryDuration = stopwatch.Elapsed,
                Metadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute filtered {QueryType}", queryType);
            stopwatch.Stop();
            
            return CreateErrorResult<T>(queryType, ex.Message, stopwatch.Elapsed);
        }
    }

    private static QueryResult<T> CreateSuccessResult<T>(
        List<T> items, 
        string queryType, 
        TimeSpan duration,
        Dictionary<string, object>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["QueryType"] = queryType
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return new QueryResult<T>
        {
            Items = items,
            TotalCount = items.Count,
            QueryDuration = duration,
            Metadata = metadata
        };
    }

    private static QueryResult<T> CreateErrorResult<T>(
        string queryType, 
        string errorMessage, 
        TimeSpan duration)
    {
        return new QueryResult<T>
        {
            Items = new List<T>(),
            TotalCount = 0,
            QueryDuration = duration,
            Metadata = new Dictionary<string, object>
            {
                ["QueryType"] = queryType,
                ["Error"] = errorMessage
            }
        };
    }
}
