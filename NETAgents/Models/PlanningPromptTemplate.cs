namespace NETAgents.Models
{
    // ===============================
    // PROMPT TEMPLATE MODELS
    // ===============================

    public record PlanningPromptTemplate
    {
        public string InitialPlan { get; init; }
        public string UpdatePlanPreMessages { get; init; }
        public string UpdatePlanPostMessages { get; init; }

        public PlanningPromptTemplate(string initialPlan, string updatePlanPreMessages, string updatePlanPostMessages)
        {
            InitialPlan = initialPlan;
            UpdatePlanPreMessages = updatePlanPreMessages;
            UpdatePlanPostMessages = updatePlanPostMessages;
        }
    }
}