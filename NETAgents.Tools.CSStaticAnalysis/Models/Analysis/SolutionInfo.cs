namespace MCPCSharpRelevancy.Models.Analysis
{
    /// <summary>
    /// Information about a solution
    /// </summary>
    public class SolutionAnalysis
    {
        public string FilePath { get; set; } = string.Empty;
        public int ProjectCount { get; set; }
        public int CSharpProjectCount { get; set; }
        public int TotalDocuments { get; set; }
        public List<string> ProjectNames { get; set; } = [];

        public override string ToString()
        {
            return $"""
                Solution: {Path.GetFileName(this.FilePath)}
                Total Projects: {this.ProjectCount}
                C# Projects: {this.CSharpProjectCount}
                Total Documents: {this.TotalDocuments}
                Projects: {string.Join(", ", this.ProjectNames)}
                """;
        }
    }
}