namespace NETAgents.Models.Processing
{
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
    
        // Collection management options
        public int MaxCompletedJobs { get; set; } = 1000;
        public int MaxActiveJobs { get; set; } = 100;
        public int MaxProcessedFiles { get; set; } = 5000;
        public TimeSpan CollectionCleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
    
        // Cache management options
        public long MaxCacheSizeBytes { get; set; } = 500 * 1024 * 1024; // 500MB default
        public TimeSpan CacheEvictionInterval { get; set; } = TimeSpan.FromMinutes(30);
        public int MaxCacheEntries { get; set; } = 10000;
    }
}
