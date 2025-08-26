using System.Text.Json;

namespace SmolConv.Models
{

    public record ActionStep : MemoryStep
    {
        public int StepNumber { get; init; }
        public List<ChatMessage>? ModelInputMessages { get; init; }
        public ChatMessage? ModelOutputMessage { get; init; }
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

        public override List<ChatMessage> ToMessages(bool summaryMode = false)
        {
            List<ChatMessage> messages = new List<ChatMessage>();

            if (ModelOutputMessage != null)
            {
                messages.Add(ModelOutputMessage);
            }

            // Add tool response messages if present
            if (ToolResponses != null && ToolResponses.Count > 0)
            {
                foreach (var toolResponse in ToolResponses)
                {
                    // Create a tool message for each tool response
                    // The content should include the tool_call_id in a format the model can extract
                    string toolContent = $"Call id: {toolResponse.Id}\n{toolResponse.Observation}";
                    
                    var toolMessage = new ChatMessage(
                        MessageRole.ToolResponse, 
                        toolContent, 
                        toolContent
                    );
                    messages.Add(toolMessage);
                }
            }

            if (!string.IsNullOrEmpty(Observations))
            {
                List<Dictionary<string, object>> content = new List<Dictionary<string, object>>
                {
                    new() { ["type"] = "text", ["text"] = Observations }
                };

                if (ObservationsImages != null)
                {
                    foreach (var image in ObservationsImages)
                    {
                        content.Add(new Dictionary<string, object> { ["type"] = "image", ["image"] = image });
                    }
                }

                messages.Add(new ChatMessage(MessageRole.User, content, JsonSerializer.Serialize(content)));
            }

            return messages;
        }
    }
}