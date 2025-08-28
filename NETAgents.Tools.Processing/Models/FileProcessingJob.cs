namespace NETAgents.Tools.Processing.Models
{
    public class FileProcessingJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public JobProcessingStatus Status { get; set; } = JobProcessingStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int RetryCount { get; set; } = 0;
        public string? ErrorMessage { get; set; }
        public string? Result { get; set; }
        public TimeSpan? ProcessingDuration { get; set; }
    }
}