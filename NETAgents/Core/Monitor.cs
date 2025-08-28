using NETAgents.Models;
using NETAgents.Tools;

namespace NETAgents.Core
{
    /// <summary>
    /// Monitor for tracking agent performance metrics
    /// </summary>
    public class Monitor
    {
        private readonly Model _trackedModel;
        private readonly AgentLogger _logger;
        private readonly List<double> _stepDurations = new();
        private int _totalInputTokenCount;
        private int _totalOutputTokenCount;
        private readonly object _lockObject = new();

        /// <summary>
        /// Gets the total token usage
        /// </summary>
        public TokenUsage TotalTokenUsage => new(_totalInputTokenCount, _totalOutputTokenCount);

        /// <summary>
        /// Gets the list of step durations
        /// </summary>
        public IReadOnlyList<double> StepDurations 
        {
            get 
            {
                lock (_lockObject)
                {
                    return _stepDurations.ToList();
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the Monitor class
        /// </summary>
        /// <param name="trackedModel">The model to track</param>
        /// <param name="logger">The logger instance</param>
        public Monitor(Model trackedModel, AgentLogger logger)
        {
            _trackedModel = trackedModel ?? throw new ArgumentNullException(nameof(trackedModel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Resets all tracking metrics
        /// </summary>
        public void Reset()
        {
            lock (_lockObject)
            {
                _stepDurations.Clear();
                _totalInputTokenCount = 0;
                _totalOutputTokenCount = 0;
            }
        }

        /// <summary>
        /// Updates metrics based on a memory step
        /// </summary>
        /// <param name="step">The memory step to extract metrics from</param>
        public void UpdateMetrics(MemoryStep step)
        {
            if (step.Timing?.Duration == null) return;

            lock (_lockObject)
            {
                _stepDurations.Add(step.Timing.Duration.Value);
                
                string consoleOutput = $"[Step {_stepDurations.Count}: Duration {step.Timing.Duration.Value:F2} seconds";

                // Extract token usage if available
                TokenUsage? tokenUsage = step switch
                {
                    ActionStep actionStep => actionStep.TokenUsage,
                    PlanningStep planningStep => planningStep.TokenUsage,
                    _ => null
                };

                if (tokenUsage != null)
                {
                    _totalInputTokenCount += tokenUsage.InputTokens;
                    _totalOutputTokenCount += tokenUsage.OutputTokens;
                    consoleOutput += $" | Input tokens: {_totalInputTokenCount:N0} | Output tokens: {_totalOutputTokenCount:N0}";
                }

                consoleOutput += "]";
                _logger.Log(consoleOutput, LogLevel.Info);
            }
        }

        /// <summary>
        /// Gets average step duration
        /// </summary>
        /// <returns>Average duration in seconds</returns>
        public double GetAverageStepDuration()
        {
            lock (_lockObject)
            {
                return _stepDurations.Count > 0 ? _stepDurations.Average() : 0.0;
            }
        }

        /// <summary>
        /// Gets total execution time
        /// </summary>
        /// <returns>Total time in seconds</returns>
        public double GetTotalExecutionTime()
        {
            lock (_lockObject)
            {
                return _stepDurations.Sum();
            }
        }
    }
}