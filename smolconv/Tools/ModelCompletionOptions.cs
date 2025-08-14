namespace SmolConv.Tools
{
    // ===============================
    // MODEL ABSTRACTION
    // ===============================

    /// <summary>
    /// Configuration for model completion requests
    /// </summary>
    public record ModelCompletionOptions
    {
        /// <summary>
        /// Stop sequences to halt generation
        /// </summary>
        public List<string>? StopSequences { get; init; }

        /// <summary>
        /// Response format specification
        /// </summary>
        public Dictionary<string, object>? ResponseFormat { get; init; }

        /// <summary>
        /// Tools available for the model to call
        /// </summary>
        public List<BaseTool>? ToolsToCallFrom { get; init; }

        /// <summary>
        /// Custom role conversions
        /// </summary>
        public Dictionary<string, string>? CustomRoleConversions { get; init; }

        /// <summary>
        /// Whether to convert images to URLs
        /// </summary>
        public bool ConvertImagesToImageUrls { get; init; }

        /// <summary>
        /// Tool choice preference
        /// </summary>
        public object? ToolChoice { get; init; }

        /// <summary>
        /// Additional parameters
        /// </summary>
        public Dictionary<string, object>? AdditionalParameters { get; init; }
    }
}