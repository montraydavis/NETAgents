namespace NETAgents.Models
{

    // ===============================
    // RUN RESULT MODEL
    // ===============================

    public record RunResult
    {
        public object? Output { get; init; }
        public string State { get; init; } // "success" or "max_steps_error"
        public List<Dictionary<string, object>> Steps { get; init; }
        public TokenUsage? TokenUsage { get; init; }
        public Timing Timing { get; init; }

        public RunResult(object? output, string state, List<Dictionary<string, object>> steps,
                        TokenUsage? tokenUsage, Timing timing)
        {
            Output = output;
            State = state;
            Steps = steps;
            TokenUsage = tokenUsage;
            Timing = timing;
        }
    }
}