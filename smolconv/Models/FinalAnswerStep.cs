namespace SmolConv.Models
{
    public record FinalAnswerStep : MemoryStep
    {
        public object Output { get; init; }

        public FinalAnswerStep(object output, Timing? timing = null) : base(timing)
        {
            Output = output;
        }

        public override List<ChatMessage> ToMessages(bool summaryMode = false)
        {
            return new List<ChatMessage>();
        }
    }
}