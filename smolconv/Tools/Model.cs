using SmolConv.Models;

namespace SmolConv.Tools
{
    /// <summary>
    /// Abstract base class for all language model implementations.
    /// 
    /// This abstract class defines the core interface that all model implementations must follow
    /// to work with agents. It provides common functionality for message handling, tool integration,
    /// and model configuration while allowing subclasses to implement their specific generation logic.
    /// </summary>
    public abstract class Model
    {
        /// <summary>
        /// Gets or sets whether to flatten complex message content into plain text format
        /// </summary>
        public bool FlattenMessagesAsText { get; set; }

        /// <summary>
        /// Gets or sets the key used to extract tool names from model responses
        /// </summary>
        public string ToolNameKey { get; set; } = "name";

        /// <summary>
        /// Gets or sets the key used to extract tool arguments from model responses
        /// </summary>
        public string ToolArgumentsKey { get; set; } = "arguments";

        /// <summary>
        /// Gets the model identifier
        /// </summary>
        public virtual string? ModelId { get; protected set; }

        /// <summary>
        /// Gets additional keyword arguments for model calls
        /// </summary>
        public Dictionary<string, object> AdditionalArguments { get; protected set; } = new();

        /// <summary>
        /// Initializes a new instance of the Model class
        /// </summary>
        /// <param name="flattenMessagesAsText">Whether to flatten messages as text</param>
        /// <param name="toolNameKey">Key for tool names</param>
        /// <param name="toolArgumentsKey">Key for tool arguments</param>
        /// <param name="modelId">Model identifier</param>
        /// <param name="additionalArguments">Additional arguments</param>
        protected Model(bool flattenMessagesAsText = false, string toolNameKey = "name",
                       string toolArgumentsKey = "arguments", string? modelId = null,
                       Dictionary<string, object>? additionalArguments = null)
        {
            FlattenMessagesAsText = flattenMessagesAsText;
            ToolNameKey = toolNameKey;
            ToolArgumentsKey = toolArgumentsKey;
            ModelId = modelId;
            AdditionalArguments = additionalArguments ?? new Dictionary<string, object>();
        }

        /// <summary>
        /// Process the input messages and return the model's response
        /// </summary>
        /// <param name="messages">A list of messages to be processed</param>
        /// <param name="options">Completion options</param>
        /// <returns>A chat message containing the model's response</returns>
        public abstract Task<ChatMessage> Generate(List<ChatMessage> messages, ModelCompletionOptions? options = null);

        /// <summary>
        /// Process the input messages and return the model's response (async version)
        /// </summary>
        /// <param name="messages">A list of messages to be processed</param>
        /// <param name="options">Completion options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A chat message containing the model's response</returns>
        public virtual Task<ChatMessage> GenerateAsync(List<ChatMessage> messages, ModelCompletionOptions? options = null,
                                                       CancellationToken cancellationToken = default)
        {
            return Generate(messages, options);
        }

        /// <summary>
        /// Process the input messages and return a stream of response deltas
        /// </summary>
        /// <param name="messages">A list of messages to be processed</param>
        /// <param name="options">Completion options</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An async enumerable of chat message stream deltas</returns>
        public virtual async IAsyncEnumerable<ChatMessageStreamDelta> GenerateStream(
            List<ChatMessage> messages,
            ModelCompletionOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Default implementation: convert synchronous Generate to single stream item
            ChatMessage result = await GenerateAsync(messages, options, cancellationToken);

            if (result.Content is string content)
            {
                yield return new ChatMessageStreamDelta(content, result.ToolCalls?.ConvertAll(tc =>
                    new ChatMessageToolCallStreamDelta(0, tc.Id, tc.Type, tc.Function)), result.TokenUsage);
            }
        }

        /// <summary>
        /// Parse tool calls from a chat message
        /// </summary>
        /// <param name="message">The message to parse tool calls from</param>
        /// <returns>The message with parsed tool calls</returns>
        public virtual ChatMessage ParseToolCalls(ChatMessage message)
        {
            // Default implementation - subclasses should override if needed
            return message;
        }

        /// <summary>
        /// Converts the model into a dictionary representation
        /// </summary>
        /// <returns>Dictionary representation of the model</returns>
        public virtual Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> result = new Dictionary<string, object>(AdditionalArguments)
            {
                ["model_id"] = ModelId ?? string.Empty,
                ["flatten_messages_as_text"] = FlattenMessagesAsText,
                ["tool_name_key"] = ToolNameKey,
                ["tool_arguments_key"] = ToolArgumentsKey
            };

            return result;
        }

        /// <summary>
        /// Creates a model instance from a dictionary representation
        /// </summary>
        /// <param name="modelDictionary">Dictionary containing model configuration</param>
        /// <returns>Model instance</returns>
        public static Model FromDict(Dictionary<string, object> modelDictionary)
        {
            throw new NotImplementedException("FromDict must be implemented by concrete model classes");
        }

