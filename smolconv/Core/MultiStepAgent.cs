using SmolConv.Exceptions;
using SmolConv.Models;
using SmolConv.Tools;


namespace SmolConv.Core
{
    // ===============================
    // AGENT IMPLEMENTATIONS
    // ===============================

    /// <summary>
    /// Abstract base class for multi-step agents
    /// </summary>
    public abstract class MultiStepAgent : BaseTool, IDisposable
    {
        protected readonly Model _model;
        protected readonly Dictionary<string, Tool> _tools;
        protected readonly Dictionary<string, MultiStepAgent> _managedAgents;
        protected readonly PromptTemplates _promptTemplates;
        protected readonly AgentLogger _logger;
        protected readonly Monitor _monitor;
        protected readonly CallbackRegistry _stepCallbacks;
        protected readonly List<Func<object, AgentMemory, bool>> _finalAnswerChecks;

        // /// <summary>
        // /// Gets or sets the agent name
        // /// </summary>
        // public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the agent description
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets the maximum number of steps
        /// </summary>
        public int MaxSteps { get; }

        /// <summary>
        /// Gets the planning interval
        /// </summary>
        public int? PlanningInterval { get; }

        /// <summary>
        /// Gets whether to return full results
        /// </summary>
        public bool ReturnFullResult { get; }

        /// <summary>
        /// Gets the agent memory
        /// </summary>
        public AgentMemory Memory { get; }

        /// <summary>
        /// Gets the current task
        /// </summary>
        public string? Task { get; protected set; }

        /// <summary>
        /// Gets the agent state variables
        /// </summary>
        public Dictionary<string, object> State { get; } = new();

        /// <summary>
        /// Gets or sets the interrupt switch
        /// </summary>
        public bool InterruptSwitch { get; set; }

        /// <summary>
        /// Gets the current step number
        /// </summary>
        public int StepNumber { get; protected set; }

        /// <summary>
        /// Initializes a new instance of MultiStepAgent
        /// </summary>
        protected MultiStepAgent(
            List<Tool> tools,
            Model model,
            PromptTemplates? promptTemplates = null,
            string? instructions = null,
            int maxSteps = 20,
            bool addBaseTools = false,
            LogLevel verbosityLevel = LogLevel.Info,
            List<MultiStepAgent>? managedAgents = null,
            Dictionary<Type, List<Action<MemoryStep, object>>>? stepCallbacks = null,
            int? planningInterval = null,
            string? name = null,
            string? description = null,
            bool provideRunSummary = false,
            List<Func<object, AgentMemory, bool>>? finalAnswerChecks = null,
            bool returnFullResult = false,
            AgentLogger? logger = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _tools = tools.ToDictionary(t => t.Name, t => t);
            _managedAgents = managedAgents?.ToDictionary(a => a.Name!, a => a) ?? new Dictionary<string, MultiStepAgent>();
            _promptTemplates = promptTemplates ?? new PromptTemplates("",
                new PlanningPromptTemplate("", "", ""),
                new ManagedAgentPromptTemplate("", ""),
                new FinalAnswerPromptTemplate("", ""));

            MaxSteps = maxSteps;
            PlanningInterval = planningInterval;
            // Name = name;
            Description = description;
            ReturnFullResult = returnFullResult;
            _finalAnswerChecks = finalAnswerChecks ?? new List<Func<object, AgentMemory, bool>>();

            _logger = logger ?? new AgentLogger(verbosityLevel);
            _monitor = new Monitor(_model, _logger);
            _stepCallbacks = new CallbackRegistry();

            if (addBaseTools)
            {
                AddBaseTool(new FinalAnswerTool());
            }

            if (!_tools.ContainsKey("final_answer"))
            {
                _tools["final_answer"] = new FinalAnswerTool();
            }

            Memory = new AgentMemory(InitializeSystemPrompt());

            SetupStepCallbacks(stepCallbacks);
        }

        /// <summary>
        /// Initializes the system prompt
        /// </summary>
        /// <returns>System prompt string</returns>
        protected abstract string InitializeSystemPrompt();

        /// <summary>
        /// Adds a base tool to the agent
        /// </summary>
        /// <param name="tool">Tool to add</param>
        protected void AddBaseTool(Tool tool)
        {
            _tools[tool.Name] = tool;
        }

        /// <summary>
        /// Sets up step callbacks
        /// </summary>
        /// <param name="stepCallbacks">Callback configuration</param>
        protected void SetupStepCallbacks(Dictionary<Type, List<Action<MemoryStep, object>>>? stepCallbacks)
        {
            if (stepCallbacks != null)
            {
                foreach (KeyValuePair<Type, List<Action<MemoryStep, object>>> kvp in stepCallbacks)
                {
                    foreach (Action<MemoryStep, object> callback in kvp.Value)
                    {
                        // This would need proper generic handling
                        _stepCallbacks.Register<ActionStep>((step, agent) => callback(step, agent));
                    }
                }
            }

            _stepCallbacks.Register<ActionStep>((step, agent) => _monitor.UpdateMetrics(step));
        }

