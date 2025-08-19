using NETAgents.Tools.Processing;
using NETAgents.Tools.Processing.Services;
using Microsoft.Extensions.Options;
using NETAgents.Tools.Processing.Cache;
using NETAgents.Tools.Processing.Models.Ast;
using NETAgents.Models.Processing;

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