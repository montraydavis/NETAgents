namespace NETAgents.Tools.Processing.Models;

public enum ProcessingLevel
{
    Ast = 1,
    DomainKeywords = 2
}

public class MultiLevelProcessingJob : FileProcessingJob
{
    public List<ProcessingLevel> RequiredLevels { get; set; } = new();
    public Dictionary<ProcessingLevel, ProcessingResult> Results { get; set; } = new();
    public ProcessingLevel CurrentLevel { get; set; } = ProcessingLevel.Ast;
    public bool IsMultiLevelComplete => RequiredLevels.All(level => Results.ContainsKey(level) && Results[level].IsSuccess);
}

public class ProcessingResult
{
    public bool IsSuccess { get; set; }
    public string? Content { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan ProcessingDuration { get; set; }
}

public class DomainKeyword
{
    public string Name { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

public class DomainKeywordsResponse
{
    public List<DomainKeyword> Domains { get; set; } = new();
}