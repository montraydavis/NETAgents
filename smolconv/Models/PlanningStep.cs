namespace SmolConv.Models
{
    public record PlanningStep : MemoryStep
    {
        public List<ChatMessage>? ModelInputMessages { get; init; }
        public string Plan { get; init; }
        public ChatMessage ModelOutputMessage { get; init; }
        public TokenUsage? TokenUsage { get; init; }

        public PlanningStep(string plan, ChatMessage modelOutputMessage, List<ChatMessage>? modelInputMessages = null,
                          TokenUsage? tokenUsage = null, Timing? timing = null) : base(timing)
        {
            Plan = plan;
            ModelOutputMessage = modelOutputMessage;
            ModelInputMessages = modelInputMessages;
            TokenUsage = tokenUsage;
        }

        public override List<ChatMessage> ToMessages(bool summaryMode = false)
        {
            if (summaryMode)
            {
                return new List<ChatMessage>
                {
                    new ChatMessage(MessageRole.Assistant, Plan, Plan)
                };
            }

            return new List<ChatMessage>();
        }
    }
}