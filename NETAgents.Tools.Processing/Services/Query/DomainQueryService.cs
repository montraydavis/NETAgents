using System.Text.Json;
using System.Text.RegularExpressions;
using NETAgents.Tools.Processing.Cache;
using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services.Query;

public class DomainQueryService : BaseQueryService
{
    public DomainQueryService(ILogger<DomainQueryService> logger, string cacheDirectory) 
        : base(logger, cacheDirectory)
    {
    }

    public async Task<QueryResult<DomainResult>> GetAllDomainsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteQueryAsync(
            async (ct) => await ExtractAllDomainsAsync(ct),
            "GetAllDomains",
            cancellationToken
        );
    }

    public async Task<QueryResult<DomainResult>> QueryDomainsAsync(
        DomainQueryRequest request, 
        CancellationToken cancellationToken = default)
    {
        return await ExecuteFilteredQueryAsync(
            async (ct) => await ExtractAllDomainsAsync(ct),
            domains => ApplyDomainFilters(domains, request),
            "DomainQuery",
            cancellationToken: cancellationToken
        );
    }

    private static IEnumerable<DomainResult> ApplyDomainFilters(
        IEnumerable<DomainResult> domains, 
        DomainQueryRequest request)
    {
        var query = domains.AsEnumerable();
        
        if (!string.IsNullOrEmpty(request.DomainName))
            query = query.Where(d => d.Name.Equals(request.DomainName, StringComparison.OrdinalIgnoreCase));
        
        if (!string.IsNullOrEmpty(request.DomainPattern))
        {
            var regex = new Regex(request.DomainPattern, RegexOptions.IgnoreCase);
            query = query.Where(d => regex.IsMatch(d.Name));
        }

        return query;
    }

    private async Task<List<DomainResult>> ExtractAllDomainsAsync(CancellationToken cancellationToken)
    {
        var allDomains = new List<DomainResult>();
        var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
        var processedFiles = 0;
        
        foreach (var cacheFile in cacheFiles) // Removed .Take(100) limit to process ALL files
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(cacheFile, cancellationToken);
                var entry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
                
                if (entry?.LevelData.TryGetValue(ProcessingLevel.DomainKeywords, out var domainData) == true && domainData.IsSuccess)
                {
                    var domains = await ExtractDomainsAsync(entry.FilePath, domainData.Content);
                    allDomains.AddRange(domains);
                    processedFiles++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cache file {CacheFile}", cacheFile);
            }
        }
        
        return allDomains;
    }

    private async Task<List<DomainResult>> ExtractDomainsAsync(string filePath, string domainContent)
    {
        var domains = new List<DomainResult>();
        
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            var domainData = JsonSerializer.Deserialize<DomainKeywordsResponse>(domainContent, options);
            if (domainData?.Domains != null)
            {
                domains.AddRange(domainData.Domains.Select(d => new DomainResult
                {
                    FilePath = filePath,
                    Name = d.Name,
                    Reasoning = d.Reasoning
                }));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract domains for {FilePath}", filePath);
        }

        await Task.CompletedTask;
        return domains;
    }
}