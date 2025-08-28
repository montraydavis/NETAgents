namespace NETAgents.Tools.Processing.Models
{
    public class MultiLevelProcessingJob : FileProcessingJob
    {
        public List<JobProcessingLevel> RequiredLevels { get; set; } = new();
        public Dictionary<JobProcessingLevel, JobProcessingResult> Results { get; set; } = new();
        public JobProcessingLevel CurrentLevel { get; set; } = JobProcessingLevel.Ast;
        public bool IsMultiLevelComplete => RequiredLevels.All(level => Results.ContainsKey(level) && Results[level].IsSuccess);
    }
}
