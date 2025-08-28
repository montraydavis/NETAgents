namespace NETAgents.Models
{
    // ===============================
    // MEMORY MODELS
    // ===============================

    public abstract record MemoryStep
    {
        public Timing? Timing { get; init; }

        protected MemoryStep(Timing? timing = null)
        {
            Timing = timing;
        }

        public abstract List<LLMMessage> ToMessages(bool summaryMode = false);
    }
}