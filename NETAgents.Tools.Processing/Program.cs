using NETAgents.Tools.Processing;
using NETAgents.Tools.Processing.Services;
using Microsoft.Extensions.Options;
using NETAgents.Tools.Processing.Cache;
using NETAgents.Tools.Processing.Models.Ast;
using NETAgents.Models.Processing;
using MCPCSharpRelevancy.Services.Analysis;
using Microsoft.CodeAnalysis;
using MCPCSharpRelevancy.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using System.Runtime.Intrinsics.Arm;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure processing options
builder.Services.Configure<ProcessingOptions>(
    builder.Configuration.GetSection(ProcessingOptions.SectionName));

// Register processing options as singleton
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IOptions<ProcessingOptions>>().Value);

// Register services in dependency order
// NOTE: Service disposal happens in REVERSE registration order
// This ensures proper cleanup sequence during application shutdown:
//
// 1. FileBasedCacheService (registers first, disposes last)
//    - Maintains data integrity until all other services are done
//    - Contains file I/O operations and cleanup timers
//
// 2. MultiLevelFileProcessorService 
//    - AI model resources that need proper disposal
//    - No dependencies on other services for disposal
//
// 3. ProcessingQueueService
//    - Manages job queues and cleanup timers
//    - Depends on cache service for storing results
//
// 4. FileDiscoveryService
//    - File system watchers and event handlers
//    - Depends on queue service for job enqueuing
//
// 5. CachedMultiLevelWorker (registers last, disposes first)
//    - Orchestrates all other services
//    - Implements proper shutdown sequence in its disposal

builder.Services.AddSingleton<ICacheService, FileBasedCacheService>();
builder.Services.AddSingleton<IMultiLevelFileProcessorService, MultiLevelFileProcessorService>();
builder.Services.AddSingleton<IProcessingQueueService, ProcessingQueueService>();
builder.Services.AddSingleton<IFileDiscoveryService, FileDiscoveryService>();

// Register the worker service last (disposes first)
builder.Services.AddHostedService<CachedMultiLevelWorker>();

IHost host = builder.Build();

SolutionAnalyzer solutionAnalyzer = new SolutionAnalyzer();
Solution solution = await solutionAnalyzer.LoadSolutionAsync("/Users/montraydavis/NETAgents/NETAgents.sln");
SourceDependencyGraph analysis = await solutionAnalyzer.AnalyzeSolutionAsync(solution, false);

Directory.CreateDirectory("./.tmp");

// Process each node and generate markdown documentation
foreach (SourceTypeNode node in analysis.Nodes.Values)
{
    // Filter out self-references
    SourceTypeDependency[] dependencies = analysis.GetDependenciesOf(node.FullName)
        .Where(dep => dep.SourceType.TypeKind == TypeKind.Class)
        .ToArray();

    // var gg = dependencies.First().SourceType.ToDisplayString();

    SourceTypeDependency[] dependents = analysis.GetDependentsOf(node.FullName)
        .Where(dep => dep.TargetType.TypeKind == TypeKind.Class)
        .ToArray();

    var distinctDependencies = dependencies.Select(dep => dep.TargetType.ToDisplayString())
        .Distinct()
        .ToArray();

    var distinctDependents = dependents.Select(dep => dep.SourceType.ToDisplayString())
        .Distinct()
        .ToArray();

    var cnt = distinctDependencies.Count() + distinctDependents.Count();


    // Prepare data for template
    var templateData = new
    {
        NodeName = node.FullName,
        SourceCode = node.SourceCode,
        Dependencies = dependencies.Select(dep => new
        {
            TypeName = dep.SourceType.ToDisplayString(),
            SourceCode = analysis.GetNode(dep.SourceType.ToDisplayString())?.SourceCode ?? "Source code not available",
            Namespace = string.Join(".", (dep.FullName ?? "").Split(".").SkipLast(1)),
            DependencyType = dep.DependencyType,
            Member = dep.FullName ?? "Unknown"
        }).ToArray(),
        Dependents = dependents.GroupBy(dep => dep.TargetType.ToDisplayString())
            .Select(group => new
            {
                TypeName = group.First().TargetType.ToDisplayString(),
                SourceCode = analysis.GetNode(group.First().TargetType.ToDisplayString())?.SourceCode ?? "Source code not available",
                Namespace = string.Join(".", (group.First().FullName ?? "").Split(".").SkipLast(1)),
                DependencyTypes = string.Join(", ", group.Select(d => d.DependencyType)),
                Members = string.Join(", ", group.Select(d => d.FullName ?? "Unknown"))
            }).ToArray()
    };

    // Format C# code using Roslyn
    string FormatCSharpCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return code;

        try
        {
            // Parse the code into a syntax tree
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);
            SyntaxNode root = syntaxTree.GetRoot();

            // Format the code using Roslyn's formatter
            AdhocWorkspace workspace = new AdhocWorkspace();
            SyntaxNode formattedRoot = Formatter.Format(root, workspace);

            return formattedRoot.ToFullString();
        }
        catch (Exception)
        {
            // Fallback to original code if formatting fails
            return code;
        }
    }

    // Format markdown for consistent output
    string FormatMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return markdown;

        try
        {
            // Normalize line endings
            string normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

            // Remove excessive blank lines (more than 2 consecutive)
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\n{3,}", "\n\n");

            // Trim and ensure proper ending
            return normalized.Trim() + "\n";
        }
        catch (Exception)
        {
            // Fallback to original markdown if formatting fails
            return markdown;
        }
    }

    string markdown = $"""
                      ---
                      node:
                        fullName: "{node.FullName}"
                        filePath: "{node.FilePath}"
                        kind: "{node.Symbol.TypeKind}"
                        namespace: "{node.Namespace}"
                        name: "{node.Name}"
                        dependencyCount: {node.Dependencies.Count}
                        dependentCount: {node.Dependents.Count}
                      ---

                      <!-- Source Code Path: {node.FilePath} -->

                      # {templateData.NodeName}

                      ## Source Code

                      ```csharp
                      namespace {string.Join(".", templateData.NodeName.Split(".").SkipLast(1))};

                      {FormatCSharpCode(templateData.SourceCode)}
                      ```

                      {(templateData.Dependencies.Any() ?
                          $"""
                          ## Dependencies (Dependent On)

                          {string.Join("\n\n", distinctDependencies.Select(dep => dep))}

                          """ : "")}{(templateData.Dependents.Any() ?
                          $"""

                          ## Dependents (Dependent By)

                          {string.Join("\n\n", distinctDependents.Select(dep => dep))}

                          """ : "")}
                        ---
                      """;

    // Format the final markdown
    string formattedMarkdown = FormatMarkdown(markdown);

    // Write to file
    string fileName = $"./.tmp/{node.FullName.Replace("<", "").Replace(">", "").Replace(" ", "_")}_dependencies.md";
    await File.WriteAllTextAsync(fileName, formattedMarkdown);

    Console.WriteLine($"Generated documentation for {node.FullName} -> {fileName}");
}


