

namespace SmolConv.Core
{
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using SmolConv.Exceptions;
    using SmolConv.Models;
    using SmolConv.Tools;


    /// <summary>
    /// Code-generating agent implementation
    /// </summary>
    public class CodeAgent : MultiStepAgent
    {
        private readonly List<string> _additionalAuthorizedImports;
        private readonly List<string> _authorizedImports;
        private readonly int? _maxPrintOutputsLength;
        private readonly bool _useStructuredOutputsInternally;
        private readonly (string, string) _codeBlockTags;
        private readonly bool _streamOutputs;
        private readonly string _executorType;
        private readonly Dictionary<string, object> _executorKwargs;

        protected PythonExecutor? _pythonExecutor;

        /// <summary>
        /// Gets the authorized imports
        /// </summary>
        public List<string> AuthorizedImports => _authorizedImports;

        public override string Name => "CodeAgent";

        public override Dictionary<string, Dictionary<string, object>> Inputs => new Dictionary<string, Dictionary<string, object>>();

        /// <summary>
        /// Initializes a new instance of CodeAgent
        /// </summary>
        public CodeAgent(
            List<Tool> tools,
            Model model,
            PromptTemplates? promptTemplates = null,
            List<string>? additionalAuthorizedImports = null,
            int? planningInterval = null,
            string executorType = "local",
            Dictionary<string, object>? executorKwargs = null,
            int? maxPrintOutputsLength = null,
            bool streamOutputs = false,
            bool useStructuredOutputsInternally = false,
            (string, string)? codeBlockTags = null,
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
            _additionalAuthorizedImports = additionalAuthorizedImports ?? new List<string>();
            _authorizedImports = new List<string>(LocalPythonExecutor.BaseBuiltinModules);
            _authorizedImports.AddRange(_additionalAuthorizedImports);

            _maxPrintOutputsLength = maxPrintOutputsLength;
            _useStructuredOutputsInternally = useStructuredOutputsInternally;
            _codeBlockTags = codeBlockTags ?? ("<code>", "</code>");
            _streamOutputs = streamOutputs;
            _executorType = executorType;
            _executorKwargs = executorKwargs ?? new Dictionary<string, object>();

            _pythonExecutor = CreatePythonExecutor();
        }

        /// <summary>
        /// Creates the appropriate Python executor
        /// </summary>
        /// <returns>Python executor instance</returns>
        protected virtual PythonExecutor CreatePythonExecutor()
        {
            return _executorType switch
            {
                "local" => new LocalPythonExecutor(_additionalAuthorizedImports, _maxPrintOutputsLength),
                "docker" => new DockerExecutor(_authorizedImports, _logger),
                "e2b" => new E2BExecutor(_authorizedImports, _logger),
                "wasm" => new WasmExecutor(_authorizedImports, _logger),
                _ => throw new ArgumentException($"Unsupported executor type: {_executorType}")
            };
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
                ["authorized_imports"] = _authorizedImports.Contains("*") ?
                    "You can import from any package you want." :
                    string.Join(", ", _authorizedImports),
                ["custom_instructions"] = "",
                ["code_block_opening_tag"] = _codeBlockTags.Item1,
                ["code_block_closing_tag"] = _codeBlockTags.Item2
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

            // --- Build completion options (no yields here) ---
            var stopSequences = new List<string> { "Observation:", "Calling tools:" };
            if (_codeBlockTags.Item2 != _codeBlockTags.Item1)
                stopSequences.Add(_codeBlockTags.Item2);

            var options = new ModelCompletionOptions { StopSequences = stopSequences };

            ChatMessage chatMessage;

            // --- Phase 1: Model generation (stream vs. non-stream) ---
            if (_streamOutputs)
            {
                // No catch around this loop: yielding inside try/catch is illegal.
                var deltas = new List<ChatMessageStreamDelta>();
                await foreach (var delta in _model.GenerateStream(inputMessages, options, cancellationToken))
                {
                    deltas.Add(delta);
                    yield return delta;                    // ✅ allowed (not in catch/finally)
                }
                chatMessage = AgglomerateStreamDeltas(deltas);
            }
            else
            {
                // Catch allowed here because there is no yield inside this try.
                try
                {
                    chatMessage = await _model.GenerateAsync(inputMessages, options, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Wrap non-stream generation failures in your domain error.
                    throw new AgentGenerationError($"Error in generating model output: {ex}", _logger);
                }
            }

            var outputText = chatMessage.Content?.ToString() ?? string.Empty;

            // --- Phase 2: Parse code (with catch), but DO NOT yield inside try/catch ---
            string codeAction;
            try
            {
                if (_useStructuredOutputsInternally)
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(outputText);
                    codeAction = parsed?["code"]?.ToString() ?? string.Empty;
                    codeAction = ExtractCodeFromText(codeAction) ?? codeAction;
                }
                else
                {
                    codeAction = ParseCodeBlobs(outputText);
                }

                codeAction = FixFinalAnswerCode(codeAction);
            }
            catch (Exception ex)
            {
                throw new AgentParsingError($"Error in code parsing: {ex.Message}", _logger);
            }

            // Prepare items to yield AFTER try/catch blocks
            var toolCall = new ToolCall("python_interpreter", codeAction, $"call_{Memory.Steps.Count}");
            ActionOutput? actionOutputToYield = null;

            // --- Phase 3: Execute code (with catch), buffer result ---
            try
            {
                _logger.LogCode("Executing parsed code:", codeAction, LogLevel.Info);

                var codeOutput = _pythonExecutor?.Execute(codeAction)
                                 ?? throw new InvalidOperationException("Python executor not initialized");

                if (codeOutput.Error != null)
                    throw codeOutput.Error;

                var truncatedOutput = TruncateContent(codeOutput.Output?.ToString() ?? string.Empty);
                _logger.Log($"Out: {truncatedOutput}", LogLevel.Info);

                actionOutputToYield = new ActionOutput(codeOutput.Output, codeOutput.IsFinalAnswer);
            }
            catch (Exception ex)
            {
                throw new AgentExecutionError(ex.Message, _logger);
            }

            // --- Phase 4: Perform the yields OUTSIDE catch/finally blocks ---
            yield return toolCall;                 // ✅ safe (not inside try/catch/finally)
            if (actionOutputToYield is not null)
                yield return actionOutputToYield;  // ✅ safe
        }


