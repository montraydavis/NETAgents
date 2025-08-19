namespace NETAgents.Tools.Processing;

public class ProcessingOptions
{
    public const string SectionName = "Processing";
    
    public string InputDirectory { get; set; } = string.Empty;
    public string FilePattern { get; set; } = "*.md";
    public int MaxConcurrentProcessing { get; set; } = 3;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan DequeueTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableFileWatcher { get; set; } = true;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}
