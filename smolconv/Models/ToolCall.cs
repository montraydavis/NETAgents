namespace SmolConv.Models
{
    // ===============================
    // TOOL MODELS
    // ===============================

    public record ToolCall
    {
        public string Name { get; init; }
        public object? Arguments { get; init; }
        public string Id { get; init; }

        public ToolCall(string name, object? arguments, string id)
        {
            Name = name;
            Arguments = arguments;
            Id = id;
        }
    }
}