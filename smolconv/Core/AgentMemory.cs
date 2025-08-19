using SmolConv.Models;


namespace SmolConv.Core
{
    /// <summary>
    /// Agent memory management for conversation history
    /// </summary>
    public class AgentMemory
    {
        private readonly List<MemoryStep> _steps = new();
        private SystemPromptStep? _systemPrompt;

        /// <summary>
        /// Gets or sets the system prompt step
        /// </summary>
        public SystemPromptStep? SystemPrompt
        {
            get => _systemPrompt;
            set => _systemPrompt = value;
        }

        /// <summary>
        /// Gets the list of memory steps
        /// </summary>
        public IReadOnlyList<MemoryStep> Steps => _steps.AsReadOnly();

        /// <summary>
        /// Initializes a new instance of AgentMemory
        /// </summary>
        /// <param name="systemPrompt">Initial system prompt</param>
        public AgentMemory(string? systemPrompt = null)
        {
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                _systemPrompt = new SystemPromptStep(systemPrompt);
            }
        }

        /// <summary>
        /// Adds a memory step
        /// </summary>
        /// <param name="step">Step to add</param>
        public void AddStep(MemoryStep step)
        {
            _steps.Add(step);
        }

        /// <summary>
        /// Resets the memory, keeping only the system prompt
        /// </summary>
        public void Reset()
        {
            _steps.Clear();
        }

        /// <summary>
        /// Converts memory to a list of chat messages
        /// </summary>
        /// <param name="summaryMode">Whether to use summary mode</param>
        /// <returns>List of chat messages</returns>
        public List<ChatMessage> ToMessages(bool summaryMode = false)
        {
            List<ChatMessage> messages = new List<ChatMessage>();

            if (_systemPrompt != null)
            {
                messages.AddRange(_systemPrompt.ToMessages(summaryMode));
            }

            foreach (MemoryStep step in _steps)
            {
                messages.AddRange(step.ToMessages(summaryMode));
            }

            return messages;
        }

        /// <summary>
        /// Gets full step information as dictionaries
        /// </summary>
        /// <returns>List of step dictionaries</returns>
        public List<Dictionary<string, object>> GetFullSteps()
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            if (_systemPrompt != null)
            {
                result.Add(StepToDict(_systemPrompt));
            }

            foreach (MemoryStep step in _steps)
            {
                result.Add(StepToDict(step));
            }

            return result;
        }

        /// <summary>
        /// Converts a memory step to dictionary
        /// </summary>
        /// <param name="step">Memory step</param>
        /// <returns>Dictionary representation</returns>
        private Dictionary<string, object> StepToDict(MemoryStep step)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>
            {
                ["type"] = step.GetType().Name,
                ["timing"] = new Dictionary<string, object?>(){
                    ["start_time"] = step.Timing?.StartTime,
                    ["end_time"] = step.Timing?.EndTime,
                    ["duration"] = step.Timing?.Duration
                }
            };

            switch (step)
            {
                case SystemPromptStep systemStep:
                    dict["system_prompt"] = systemStep.SystemPrompt;
                    break;
                case TaskStep taskStep:
                    dict["task"] = taskStep.Task;
                    if (taskStep.TaskImages != null) dict["task_images"] = taskStep.TaskImages;
                    break;
                case ActionStep actionStep:
                    dict["step_number"] = actionStep.StepNumber;
                    if (actionStep.ModelOutput != null) dict["model_output"] = actionStep.ModelOutput;
                    if (actionStep.ToolCalls != null) dict["tool_calls"] = actionStep.ToolCalls;
                    if (actionStep.Observations != null) dict["observations"] = actionStep.Observations;
                    if (actionStep.CodeAction != null) dict["code_action"] = actionStep.CodeAction;
                    if (actionStep.ActionOutput != null) dict["action_output"] = actionStep.ActionOutput;
                    dict["is_final_answer"] = actionStep.IsFinalAnswer;
                    if (actionStep.Error != null) dict["error"] = actionStep.Error.Message;
                    if (actionStep.TokenUsage != null) dict["token_usage"] = actionStep.TokenUsage;
                    break;
                case PlanningStep planningStep:
                    dict["plan"] = planningStep.Plan;
                    if (planningStep.TokenUsage != null) dict["token_usage"] = planningStep.TokenUsage;
                    break;
                case FinalAnswerStep finalStep:
                    dict["output"] = finalStep.Output;
                    break;
            }

            return dict;
        }
    }
}