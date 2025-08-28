namespace NETAgents.Models
{
    public record SystemPromptStep : MemoryStep
    {
        public string SystemPrompt { get; init; }

        public SystemPromptStep(string systemPrompt, Timing? timing = null) : base(timing)
        {
            SystemPrompt = systemPrompt;
        }

        public override List<LLMMessage> ToMessages(bool summaryMode = false)
        {
            if (summaryMode) return new List<LLMMessage>();

            return new List<LLMMessage>
            {
                new LLMMessage(MessageRole.System, SystemPrompt, SystemPrompt)
            };
        }
    }
}