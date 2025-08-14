using System.Text.Json;
using SmolConv.Exceptions;
using SmolConv.Models;
using SmolConv.Tools;
using System.Linq;

namespace SmolConv.Core
{
    /// <summary>
    /// Tool-calling agent implementation
    /// </summary>
    public class ToolCallingAgent : MultiStepAgent
    {
        private readonly bool _streamOutputs;
        private readonly int? _maxToolThreads;

        /// <summary>
        /// Gets the tools and managed agents combined
        /// </summary>
        protected List<BaseTool> ToolsAndManagedAgents =>
            _tools.Values.Cast<BaseTool>().Concat(_managedAgents.Values.Cast<BaseTool>()).ToList();

        public override string Name => "ToolCallingAgent";

        /// <summary>
        /// Initializes a new instance of ToolCallingAgent
        /// </summary>
        public ToolCallingAgent(
            List<Tool> tools,
            Model model,
            PromptTemplates? promptTemplates = null,
            int? planningInterval = null,
            bool streamOutputs = false,
            int? maxToolThreads = null,
            string? instructions = null,
            int maxSteps = 20,
            bool addBaseTools = false,
            LogLevel verbosityLevel = LogLevel.Info,
            List<MultiStepAgent>? managedAgents = null,
            Dictionary<Type, List<Action<MemoryStep, object>>>? stepCallbacks = null,
            string? name = null,
            string? description = null,
            bool provideRunSummary = false,
            List<Func<object, AgentMemory, bool>>? finalAnswerChecks = null,
            bool returnFullResult = false,
            AgentLogger? logger = null)
            : base(tools, model, promptTemplates, instructions, maxSteps, addBaseTools, verbosityLevel,
                  managedAgents, stepCallbacks, planningInterval, name, description, provideRunSummary,
                  finalAnswerChecks, returnFullResult, logger)
        {
            _streamOutputs = streamOutputs;
            _maxToolThreads = maxToolThreads;
        }

        /// <summary>
        /// Initializes system prompt
        /// </summary>
        /// <returns>System prompt</returns>
        protected override string InitializeSystemPrompt()
        {
            return PopulateTemplate(_promptTemplates.SystemPrompt, new Dictionary<string, object>
            {
                ["tools"] = _tools,
                ["managed_agents"] = _managedAgents,
                ["custom_instructions"] = ""
            });
        }

        /// <summary>
        /// Performs a step in streaming mode
        /// </summary>
        /// <param name="actionStep">Action step</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream of outputs</returns>
        protected override async IAsyncEnumerable<object> StepStreamAsync(
            ActionStep actionStep,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var memoryMessages = Memory.ToMessages();
            var inputMessages = new List<ChatMessage>(memoryMessages);

            ChatMessage chatMessage;

            // --- Phase 1: Model generation (stream vs non-stream) ---
            if (_streamOutputs)
            {
                // No try/catch around this loop: yielding inside try/catch is illegal in C# iterators.
                var deltas = new List<ChatMessageStreamDelta>();
                await foreach (var delta in _model.GenerateStream(
                    inputMessages,
                    new ModelCompletionOptions
                    {
                        ToolsToCallFrom = ToolsAndManagedAgents,
                        ToolChoice = "auto" // Add tool choice
                    },
                    cancellationToken))
                {
                    deltas.Add(delta);
                    yield return delta; // ✅ OK: not inside catch/finally
                }

                chatMessage = AgglomerateStreamDeltas(deltas);
            }
            else
            {
                // Allowed: no yields inside this try/catch
                try
                {
                    chatMessage = await _model.GenerateAsync(
                        inputMessages,
                        new ModelCompletionOptions
                        {
                            ToolsToCallFrom = ToolsAndManagedAgents,
                            ToolChoice = "auto" // Add tool choice
                        },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new AgentGenerationError($"Error while generating output: {ex}", _logger);
                }
            }

            // --- Phase 2: Ensure tool calls parsed (no yields here, so we can catch) ---
            try
            {
                if (chatMessage.ToolCalls == null || chatMessage.ToolCalls.Count == 0)
                {
                    chatMessage = _model.ParseToolCalls(chatMessage);
                }
            }
            catch (Exception ex)
            {
                throw new AgentGenerationError($"Error while parsing tool calls: {ex}", _logger);
            }

            // --- Phase 3: Process tool calls (stream results). No try/catch around yields. ---
            var finalAnswer = default(object);
            finalAnswer = chatMessage.ContentString;
            var gotFinalAnswer = false;

            if (chatMessage.ToolCalls != null)
            {
                await foreach (var toolOutput in ProcessToolCallsAsync(chatMessage.ToolCalls, cancellationToken))
                {
                    yield return toolOutput; // ✅ streaming tool outputs

                    if (toolOutput is ToolOutput output && output.IsFinalAnswer)
                    {
                        if (chatMessage.ToolCalls.Count > 1)
                            throw new AgentExecutionError("Cannot return answer with multiple tool calls", _logger);

                        if (gotFinalAnswer)
                            throw new AgentToolExecutionError("Multiple final answers returned", _logger);

                        finalAnswer = output.Output;
                        gotFinalAnswer = true;
                    }
                }
            }

            // --- Phase 4: Final action output (outside any catch) ---
            yield return new ActionOutput(finalAnswer, gotFinalAnswer);
        }

        /// <summary>
        /// Processes tool calls
        /// </summary>
        /// <param name="toolCalls">Tool calls to process</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream of tool outputs</returns>
        protected virtual async IAsyncEnumerable<object> ProcessToolCallsAsync(
            List<ChatMessageToolCall> toolCalls,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chatToolCall in toolCalls)
            {
                var toolCall = new ToolCall(chatToolCall.Function.Name, chatToolCall.Function.Arguments, chatToolCall.Id);
                yield return toolCall;

                var toolOutput = await ExecuteToolCallAsync(toolCall, cancellationToken);
                yield return toolOutput;
            }
        }

