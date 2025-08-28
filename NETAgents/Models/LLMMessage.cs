namespace NETAgents.Models
{
    public record LLMMessage
    {
        public MessageRole Role { get; init; }
        public object? Content { get; init; } // Can be string or List<Dictionary<string, object>>
        public string? ContentString { get; init; } // Can be string or List<Dictionary<string, object>>
        public List<ChatMessageToolCall>? ToolCalls { get; init; }
        public object? Raw { get; init; }
        public TokenUsage? TokenUsage { get; init; }

        public LLMMessage(MessageRole role, object? content = null, string? contentString = null, List<ChatMessageToolCall>? toolCalls = null,
                          object? raw = null, TokenUsage? tokenUsage = null)
        {
            Role = role;
            Content = content;
            ContentString = contentString;
            ToolCalls = toolCalls;
            Raw = raw;
            TokenUsage = tokenUsage;
        }
    }
}