namespace SmolConv.Models
{
    // ===============================
    // CHAT MESSAGE MODELS
    // ===============================

    public record ChatMessageToolCallFunction
    {
        public object Arguments { get; init; }
        public string Name { get; init; }
        public string? Description { get; init; }

        public ChatMessageToolCallFunction(string name, object arguments, string? description = null)
        {
            Name = name;
            Arguments = arguments;
            Description = description;
        }
    }
}