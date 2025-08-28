namespace NETAgents.Models
{
    public record PlanningStep : MemoryStep
    {
        public List<LLMMessage>? ModelInputMessages { get; init; }
        public string Plan { get; init; }
        public LLMMessage ModelOutputMessage { get; init; }
        public TokenUsage? TokenUsage { get; init; }

        public PlanningStep(string plan, LLMMessage modelOutputMessage, List<LLMMessage>? modelInputMessages = null,
                          TokenUsage? tokenUsage = null, Timing? timing = null) : base(timing)
        {
            Plan = plan;
            ModelOutputMessage = modelOutputMessage;
            ModelInputMessages = modelInputMessages;
            TokenUsage = tokenUsage;
        }

        public override List<LLMMessage> ToMessages(bool summaryMode = false)
        {
            if (summaryMode)
            {
                return new List<LLMMessage>
                {
                    new LLMMessage(MessageRole.Assistant, Plan, Plan)
                };
            }

            return new List<LLMMessage>();
        }
    }
}