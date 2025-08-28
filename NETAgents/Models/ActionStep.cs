namespace NETAgents.Models
{

    public record ActionStep : MemoryStep
    {
        public int StepNumber { get; init; }
        public List<LLMMessage>? ModelInputMessages { get; init; }
        public LLMMessage? ModelOutputMessage { get; init; }
        public string? ModelOutput { get; init; }
        public List<ToolCall>? ToolCalls { get; init; }
        public List<ToolOutput>? ToolResponses { get; init; } // Add tool responses
        public string? Observations { get; init; }
        public List<object>? ObservationsImages { get; init; }
        public string? CodeAction { get; init; }
        public object? ActionOutput { get; init; }
        public bool IsFinalAnswer { get; init; }
        public Exception? Error { get; init; }
        public TokenUsage? TokenUsage { get; init; }

        public ActionStep(int stepNumber, Timing? timing = null, List<object>? observationsImages = null,
                         Exception? error = null, TokenUsage? tokenUsage = null, bool isFinalAnswer = false) : base(timing)
        {
            StepNumber = stepNumber;
            ObservationsImages = observationsImages;
            Error = error;
            TokenUsage = tokenUsage;
            IsFinalAnswer = isFinalAnswer;
        }

        public override List<LLMMessage> ToMessages(bool summaryMode = false)
        {
            List<LLMMessage> messages = new List<LLMMessage>();

            if (ModelOutputMessage != null)
            {
                messages.Add(ModelOutputMessage);
            }

            // CRITICAL: Add tool response messages for each tool call
            if (ToolResponses != null && ToolResponses.Count > 0)
            {
                foreach (var toolResponse in ToolResponses)
                {
                    // Format: "Call id: {tool_call_id}\n{response_content}"
                    string toolContent = $"Call id: {toolResponse.Id}\n{toolResponse.Observation}";

                    var toolMessage = new LLMMessage(
                        MessageRole.ToolResponse,
                        toolContent,
                        toolContent
                    );
                    messages.Add(toolMessage);
                }
            }

            // Handle additional observations if needed
            if (!string.IsNullOrEmpty(Observations))
            {
                var observationMessage = new LLMMessage(
                    MessageRole.User,
                    Observations,
                    Observations
                );
                messages.Add(observationMessage);
            }

            return messages;
        }
    }
}