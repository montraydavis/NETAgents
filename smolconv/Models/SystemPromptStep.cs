namespace SmolConv.Models
{
    public record SystemPromptStep : MemoryStep
    {
        public string SystemPrompt { get; init; }

        public SystemPromptStep(string systemPrompt, Timing? timing = null) : base(timing)
        {
            SystemPrompt = systemPrompt;
        }

        public override List<ChatMessage> ToMessages(bool summaryMode = false)
        {
            if (summaryMode) return new List<ChatMessage>();

            return new List<ChatMessage>
            {
                new ChatMessage(MessageRole.System, SystemPrompt, SystemPrompt)
            };
        }
    }
}