        /// <summary>
        /// Executes a single tool call
        /// </summary>
        /// <param name="toolCall">Tool call to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tool output</returns>
        protected virtual async Task<ToolOutput> ExecuteToolCallAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
        {
            var toolName = toolCall.Name;
            var arguments = toolCall.Arguments;

            _logger.Log($"Calling tool: '{toolName}' with arguments: {JsonSerializer.Serialize(arguments)}", LogLevel.Info);

            // Check if tool exists
            var availableTools = new Dictionary<string, BaseTool>();
            foreach (var t in _tools) availableTools[t.Key] = t.Value;
            foreach (var agent in _managedAgents) availableTools[agent.Key] = agent.Value;

            if (!availableTools.TryGetValue(toolName, out var tool))
            {
                var availableToolNames = string.Join(", ", availableTools.Keys);
                throw new AgentToolExecutionError(
                    $"Unknown tool '{toolName}', should be one of: {availableToolNames}", _logger);
            }

            try
            {
                // Convert arguments to the expected format
                Dictionary<string, object>? kwargs = null;
                if (arguments is Dictionary<string, object> dictArgs)
                {
                    kwargs = dictArgs;
                }
                else if (arguments != null)
                {
                    // Try to convert to dictionary if possible
                    kwargs = new Dictionary<string, object> { ["input"] = arguments };
                }
                else
                {
                    // Provide default empty dictionary instead of null
                    kwargs = new Dictionary<string, object>();
                }

                // Substitute state variables
                kwargs = SubstituteStateVariables(kwargs);

                // Validate tool arguments
                ValidateToolArguments(tool, kwargs);

                // Determine if this is a managed agent
                var isManagedAgent = _managedAgents.ContainsKey(toolName);

                // Call tool with appropriate arguments
                object? result;
                if (isManagedAgent)
                {
                    result = await tool.CallAsync(null, kwargs, cancellationToken);
                }
                else
                {
                    // For regular tools, call with sanitize_inputs_outputs=True
                    result = await tool.CallAsync(null, kwargs, true, cancellationToken);
                }

                var observation = result?.ToString() ?? "No output";
                var isFinalAnswer = toolName == "final_answer";

                _logger.Log($"Observations: {observation}", LogLevel.Info);

                return new ToolOutput(
                    id: toolCall.Id,
                    output: result,
                    isFinalAnswer: isFinalAnswer,
                    observation: observation,
                    toolCall: toolCall
                );
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error executing tool '{toolName}' with arguments {JsonSerializer.Serialize(arguments)}: {ex.Message}";
                _logger.Log($"Tool execution error: {errorMsg}", LogLevel.Error);
                throw new AgentToolExecutionError(errorMsg, _logger);
            }
        }

        /// <summary>
        /// Agglomerates stream deltas into a single message
        /// </summary>
        /// <param name="deltas">Stream deltas</param>
        /// <returns>Agglomerated message</returns>
        protected virtual ChatMessage AgglomerateStreamDeltas(List<ChatMessageStreamDelta> deltas)
        {
            var content = string.Join("", deltas.Select(d => d.Content ?? ""));
            var toolCalls = new List<ChatMessageToolCall>();  // ← Empty list!

            // Simple aggregation - would need more sophisticated handling for real implementation
            return new ChatMessage(MessageRole.Assistant, content, content, toolCalls);
        }

        /// <summary>
        /// Populates a template with variables
        /// </summary>
        /// <param name="template">Template string</param>
        /// <param name="variables">Variables to substitute</param>
        /// <returns>Populated template</returns>
        protected virtual string PopulateTemplate(string template, Dictionary<string, object> variables)
        {
            var result = template;
            foreach (var kvp in variables)
            {
                result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
            }
            return result;
        }

        public override object? Call(object[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Substitute state variables in arguments
        /// </summary>
        /// <param name="arguments">Arguments to substitute</param>
        /// <returns>Arguments with state variables substituted</returns>
        protected virtual Dictionary<string, object> SubstituteStateVariables(Dictionary<string, object> arguments)
        {
            return arguments.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value is string str && State.ContainsKey(str) ? State[str] : kvp.Value
            );
        }

        /// <summary>
        /// Validate tool arguments against tool's input schema
        /// </summary>
        /// <param name="tool">Tool to validate arguments for</param>
        /// <param name="arguments">Arguments to validate</param>
        protected virtual void ValidateToolArguments(BaseTool tool, object arguments)
        {
            // Basic validation - in a full implementation, this would check argument types, required parameters, etc.
            // For now, we'll just ensure arguments is not null for tools that expect them
            if (arguments == null && tool is Tool t && t.Inputs.Count > 0)
            {
                throw new ArgumentException("Tool requires arguments but none were provided");
            }
        }
    }
}