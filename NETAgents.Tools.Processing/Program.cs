using NETAgents.Tools.Processing;
using NETAgents.Tools.Processing.Services;
using Microsoft.Extensions.Options;



var builder = Host.CreateApplicationBuilder(args);

// Configure processing options
builder.Services.Configure<ProcessingOptions>(
    builder.Configuration.GetSection(ProcessingOptions.SectionName));

// Register processing options as singleton
builder.Services.AddSingleton(sp => 
    sp.GetRequiredService<IOptions<ProcessingOptions>>().Value);

// Register services
builder.Services.AddSingleton<IFileProcessorService, FileProcessorService>();
builder.Services.AddSingleton<IProcessingQueueService, ProcessingQueueService>();
builder.Services.AddSingleton<IFileDiscoveryService, FileDiscoveryService>();

// Register the worker service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