        /// <summary>
        /// Extracts code from text using regex patterns
        /// </summary>
        /// <param name="text">Text to extract from</param>
        /// <returns>Extracted code or null</returns>
        protected virtual string? ExtractCodeFromText(string text)
        {
            var pattern = $@"{Regex.Escape(_codeBlockTags.Item1)}(.*?){Regex.Escape(_codeBlockTags.Item2)}";
            var matches = Regex.Matches(text, pattern, RegexOptions.Singleline);

            if (matches.Count > 0)
            {
                return string.Join("\n\n", matches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            }

            return null;
        }

        /// <summary>
        /// Parses code blocks from text
        /// </summary>
        /// <param name="text">Text to parse</param>
        /// <returns>Parsed code</returns>
        protected virtual string ParseCodeBlobs(string text)
        {
            var matches = ExtractCodeFromText(text);
            if (matches != null) return matches;

            // Fallback to markdown pattern
            matches = Regex.Match(text, @"```(?:python|py)(.*?)```", RegexOptions.Singleline).Groups[1].Value;
            if (!string.IsNullOrEmpty(matches)) return matches.Trim();

            // Maybe direct code
            try
            {
                // Would need actual Python AST parsing here
                return text;
            }
            catch
            {
                throw new ArgumentException("Invalid code format");
            }
        }

        /// <summary>
        /// Fixes final answer code patterns
        /// </summary>
        /// <param name="code">Code to fix</param>
        /// <returns>Fixed code</returns>
        protected virtual string FixFinalAnswerCode(string code)
        {
            // Simple pattern matching for final_answer calls
            return code;
        }

        /// <summary>
        /// Truncates content to manageable length
        /// </summary>
        /// <param name="content">Content to truncate</param>
        /// <returns>Truncated content</returns>
        protected virtual string TruncateContent(string content)
        {
            const int maxLength = 20000;
            if (content.Length <= maxLength) return content;

            var halfLength = maxLength / 2;
            return content.Substring(0, halfLength) +
                   $"\n..._Content truncated to stay below {maxLength} characters_...\n" +
                   content.Substring(content.Length - halfLength);
        }

        /// <summary>
        /// Agglomerates stream deltas
        /// </summary>
        /// <param name="deltas">Stream deltas</param>
        /// <returns>Agglomerated message</returns>
        protected virtual ChatMessage AgglomerateStreamDeltas(List<ChatMessageStreamDelta> deltas)
        {
            var content = string.Join("", deltas.Select(d => d.Content ?? ""));
            return new ChatMessage(MessageRole.Assistant, content, content);
        }

        /// <summary>
        /// Populates template with variables
        /// </summary>
        /// <param name="template">Template string</param>
        /// <param name="variables">Variables</param>
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

        /// <summary>
        /// Cleans up resources
        /// </summary>
        public override void Dispose()
        {
            _pythonExecutor?.Cleanup();
            base.Dispose();
        }

        public override object? Call(object?[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            return null;
        }
    }
}