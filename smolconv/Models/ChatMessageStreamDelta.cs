namespace SmolConv.Models
{
    public record ChatMessageStreamDelta
    {
        public string? Content { get; init; }
        public List<ChatMessageToolCallStreamDelta>? ToolCalls { get; init; }
        public TokenUsage? TokenUsage { get; init; }

        public ChatMessageStreamDelta(string? content = null, List<ChatMessageToolCallStreamDelta>? toolCalls = null,
                                    TokenUsage? tokenUsage = null)
        {
            Content = content;
            ToolCalls = toolCalls;
            TokenUsage = tokenUsage;
        }
    }
}