// Start the application
await host.StartAsync();

// Wait a bit for initial processing to complete
Console.WriteLine("Application started. Waiting for initial file processing...");

// Now check the cache after some processing has occurred
ICacheService cacheService = host.Services.GetRequiredService<ICacheService>();
CacheStatistics stats = await cacheService.GetStatisticsAsync();

QueryResult<AstQueryResult> asts = await cacheService.GetAllAstNodesAsync();
QueryResult<DomainResult> domains = await cacheService.GetAllDomainsAsync();

AstQueryResult[] aa = asts.Items.ToArray();
DomainResult[] dd = domains.Items.ToArray();

Console.WriteLine($"Cache Statistics:");
Console.WriteLine($"  Total Files: {stats.TotalFiles}");
Console.WriteLine($"  Processed Files: {stats.ProcessedFiles}");
Console.WriteLine($"  Failed Files: {stats.FailedFiles}");

if (stats.ProcessedFiles > 0)
{
    Console.WriteLine("\nAccessing cached AST and Domain data...");

    Console.WriteLine($"AST Nodes: {asts.TotalCount}");
    Console.WriteLine($"Domains: {domains.TotalCount}");

    if (asts.Items.Any())
    {
        Console.WriteLine("\nSample AST Nodes:");
        foreach (AstQueryResult ast in asts.Items.Take(3))
        {
            Console.WriteLine($"  - {ast.Name} ({ast.Type}) in {ast.FilePath}");
        }
    }

    if (domains.Items.Any())
    {
        Console.WriteLine("\nSample Domains:");
        foreach (DomainResult domain in domains.Items.Take(3))
        {
            Console.WriteLine($"  - {domain.Name} ({domain.Reasoning})");
        }
    }
}
else
{
    Console.WriteLine("No files have been processed yet. The cache is empty.");
    Console.WriteLine("Check your file discovery paths and ensure files are being found.");
}

// Keep the application running with proper shutdown handling
try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Application terminated with error: {ex.Message}");
    throw;
}
finally
{
    Console.WriteLine("Application is shutting down. Services will be disposed in proper order.");

    // The host will automatically dispose services in reverse registration order:
    // 1. CachedMultiLevelWorker (stops workers, cancels tasks)
    // 2. FileDiscoveryService (stops file watchers)
    // 3. ProcessingQueueService (completes channels, disposes timers)
    // 4. MultiLevelFileProcessorService (disposes AI model)
    // 5. FileBasedCacheService (final cleanup, disposes timers)
}