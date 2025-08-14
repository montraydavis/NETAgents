namespace SmolConv.Models
{
    public record ManagedAgentPromptTemplate
    {
        public string Task { get; init; }
        public string Report { get; init; }

        public ManagedAgentPromptTemplate(string task, string report)
        {
            Task = task;
            Report = report;
        }
    }
}