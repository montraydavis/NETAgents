namespace NETAgents.Models
{
    public record ChatMessageToolCallStreamDelta
    {
        public int? Index { get; init; }
        public string? Id { get; init; }
        public string? Type { get; init; }
        public ChatMessageToolCallFunction? Function { get; init; }

        public ChatMessageToolCallStreamDelta(int? index = null, string? id = null,
                                            string? type = null, ChatMessageToolCallFunction? function = null)
        {
            Index = index;
            Id = id;
            Type = type;
            Function = function;
        }
    }
}