namespace MCPCSharpRelevancy.Models.Analysis
{
    /// <summary>
    /// Result of solution validation
    /// </summary>
    public class SolutionValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = [];
        public List<string> Warnings { get; set; } = [];

        public override string ToString()
        {
            string result = $"Valid: {this.IsValid}";
            if (this.Errors.Count != 0)
            {
                result += $"\nErrors: {string.Join(", ", this.Errors)}";
            }

            if (this.Warnings.Count != 0)
            {
                result += $"\nWarnings: {string.Join(", ", this.Warnings)}";
            }

            return result;
        }
    }
}