namespace SmolConv.Models
{
    public record PromptTemplates
    {
        public string SystemPrompt { get; init; }
        public PlanningPromptTemplate Planning { get; init; }
        public ManagedAgentPromptTemplate ManagedAgent { get; init; }
        public FinalAnswerPromptTemplate FinalAnswer { get; init; }

        public PromptTemplates(string systemPrompt, PlanningPromptTemplate planning,
                             ManagedAgentPromptTemplate managedAgent, FinalAnswerPromptTemplate finalAnswer)
        {
            SystemPrompt = systemPrompt;
            Planning = planning;
            ManagedAgent = managedAgent;
            FinalAnswer = finalAnswer;
        }
    }
}