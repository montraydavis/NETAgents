namespace NETAgents.Core
{
    // ===============================
    // TOOL IMPLEMENTATIONS
    // ===============================

    /// <summary>
    /// Validation result for tool inputs
    /// </summary>
    public record ToolValidationResult
    {
        public bool IsValid { get; init; }
        public List<string> Errors { get; init; } = new();
        public static ToolValidationResult Success => new() { IsValid = true };
        public static ToolValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors.ToList() };
    }
}