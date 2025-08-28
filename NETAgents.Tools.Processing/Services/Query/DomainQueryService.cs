using System.Text.Json;
using System.Text.RegularExpressions;
using NETAgents.Tools.Processing.Cache;
using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services.Query
{
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
            IEnumerable<DomainResult> query = domains.AsEnumerable();
        
            if (!string.IsNullOrEmpty(request.DomainName))
                query = query.Where(d => d.Name.Equals(request.DomainName, StringComparison.OrdinalIgnoreCase));
        
            if (!string.IsNullOrEmpty(request.DomainPattern))
            {
                Regex regex = new Regex(request.DomainPattern, RegexOptions.IgnoreCase);
                query = query.Where(d => regex.IsMatch(d.Name));
            }

            return query;
        }

        private async Task<List<DomainResult>> ExtractAllDomainsAsync(CancellationToken cancellationToken)
        {
            List<DomainResult> allDomains = new List<DomainResult>();
            string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
            int processedFiles = 0;
        
            foreach (string cacheFile in cacheFiles) // Removed .Take(100) limit to process ALL files
            {
                try
                {
                    string jsonContent = await File.ReadAllTextAsync(cacheFile, cancellationToken);
                    ProcessedFileEntry? entry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
                
                    if (entry?.LevelData.TryGetValue(JobProcessingLevel.DomainKeywords, out ProcessedLevelData? domainData) == true && domainData.IsSuccess)
                    {
                        List<DomainResult> domains = await ExtractDomainsAsync(entry.FilePath, domainData.Content);
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
            List<DomainResult> domains = new List<DomainResult>();
        
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
            
                DomainKeywordsResponse? domainData = JsonSerializer.Deserialize<DomainKeywordsResponse>(domainContent, options);
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
}