        /// <summary>
        /// Prepares completion arguments for the model call
        /// </summary>
        /// <param name="messages">Input messages</param>
        /// <param name="options">Completion options</param>
        /// <returns>Prepared arguments for the model call</returns>
        protected virtual Dictionary<string, object> PrepareCompletionArguments(
            List<ChatMessage> messages,
            ModelCompletionOptions? options = null)
        {
            Dictionary<string, object> args = new Dictionary<string, object>(AdditionalArguments)
            {
                ["messages"] = ProcessMessages(messages, options)
            };

            if (options?.StopSequences != null && SupportsStopParameter())
            {
                args["stop"] = options.StopSequences;
            }

            if (options?.ResponseFormat != null)
            {
                args["response_format"] = options.ResponseFormat;
            }

            if (options?.ToolsToCallFrom != null && options.ToolsToCallFrom.Count > 0)
            {
                args["tools"] = ConvertToolsToSchema(options.ToolsToCallFrom);

                // Always set tool_choice if tools are provided
                if (options.ToolChoice != null)
                {
                    args["tool_choice"] = options.ToolChoice;
                }
                else
                {
                    // Default to "auto" if tools are provided but no tool_choice specified
                    args["tool_choice"] = "auto";
                }
            }

            // Add any additional parameters
            if (options?.AdditionalParameters != null)
            {
                foreach (KeyValuePair<string, object> kvp in options.AdditionalParameters)
                {
                    args[kvp.Key] = kvp.Value;
                }
            }

            return args;
        }

        /// <summary>
        /// Processes messages according to model requirements
        /// </summary>
        /// <param name="messages">Input messages</param>
        /// <param name="options">Completion options</param>
        /// <returns>Processed messages</returns>
        protected virtual List<Dictionary<string, object>> ProcessMessages(
            List<ChatMessage> messages,
            ModelCompletionOptions? options = null)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();

            foreach (ChatMessage message in messages)
            {
                Dictionary<string, object> messageDict = new Dictionary<string, object>
                {
                    ["role"] = ConvertRole(message.Role, options?.CustomRoleConversions),
                    ["content"] = ProcessMessageContent(message.Content, options)
                };

                if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    messageDict["tool_calls"] = message.ToolCalls.ConvertAll(tc => new Dictionary<string, object>
                    {
                        ["id"] = tc.Id,
                        ["type"] = tc.Type,
                        ["function"] = new Dictionary<string, object>
                        {
                            ["name"] = tc.Function.Name,
                            ["arguments"] = tc.Function.Arguments ?? new object()
                        }
                    });
                }

                result.Add(messageDict);
            }

            return result;
        }

        /// <summary>
        /// Processes message content according to model requirements
        /// </summary>
        /// <param name="content">Message content</param>
        /// <param name="options">Completion options</param>
        /// <returns>Processed content</returns>
        protected virtual object ProcessMessageContent(object? content, ModelCompletionOptions? options = null)
        {
            if (content == null)
                return string.Empty;

            if (FlattenMessagesAsText && content is List<Dictionary<string, object>> contentList)
            {
                // Extract text from structured content
                List<string> textParts = new List<string>();
                foreach (Dictionary<string, object> item in contentList)
                {
                    if (item.TryGetValue("text", out object? text))
                    {
                        textParts.Add(text.ToString() ?? string.Empty);
                    }
                }
                return string.Join("\n", textParts);
            }

            return content;
        }

        /// <summary>
        /// Converts a message role according to custom conversions
        /// </summary>
        /// <param name="role">Original role</param>
        /// <param name="customConversions">Custom role conversions</param>
        /// <returns>Converted role</returns>
        protected virtual string ConvertRole(MessageRole role, Dictionary<string, string>? customConversions = null)
        {
            string roleString = role.ToString().ToLower();

            if (customConversions != null && customConversions.TryGetValue(roleString, out string? converted))
            {
                return converted;
            }

            return roleString;
        }

        /// <summary>
        /// Converts tools to JSON schema format
        /// </summary>
        /// <param name="tools">Tools to convert</param>
        /// <returns>Tool schemas</returns>
        protected virtual List<Dictionary<string, object>> ConvertToolsToSchema(List<BaseTool> tools)
        {
            List<Dictionary<string, object>> schemas = new List<Dictionary<string, object>>();

            foreach (BaseTool tool in tools)
            {
                // This would need to be implemented based on the specific tool schema format
                // For now, return a basic schema
                schemas.Add(new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = tool.Name,
                        ["description"] = $"Tool: {tool.Name}",
                        ["parameters"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>(),
                            ["required"] = new List<string>()
                        }
                    }
                });
            }

            return schemas;
        }

        /// <summary>
        /// Checks if the model supports the stop parameter
        /// </summary>
        /// <returns>True if stop parameter is supported</returns>
        protected virtual bool SupportsStopParameter()
        {
            if (string.IsNullOrEmpty(ModelId))
                return true;

            string? modelName = ModelId.Split('/').Length > 1 ? ModelId.Split('/')[1] : ModelId;

            // Models that don't support stop parameter (based on Python implementation)
            string unsupportedPattern = @"^(o3[-\d]*|o4-mini[-\d]*|gpt-4.1(-mini|-nano)?[-\d]*)$";
            return !System.Text.RegularExpressions.Regex.IsMatch(modelName, unsupportedPattern);
        }
    }
}