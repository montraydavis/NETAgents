namespace SmolConv.Models
{
    // ===============================
    // TOKEN USAGE AND TIMING
    // ===============================

    public record TokenUsage
    {
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public int TotalTokens => InputTokens + OutputTokens;

        public TokenUsage(int inputTokens, int outputTokens)
        {
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
        }
    }
}