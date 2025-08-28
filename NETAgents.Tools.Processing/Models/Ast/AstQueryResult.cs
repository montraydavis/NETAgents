namespace NETAgents.Tools.Processing.Models.Ast
{
    // Query result models (for cache service)
    public class AstQueryResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Namespace { get; set; }
        public List<string> Modifiers { get; set; } = new();
        public List<string> BaseTypes { get; set; } = new();
        public List<string> Attributes { get; set; } = new();
    }
}
