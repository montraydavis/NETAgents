namespace NETAgents.Models
{
    public record ChatMessageToolCall
    {
        public ChatMessageToolCallFunction Function { get; init; }
        public string Id { get; init; }
        public string Type { get; init; }

        public ChatMessageToolCall(ChatMessageToolCallFunction function, string id, string type)
        {
            Function = function;
            Id = id;
            Type = type;
        }
    }
}