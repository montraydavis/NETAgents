using System.Text.Json;
using NETAgents.Exceptions;
using NETAgents.Models;
using NETAgents.Tools;

namespace NETAgents.Core
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

        public override Dictionary<string, Dictionary<string, object>> Inputs => new Dictionary<string, Dictionary<string, object>>();

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
            int maxSteps = 5,
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
            // --- Phase 1: Get current memory messages ---
            List<LLMMessage> memoryMessages = Memory.ToMessages();
            List<LLMMessage> inputMessages = new List<LLMMessage>(memoryMessages);
            actionStep = actionStep with { ModelInputMessages = inputMessages };

            // --- Phase 2: Generate model response ---
            LLMMessage chatMessage;
            try
            {
                chatMessage = await _model.GenerateAsync(
                    inputMessages,
                    new ModelCompletionOptions
                    {
                        ToolsToCallFrom = ToolsAndManagedAgents,
                        ToolChoice = "auto"
                    }, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new AgentGenerationError($"Error while generating output: {ex}", _logger);
            }

            // Parse tool calls if model doesn't support native tool calling
            if (chatMessage.ToolCalls == null || chatMessage.ToolCalls.Count == 0)
            {
                try
                {
                    chatMessage = _model.ParseToolCalls(chatMessage);
                }
                catch (Exception ex)
                {
                    throw new AgentParsingError($"Error while parsing tool call from model output: {ex}", _logger);
                }
            }

            // --- Phase 3: Process tool calls ---
            object? finalAnswer = chatMessage.ContentString;
            bool gotFinalAnswer = false;
            List<ToolOutput> toolResponses = new List<ToolOutput>();

            if (chatMessage.ToolCalls != null && chatMessage.ToolCalls.Count > 0)
            {
                await foreach (object toolOutput in ProcessToolCallsAsync(chatMessage.ToolCalls, cancellationToken))
                {
                    yield return toolOutput;

                    if (toolOutput is ToolOutput output)
                    {
                        toolResponses.Add(output);

                        if (output.IsFinalAnswer)
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
            }

            // --- Phase 4: Update action step with model output and tool responses ---
            actionStep = actionStep with
            {
                ModelOutputMessage = chatMessage,
                ToolResponses = toolResponses.Count > 0 ? toolResponses : null
            };

            // --- Phase 5: If we have tool responses but no final answer, let the model continue ---
            if (toolResponses.Count > 0 && !gotFinalAnswer)
            {
                // Create a follow-up message with tool responses for the model to process
                List<LLMMessage> followUpMessages = new List<LLMMessage>(inputMessages);
                followUpMessages.Add(chatMessage); // Add the assistant message with tool calls

                // Add tool response messages
                foreach (var toolResponse in toolResponses)
                {
                    string toolContent = $"Call id: {toolResponse.Id}\n{toolResponse.Observation}";
                    var toolResponseMessage = new LLMMessage(
                        MessageRole.ToolResponse,
                        toolContent,
                        toolContent
                    );
                    followUpMessages.Add(toolResponseMessage);
                }

                // Generate follow-up response from the model
                try
                {
                    LLMMessage followUpResponse = await _model.GenerateAsync(
                        followUpMessages,
                        new ModelCompletionOptions
                        {
                            // Don't provide tools again to prevent infinite loop
                            ToolsToCallFrom = null,
                            ToolChoice = "none"
                        }, cancellationToken);

                    // Use the follow-up response as the final answer
                    finalAnswer = followUpResponse.ContentString;
                    gotFinalAnswer = true;

                    // Update the action step with the follow-up response
                    actionStep = actionStep with
                    {
                        ModelOutputMessage = followUpResponse,
                        ToolResponses = toolResponses
                    };
                }
                catch (Exception ex)
                {
                    // If follow-up fails, use tool response as final answer
                    if (toolResponses.Count > 0)
                    {
                        finalAnswer = toolResponses.First().Observation;
                        gotFinalAnswer = true;
                    }
                }
            }

            // --- Phase 6: Final action output ---
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
            foreach (ChatMessageToolCall chatToolCall in toolCalls)
            {
                ToolCall toolCall = new ToolCall(chatToolCall.Function.Name, chatToolCall.Function.Arguments, chatToolCall.Id);
                yield return toolCall;

                ToolOutput toolOutput = await ExecuteToolCallAsync(toolCall, cancellationToken);
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
            string toolName = toolCall.Name;
            object? arguments = toolCall.Arguments;

            _logger.Log($"Calling tool: '{toolName}' with arguments: {JsonSerializer.Serialize(arguments)}", LogLevel.Info);

            // 1. Tool Discovery
            Dictionary<string, BaseTool> availableTools = GetAvailableTools();
            if (!availableTools.TryGetValue(toolName, out BaseTool? tool))
            {
                string availableToolNames = string.Join(", ", availableTools.Keys);
                throw new AgentToolExecutionError(
                    $"Unknown tool '{toolName}', should be one of: {availableToolNames}",
                    _logger);
            }

            try
            {
                // 2. Argument Conversion
                Dictionary<string, object>? processedArgs = ConvertAndProcessArguments(arguments, tool);

                // 3. State Variable Validation
                ValidateStateVariables(processedArgs);

                // 4. State Variable Substitution
                processedArgs = (Dictionary<string, object>)SubstituteStateVariables(processedArgs);

                // 5. Tool Argument Validation
                Dictionary<string, object?> nullableProcessedArgs = processedArgs?.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value) ?? new Dictionary<string, object?>();
                Validation.ToolArgumentValidator.ValidateToolArguments(tool, nullableProcessedArgs);

                // 6. Determine execution context
                bool isManagedAgent = _managedAgents.ContainsKey(toolName);

                // 7. Execute tool with proper sanitization
                object? result = await ExecuteToolWithProperSanitization(tool, processedArgs ?? new Dictionary<string, object>(),
                                                                       isManagedAgent, cancellationToken);

                // 8. Create tool output
                return CreateToolOutput(toolCall, result, toolName);
            }
            catch (ArgumentException ex)
            {
                throw new AgentToolCallError(ex.Message, _logger);
            }
            catch (Exception ex)
            {
                HandleToolExecutionError(ex, toolName, arguments, _managedAgents.ContainsKey(toolName));
                throw; // This won't execute, but satisfies compiler
            }
        }

        /// <summary>
        /// Gets all available tools and managed agents
        /// </summary>
        /// <returns>Dictionary of available tools</returns>
        private Dictionary<string, BaseTool> GetAvailableTools()
        {
            Dictionary<string, BaseTool> availableTools = new Dictionary<string, BaseTool>();
            foreach (KeyValuePair<string, Tool> t in _tools) availableTools[t.Key] = t.Value;
            foreach (KeyValuePair<string, MultiStepAgent> agent in _managedAgents) availableTools[agent.Key] = agent.Value;
            return availableTools;
        }

        /// <summary>
        /// Converts and processes arguments to the expected format
        /// </summary>
        /// <param name="arguments">Raw arguments</param>
        /// <param name="tool">The tool being called (for schema checking)</param>
        /// <returns>Processed arguments</returns>
        private Dictionary<string, object> ConvertAndProcessArguments(object? arguments, BaseTool? tool = null)
        {
            return arguments switch
            {
                Dictionary<string, object> dict => dict,
                null => new Dictionary<string, object>(),
                _ => ConvertNonDictionaryArguments(arguments, tool)
            };
        }

        /// <summary>
        /// Converts non-dictionary arguments based on tool schema
        /// </summary>
        /// <param name="arguments">The arguments to convert</param>
        /// <param name="tool">The tool being called</param>
        /// <returns>Converted arguments</returns>
        private Dictionary<string, object> ConvertNonDictionaryArguments(object? arguments, BaseTool? tool)
        {
            // If we have a tool and it's a Tool (not a managed agent), check its schema
            if (tool is Tool t)
            {
                if (t.Inputs.Count == 1)
                {
                    // If the tool has exactly one input parameter, use that parameter name
                    string parameterName = t.Inputs.Keys.First();
                    return new Dictionary<string, object> { [parameterName] = arguments! };
                }
                else if (t.Inputs.Count == 0)
                {
                    // Tool has no inputs, return empty dictionary
                    return new Dictionary<string, object>();
                }
            }

            // For managed agents or tools with multiple inputs, use default mapping
            if (tool != null)
            {
                string defaultArgName = GetDefaultArgumentName(tool.Name);
                return new Dictionary<string, object> { [defaultArgName] = arguments! };
            }

            // Default fallback - don't wrap in any key, let the tool handle it
            return new Dictionary<string, object>();
        }

        /// <summary>
        /// Gets the default argument name for a tool
        /// </summary>
        /// <param name="toolName">The tool name</param>
        /// <returns>The default argument name</returns>
        private string GetDefaultArgumentName(string toolName)
        {
            return toolName switch
            {
                "final_answer" => "answer",
                "search" => "query",
                "calculator" => "expression",
                _ => "input"
            };
        }

        /// <summary>
        /// Executes a tool with proper sanitization based on context
        /// </summary>
        /// <param name="tool">The tool to execute</param>
        /// <param name="kwargs">The arguments</param>
        /// <param name="isManagedAgent">Whether this is a managed agent</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The tool result</returns>
        private async Task<object?> ExecuteToolWithProperSanitization(BaseTool tool,
                                                                    Dictionary<string, object> kwargs,
                                                                    bool isManagedAgent,
                                                                    CancellationToken cancellationToken)
        {
            if (isManagedAgent)
            {
                // Managed agents don't use sanitization
                return await tool.CallAsync(null, kwargs, false, cancellationToken);
            }
            else
            {
                // Regular tools use sanitization
                return await tool.CallAsync(null, kwargs, true, cancellationToken);
            }
        }

        /// <summary>
        /// Creates a tool output from the execution result
        /// </summary>
        /// <param name="toolCall">The original tool call</param>
        /// <param name="result">The execution result</param>
        /// <param name="toolName">The tool name</param>
        /// <returns>Tool output</returns>
        private ToolOutput CreateToolOutput(ToolCall toolCall, object? result, string toolName)
        {
            string observation;

            if (result == null)
            {
                observation = "Tool executed successfully but returned no result.";
            }
            else if (result is string strResult)
            {
                observation = string.IsNullOrWhiteSpace(strResult) ? "Tool executed but returned empty string." : strResult;
            }
            else if (result is Dictionary<string, object> dictResult)
            {
                // Handle Dictionary results specifically - format them in a readable way
                var sb = new System.Text.StringBuilder();

                foreach (var kvp in dictResult)
                {
                    if (kvp.Value is string[] stringArray)
                    {
                        sb.AppendLine($"{kvp.Key}: [{string.Join(", ", stringArray)}]");
                    }
                    else if (kvp.Value is Dictionary<string, object> nestedDict)
                    {
                        sb.AppendLine($"{kvp.Key}:");
                        foreach (var nested in nestedDict)
                        {
                            sb.AppendLine($"  {nested.Key}: {nested.Value}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                    }
                }

                observation = sb.ToString().Trim();

                // Fallback if dictionary formatting failed
                if (string.IsNullOrWhiteSpace(observation))
                {
                    try
                    {
                        observation = JsonSerializer.Serialize(dictResult, new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        });
                    }
                    catch
                    {
                        observation = $"Tool '{toolName}' returned a dictionary with {dictResult.Count} items.";
                    }
                }
            }
            else
            {
                // Handle other complex objects
                try
                {
                    observation = JsonSerializer.Serialize(result, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                    // If serialization results in empty object or null, provide fallback
                    if (string.IsNullOrWhiteSpace(observation) || observation == "{}" || observation == "null")
                    {
                        observation = $"Tool '{toolName}' executed successfully. Result type: {result.GetType().Name}";
                    }
                }
                catch (Exception ex)
                {
                    observation = $"Tool '{toolName}' executed successfully. Result type: {result.GetType().Name}. Serialization failed: {ex.Message}";
                }
            }

            bool isFinalAnswer = toolName == "final_answer";

            _logger.Log($"Observations: {observation}", LogLevel.Info);

            return new ToolOutput(
                id: toolCall.Id,
                output: result,
                isFinalAnswer: isFinalAnswer,
                observation: observation,
                toolCall: toolCall
            );
        }

        /// <summary>
        /// Handles tool execution errors with context-aware messages
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="toolName">The tool name</param>
        /// <param name="arguments">The arguments that were passed</param>
        /// <param name="isManagedAgent">Whether this is a managed agent</param>
        private void HandleToolExecutionError(Exception ex, string toolName, object? arguments, bool isManagedAgent)
        {
            string errorMsg;
            if (isManagedAgent)
            {
                errorMsg = $"Error executing request to team member '{toolName}' with arguments {JsonSerializer.Serialize(arguments)}: {ex.Message}\n" +
                          "Please try again or request to another team member";
            }
            else
            {
                errorMsg = $"Error executing tool '{toolName}' with arguments {JsonSerializer.Serialize(arguments)}: {ex.GetType().Name}: {ex.Message}\n" +
                          "Please try again or use another tool";
            }

            throw new AgentToolExecutionError(errorMsg, _logger, toolName, arguments, isManagedAgent);
        }

        /// <summary>
        /// Agglomerates stream deltas into a single message
        /// </summary>
        /// <param name="deltas">Stream deltas</param>
        /// <returns>Agglomerated message</returns>
        protected virtual LLMMessage AgglomerateStreamDeltas(List<ChatMessageStreamDelta> deltas)
        {
            string content = string.Join("", deltas.Select(d => d.Content ?? ""));
            List<ChatMessageToolCall> toolCalls = new List<ChatMessageToolCall>();  // ← Empty list!

            // Simple aggregation - would need more sophisticated handling for real implementation
            return new LLMMessage(MessageRole.Assistant, content, content, toolCalls);
        }

        /// <summary>
        /// Populates a template with variables
        /// </summary>
        /// <param name="template">Template string</param>
        /// <param name="variables">Variables to substitute</param>
        /// <returns>Populated template</returns>
        protected virtual string PopulateTemplate(string template, Dictionary<string, object> variables)
        {
            string result = template;
            foreach (KeyValuePair<string, object> kvp in variables)
            {
                result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
            }
            return result;
        }

        public override object? Call(object?[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Substitute state variables in arguments with enhanced support for nested structures
        /// </summary>
        /// <param name="arguments">Arguments to substitute</param>
        /// <returns>Arguments with state variables substituted</returns>
        public virtual object SubstituteStateVariables(object arguments)
        {
            return arguments switch
            {
                Dictionary<string, object> dict => SubstituteDictionary(dict),
                string str when State.ContainsKey(str) => State[str],
                string str => str,
                List<object> list => list.Select(SubstituteStateVariables).ToList(),
                Array arr => arr.Cast<object>().Select(SubstituteStateVariables).ToArray(),
                IEnumerable<object> enumerable => enumerable.Select(SubstituteStateVariables).ToArray(),
                _ => arguments
            };
        }

        /// <summary>
        /// Substitute state variables in a dictionary
        /// </summary>
        /// <param name="dict">Dictionary to substitute</param>
        /// <returns>Dictionary with state variables substituted</returns>
        private Dictionary<string, object> SubstituteDictionary(Dictionary<string, object> dict)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kvp in dict)
            {
                result[kvp.Key] = SubstituteStateVariables(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// Support for non-dictionary arguments
        /// </summary>
        /// <param name="arguments">Arguments to substitute</param>
        /// <returns>Arguments with state variables substituted</returns>
        protected virtual object SubstituteStateVariablesForAnyType(object arguments)
        {
            if (arguments is string str)
                return State.ContainsKey(str) ? State[str] : str;

            if (arguments is Dictionary<string, object> dict)
                return SubstituteDictionary(dict);

            return arguments;
        }

        /// <summary>
        /// Validates state variables referenced in arguments
        /// </summary>
        /// <param name="arguments">Arguments to validate</param>
        public virtual void ValidateStateVariables(object arguments)
        {
            List<string> referencedVars = ExtractStateVariableReferences(arguments);
            List<string> missingVars = referencedVars.Where(v => !State.ContainsKey(v)).ToList();

            if (missingVars.Any())
            {
                throw new ArgumentException(
                    $"Referenced state variables not found: {string.Join(", ", missingVars)}");
            }
        }

        /// <summary>
        /// Extracts state variable references from arguments
        /// </summary>
        /// <param name="obj">Object to extract references from</param>
        /// <returns>List of referenced state variable names</returns>
        private List<string> ExtractStateVariableReferences(object obj)
        {
            List<string> references = new List<string>();

            switch (obj)
            {
                case string str:
                    // Only consider strings that look like state variable references
                    // (e.g., contain underscores, are not empty, etc.)
                    if (IsStateVariableReference(str))
                    {
                        references.Add(str);
                    }
                    break;
                case Dictionary<string, object> dict:
                    foreach (object value in dict.Values)
                        references.AddRange(ExtractStateVariableReferences(value));
                    break;
                case IEnumerable<object> enumerable:
                    foreach (object item in enumerable)
                        references.AddRange(ExtractStateVariableReferences(item));
                    break;
            }

            return references;
        }

        /// <summary>
        /// Determines if a string looks like a state variable reference
        /// </summary>
        /// <param name="str">String to check</param>
        /// <returns>True if it looks like a state variable reference</returns>
        private bool IsStateVariableReference(string str)
        {
            // Simple heuristic: state variables typically contain underscores or are camelCase
            return !string.IsNullOrEmpty(str) &&
                   (str.Contains('_') ||
                    (str.Length > 1 && char.IsLower(str[0]) && str.Any(c => char.IsUpper(c))));
        }

        /// <summary>
        /// Handles state variable specific errors
        /// </summary>
        /// <param name="ex">The exception that occurred</param>
        /// <param name="toolName">The tool name</param>
        /// <param name="arguments">The arguments that were passed</param>
        private void HandleStateVariableError(Exception ex, string toolName, object arguments)
        {
            string errorMsg = $"State variable error in tool '{toolName}' with arguments {JsonSerializer.Serialize(arguments)}: {ex.Message}\n" +
                              "Please ensure all referenced state variables are defined";

            throw new AgentToolCallError(errorMsg, _logger);
        }


    }
}