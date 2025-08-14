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
            var messages = new List<ChatMessage>();

            if (ModelOutputMessage != null)
            {
                messages.Add(ModelOutputMessage);
            }

            if (!string.IsNullOrEmpty(Observations))
            {
                var content = new List<Dictionary<string, object>>
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