        /// <summary>
        /// Runs the agent for the given task
        /// </summary>
        /// <param name="task">Task to perform</param>
        /// <param name="stream">Whether to stream results</param>
        /// <param name="reset">Whether to reset memory</param>
        /// <param name="images">Optional images</param>
        /// <param name="additionalArgs">Additional arguments</param>
        /// <param name="maxSteps">Override max steps</param>
        /// <param name="returnFullResult">Override return full result</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Agent result</returns>
        public async Task<object> RunAsync(
            string task,
            bool stream = false,
            bool reset = true,
            List<object>? images = null,
            Dictionary<string, object>? additionalArgs = null,
            int? maxSteps = null,
            bool? returnFullResult = null,
            CancellationToken cancellationToken = default)
        {
            int effectiveMaxSteps = maxSteps ?? MaxSteps;
            bool effectiveReturnFullResult = returnFullResult ?? ReturnFullResult;

            Task = task;
            InterruptSwitch = false;

            if (additionalArgs != null)
            {
                foreach (KeyValuePair<string, object> kvp in additionalArgs)
                {
                    State[kvp.Key] = kvp.Value;
                }
            }

            if (reset)
            {
                Memory.Reset();
                _monitor.Reset();
            }

            _logger.LogTask(task.Trim(), $"{_model.GetType().Name} - {_model.ModelId}");
            Memory.AddStep(new TaskStep(task, images));

            DateTime runStartTime = DateTime.UtcNow;

            if (stream)
            {
                return RunStreamAsync(effectiveMaxSteps, images, cancellationToken);
            }

            List<MemoryStep> steps = new List<MemoryStep>();
            await foreach (MemoryStep step in RunStreamAsync(effectiveMaxSteps, images, cancellationToken))
            {
                steps.Add(step);
            }

            FinalAnswerStep? finalStep = steps.LastOrDefault() as FinalAnswerStep;
            object? output = finalStep?.Output;

            if (effectiveReturnFullResult)
            {
                int totalInputTokens = 0;
                int totalOutputTokens = 0;
                bool correctTokenUsage = true;

                foreach (MemoryStep step in Memory.Steps)
                {
                    if (step is ActionStep actionStep && actionStep.TokenUsage != null)
                    {
                        totalInputTokens += actionStep.TokenUsage.InputTokens;
                        totalOutputTokens += actionStep.TokenUsage.OutputTokens;
                    }
                    else if (step is PlanningStep planningStep && planningStep.TokenUsage != null)
                    {
                        totalInputTokens += planningStep.TokenUsage.InputTokens;
                        totalOutputTokens += planningStep.TokenUsage.OutputTokens;
                    }
                    else
                    {
                        correctTokenUsage = false;
                    }
                }

                TokenUsage? tokenUsage = correctTokenUsage ? new TokenUsage(totalInputTokens, totalOutputTokens) : null;
                string state = Memory.Steps.Any(s => s is ActionStep a && a.Error is AgentMaxStepsError) ? "max_steps_error" : "success";

                return new RunResult(
                    output: output,
                    state: state,
                    steps: Memory.GetFullSteps(),
                    tokenUsage: tokenUsage,
                    timing: new Timing((runStartTime - DateTime.UnixEpoch).TotalSeconds, (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds)
                );
            }

            return output ?? new object();
        }

        /// <summary>
        /// Runs the agent in streaming mode
        /// </summary>
        /// <param name="maxSteps">Maximum steps</param>
        /// <param name="images">Optional images</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Async enumerable of memory steps</returns>
        protected virtual async IAsyncEnumerable<MemoryStep> RunStreamAsync(
    int maxSteps,
    List<object>? images = null,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StepNumber = 1;
            bool returnedFinalAnswer = false;

            while (!returnedFinalAnswer && StepNumber <= maxSteps)
            {
                if (InterruptSwitch)
                {
                    throw new AgentError("Agent interrupted.", _logger);
                }

                // Run planning step if scheduled
                if (PlanningInterval.HasValue && (StepNumber == 1 || (StepNumber - 1) % PlanningInterval.Value == 0))
                {
                    PlanningStep planningStep = await GeneratePlanningStepAsync(Task!, StepNumber == 1, StepNumber, cancellationToken);
                    Memory.AddStep(planningStep);
                    yield return planningStep;
                }

                // Run action step
                DateTime actionStepStartTime = DateTime.UtcNow;
                ActionStep actionStep = new ActionStep(
                    StepNumber,
                    new Timing((actionStepStartTime - DateTime.UnixEpoch).TotalSeconds),
                    images);

                _logger.LogRule($"Step {StepNumber}", LogLevel.Info);

                bool stepCompleted = false;

                try
                {
                    await foreach (object output in StepStreamAsync(actionStep, cancellationToken))
                    {
                        if (output is ActionOutput actionOutput && actionOutput.IsFinalAnswer)
                        {
                            object? finalAnswer = actionOutput.Output;
                            _logger.Log($"Final answer: {finalAnswer}", LogLevel.Info);

                            ValidateFinalAnswer(finalAnswer);
                            returnedFinalAnswer = true;
                            actionStep = actionStep with { IsFinalAnswer = true, ActionOutput = finalAnswer };
                        }
                    }
                }
                catch (AgentGenerationError)
                {
                    throw;
                }
                catch (AgentError ex)
                {
                    actionStep = actionStep with { Error = ex };
                }
                finally
                {
                    FinalizeStep(actionStep);
                    Memory.AddStep(actionStep);
                    stepCompleted = true;
                }

                if (stepCompleted)
                    yield return actionStep;

                StepNumber++;
            }

            if (!returnedFinalAnswer && StepNumber == maxSteps + 1)
            {
                ChatMessage finalAnswer = await ProvideFinalAnswerAsync(Task!, images, cancellationToken);
                ActionStep finalStep = new ActionStep(
                    StepNumber,
                    new Timing((DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds),
                    error: new AgentMaxStepsError("Reached max steps.", _logger),
                    tokenUsage: finalAnswer.TokenUsage)
                {
                    ActionOutput = finalAnswer.Content
                };

                FinalizeStep(finalStep);
                Memory.AddStep(finalStep);
                yield return finalStep;
            }

            // Find the actual final answer from memory steps
            object? actualFinalAnswer = new object();
            foreach (MemoryStep step in Memory.Steps)
            {
                if (step is ActionStep actionStep && actionStep.IsFinalAnswer && actionStep.ActionOutput != null)
                {
                    actualFinalAnswer = actionStep.ActionOutput;
                    break;
                }
            }

            yield return new FinalAnswerStep(actualFinalAnswer);
        }


        /// <summary>
        /// Performs a single step in streaming mode
        /// </summary>
        /// <param name="actionStep">Action step</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream of outputs</returns>
        protected abstract IAsyncEnumerable<object> StepStreamAsync(ActionStep actionStep, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a planning step
        /// </summary>
        /// <param name="task">Current task</param>
        /// <param name="isFirstStep">Whether this is the first step</param>
        /// <param name="stepNumber">Current step number</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Planning step</returns>
        protected virtual async Task<PlanningStep> GeneratePlanningStepAsync(string task, bool isFirstStep, int stepNumber, CancellationToken cancellationToken = default)
        {
            // Placeholder implementation
            await System.Threading.Tasks.Task.Delay(100, cancellationToken);
            return new PlanningStep("Generated plan", new ChatMessage(MessageRole.Assistant, "Plan generated", "Plan generated"));
        }

        /// <summary>
        /// Provides final answer when max steps reached
        /// </summary>
        /// <param name="task">Current task</param>
        /// <param name="images">Optional images</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Final answer message</returns>
        protected virtual async Task<ChatMessage> ProvideFinalAnswerAsync(string task, List<object>? images = null, CancellationToken cancellationToken = default)
        {
            List<ChatMessage> messages = Memory.ToMessages();
            return await _model.GenerateAsync(messages, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Validates final answer
        /// </summary>
        /// <param name="finalAnswer">Final answer to validate</param>
        protected virtual void ValidateFinalAnswer(object? finalAnswer)
        {
            foreach (Func<object, AgentMemory, bool> check in _finalAnswerChecks)
            {
                try
                {
                    if (!check(finalAnswer ?? new object(), Memory))
                    {
                        throw new AgentError($"Final answer validation failed", _logger);
                    }
                }
                catch (Exception ex)
                {
                    throw new AgentError($"Check failed with error: {ex}", _logger);
                }
            }
        }

        /// <summary>
        /// Finalizes a step by updating timing and executing callbacks
        /// </summary>
        /// <param name="step">Step to finalize</param>
        protected virtual void FinalizeStep(MemoryStep step)
        {
            if (step.Timing != null)
            {
                Timing timing = step.Timing with { EndTime = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds };
                // Update step timing - would need to implement with records properly
            }

            _stepCallbacks.ExecuteCallbacks(step, this);
        }

        /// <summary>
        /// Interrupts agent execution
        /// </summary>
        public void Interrupt()
        {
            InterruptSwitch = true;
        }

        public virtual void Dispose()
        {

        }
    }
}