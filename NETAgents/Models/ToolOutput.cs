namespace NETAgents.Models
{
    public record ToolOutput
    {
        public string Id { get; init; }
        public object? Output { get; init; }
        public bool IsFinalAnswer { get; init; }
        public string Observation { get; init; }
        public ToolCall ToolCall { get; init; }

        public ToolOutput(string id, object? output, bool isFinalAnswer, string observation, ToolCall toolCall)
        {
            Id = id;
            Output = output;
            IsFinalAnswer = isFinalAnswer;
            Observation = observation;
            ToolCall = toolCall;
        }
    }
}