using System.Text.Json;
using NETAgents.Tools.Processing.Cache;
using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Models.Ast;

namespace NETAgents.Tools.Processing.Services.Query;

public class AstQueryService : BaseQueryService
{
    public AstQueryService(ILogger<AstQueryService> logger, string cacheDirectory) 
        : base(logger, cacheDirectory)
    {
    }

    public async Task<QueryResult<AstQueryResult>> GetAllAstNodesAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteQueryAsync(
            async (ct) => await ExtractAllAstNodesAsync(ct),
            "GetAllAstNodes",
            cancellationToken
        );
    }

    public async Task<QueryResult<AstQueryResult>> QueryAstAsync(
        AstQueryRequest request, 
        CancellationToken cancellationToken = default)
    {
        return await ExecuteFilteredQueryAsync(
            async (ct) => await ExtractAllAstNodesAsync(ct),
            nodes => ApplyAstFilters(nodes, request),
            "QueryAst",
            cancellationToken: cancellationToken
        );
    }

    private static IEnumerable<AstQueryResult> ApplyAstFilters(
        IEnumerable<AstQueryResult> nodes, 
        AstQueryRequest request)
    {
        IEnumerable<AstQueryResult> query = nodes.AsEnumerable();
        
        if (!string.IsNullOrEmpty(request.NodeType))
            query = query.Where(n => n.Type?.Equals(request.NodeType, StringComparison.OrdinalIgnoreCase) == true);
        
        if (!string.IsNullOrEmpty(request.NodeName))
            query = query.Where(n => n.Name?.Equals(request.NodeName, StringComparison.OrdinalIgnoreCase) == true);
        
        if (!string.IsNullOrEmpty(request.Namespace))
            query = query.Where(n => n.Namespace?.Equals(request.Namespace, StringComparison.OrdinalIgnoreCase) == true);
        
        if (request.Modifiers?.Any() == true)
            query = query.Where(n => request.Modifiers.All(m => n.Modifiers?.Contains(m) == true));
        
        if (request.BaseTypes?.Any() == true)
            query = query.Where(n => request.BaseTypes.Any(bt => n.BaseTypes?.Contains(bt) == true));
        
        if (request.Attributes?.Any() == true)
            query = query.Where(n => request.Attributes.Any(attr => n.Attributes?.Contains(attr) == true));

        return query;
    }

    private async Task<List<AstQueryResult>> ExtractAllAstNodesAsync(CancellationToken cancellationToken)
    {
        List<AstQueryResult> allNodes = new List<AstQueryResult>();
        string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
        int processedFiles = 0;
        int skippedFiles = 0;
        int totalFiles = cacheFiles.Length;
        
        _logger.LogInformation("Processing {TotalFiles} cache files for AST extraction", totalFiles);
        
        foreach (string cacheFile in cacheFiles)
        {
            try
            {
                string jsonContent = await File.ReadAllTextAsync(cacheFile, cancellationToken);
                ProcessedFileEntry? entry = JsonSerializer.Deserialize<ProcessedFileEntry>(jsonContent);
                
                // Validate entry before processing
                if (entry == null)
                {
                    _logger.LogWarning("Skipping null entry in cache file: {CacheFile}", cacheFile);
                    skippedFiles++;
                    continue;
                }
                
                // Skip files that don't have AST data or failed processing
                if (!entry.LevelData.TryGetValue(JobProcessingLevel.Ast, out ProcessedLevelData? astData) || !astData.IsSuccess)
                {
                    skippedFiles++;
                    continue;
                }
                
                // Skip empty or invalid AST content
                if (string.IsNullOrWhiteSpace(astData.Content))
                {
                    _logger.LogWarning("Skipping file with empty AST content: {FilePath}", entry.FilePath);
                    skippedFiles++;
                    continue;
                }
                
                List<AstQueryResult> nodes = await ExtractNodesFromAstAsync(entry.FilePath, astData.Content);
                
                // Only add nodes if we actually found valid ones
                if (nodes.Any())
                {
                    allNodes.AddRange(nodes);
                    processedFiles++;
                    _logger.LogDebug("Extracted {NodeCount} AST nodes from {FilePath}", nodes.Count, entry.FilePath);
                }
                else
                {
                    _logger.LogDebug("No valid AST nodes found in {FilePath}", entry.FilePath);
                    skippedFiles++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cache file {CacheFile}", cacheFile);
                skippedFiles++;
            }
        }
        
        _logger.LogInformation("AST extraction complete: {ProcessedFiles} files processed, {SkippedFiles} files skipped, {TotalNodes} total nodes", 
            processedFiles, skippedFiles, allNodes.Count);
        
        return allNodes;
    }

    private async Task<List<AstQueryResult>> ExtractNodesFromAstAsync(string filePath, string astContent)
    {
        List<AstQueryResult> nodes = new List<AstQueryResult>();
        
        try
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            AstCompilationUnit? astData = JsonSerializer.Deserialize<AstCompilationUnit>(astContent, options);
            if (astData == null) return nodes;

            // Extract classes
            if (astData.Classes != null)
            {
                foreach (AstClass cls in astData.Classes)
                {
                    // Validate class data before adding
                    if (!string.IsNullOrWhiteSpace(cls.Name))
                    {
                        nodes.Add(new AstQueryResult
                        {
                            FilePath = filePath,
                            Type = "class",
                            Name = cls.Name,
                            Namespace = astData.Namespace,
                            Modifiers = cls.Modifiers?.ToList() ?? new List<string>(),
                            BaseTypes = cls.BaseTypes?.ToList() ?? new List<string>(),
                            Attributes = cls.Attributes?.ToList() ?? new List<string>()
                        });
                    }
                }
            }

            // Extract interfaces
            if (astData.Interfaces != null)
            {
                foreach (AstInterface iface in astData.Interfaces)
                {
                    // Validate interface data before adding
                    if (!string.IsNullOrWhiteSpace(iface.Name))
                    {
                        nodes.Add(new AstQueryResult
                        {
                            FilePath = filePath,
                            Type = "interface",
                            Name = iface.Name,
                            Namespace = astData.Namespace,
                            Modifiers = iface.Modifiers?.ToList() ?? new List<string>(),
                            BaseTypes = iface.BaseTypes?.ToList() ?? new List<string>(),
                            Attributes = iface.Attributes?.ToList() ?? new List<string>()
                        });
                    }
                }
            }

            // Extract enums
            if (astData.Enums != null)
            {
                foreach (AstEnum enumType in astData.Enums)
                {
                    // Validate enum data before adding
                    if (!string.IsNullOrWhiteSpace(enumType.Name))
                    {
                        nodes.Add(new AstQueryResult
                        {
                            FilePath = filePath,
                            Type = "enum",
                            Name = enumType.Name,
                            Namespace = astData.Namespace,
                            Modifiers = enumType.Modifiers?.ToList() ?? new List<string>(),
                            BaseTypes = new List<string>(), // Enums don't have base types
                            Attributes = enumType.Attributes?.ToList() ?? new List<string>()
                        });
                    }
                }
            }

            // Extract records
            if (astData.Records != null)
            {
                foreach (AstRecord record in astData.Records)
                {
                    // Validate record data before adding
                    if (!string.IsNullOrWhiteSpace(record.Name))
                    {
                        nodes.Add(new AstQueryResult
                        {
                            FilePath = filePath,
                            Type = "record",
                            Name = record.Name,
                            Namespace = astData.Namespace,
                            Modifiers = record.Modifiers?.ToList() ?? new List<string>(),
                            BaseTypes = record.BaseTypes?.ToList() ?? new List<string>(),
                            Attributes = record.Attributes?.ToList() ?? new List<string>()
                        });
                    }
                }
            }

            // Extract structs
            if (astData.Structs != null)
            {
                foreach (AstStruct structType in astData.Structs)
                {
                    // Validate struct data before adding
                    if (!string.IsNullOrWhiteSpace(structType.Name))
                    {
                        nodes.Add(new AstQueryResult
                        {
                            FilePath = filePath,
                            Type = "struct",
                            Name = structType.Name,
                            Namespace = astData.Namespace,
                            Modifiers = structType.Modifiers?.ToList() ?? new List<string>(),
                            BaseTypes = structType.BaseTypes?.ToList() ?? new List<string>(),
                            Attributes = structType.Attributes?.ToList() ?? new List<string>()
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract nodes from AST for {FilePath}", filePath);
        }

        await Task.CompletedTask;
        return nodes;
    }
}