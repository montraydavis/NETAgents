namespace SmolConv.Models
{
    // ===============================
    // AGENT OUTPUT MODELS
    // ===============================

    public record ActionOutput
    {
        public object? Output { get; init; }
        public AgentType? TypedOutput { get; init; }
        public bool IsFinalAnswer { get; init; }
        public string? OutputType { get; init; }

        public ActionOutput(object? output, bool isFinalAnswer, string? outputType = null)
        {
            Output = output;
            IsFinalAnswer = isFinalAnswer;
            OutputType = outputType;
            
            // Automatically convert to typed output if possible
            TypedOutput = AgentTypeMapping.HandleAgentOutputTypes(output, outputType);
        }

        public ActionOutput(AgentType typedOutput, bool isFinalAnswer)
        {
            Output = typedOutput.ToRaw();
            TypedOutput = typedOutput;
            IsFinalAnswer = isFinalAnswer;
            OutputType = typedOutput.GetType().Name.ToLowerInvariant().Replace("agent", "");
        }

        // Helper method to get the raw value
        public object? GetRawOutput() => TypedOutput?.ToRaw() ?? Output;
        
        // Helper method to get the string representation
        public string GetStringOutput() => TypedOutput?.ToString() ?? Output?.ToString() ?? string.Empty;
    }
}