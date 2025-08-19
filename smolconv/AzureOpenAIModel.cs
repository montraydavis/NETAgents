using Azure.AI.OpenAI;
using OpenAI.Chat;
using SmolConv.Models;
using SmolConv.Tools;
using System.Text.Json;
using ChatMessage = SmolConv.Models.ChatMessage;
using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

namespace SmolConv.Inference
{
    public class AzureOpenAIModel : Model
    {
        private readonly ChatClient _chatClient;
        private readonly string _modelId;
        private readonly string _endpoint;
        private readonly string _cacheDirectory;
        private readonly string _cacheSalt;

        public AzureOpenAIModel(string modelId, string endpoint, string apiKey) : base(modelId: modelId)
        {
            _modelId = modelId;
            _endpoint = endpoint;

            // Create Azure OpenAI client with API key authentication
            var azureClient = new AzureOpenAIClient(
                new Uri(endpoint),
                new ApiKeyCredential(apiKey)
            );

            // Get chat client for the specific deployment
            _chatClient = azureClient.GetChatClient(_modelId);

            // Initialize cache directory and salt
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "smolconv",
                ".cache"
            );
            _cacheSalt = $"{_modelId}_{endpoint}"; // Use model and endpoint as salt
            
            // Ensure cache directory exists
            Directory.CreateDirectory(_cacheDirectory);
        }

        public override async Task<ChatMessage> GenerateAsync(List<ChatMessage> messages, ModelCompletionOptions? options = null,
                                                       CancellationToken cancellationToken = default)
        {
            try
            {
                // Generate cache key
                string cacheKey = GenerateCacheKey(messages, options);
                string cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.json");

                // Try to load from cache first
                ChatMessage? cachedResponse = LoadFromCache(cacheFilePath);
                if (cachedResponse != null)
                {
                    Debug.WriteLine($"Loaded from cache: {cacheFilePath}");
                    return cachedResponse;
                }

                // Convert smolagents messages to Azure OpenAI format
                List<OpenAI.Chat.ChatMessage> azureMessages = ConvertToAzureOpenAIMessages(messages);

                // Prepare completion options
                ChatCompletionOptions completionOptions = new ChatCompletionOptions();

                // Add tools if provided
                if (options?.ToolsToCallFrom != null && options.ToolsToCallFrom.Count > 0)
                {
                    foreach (BaseTool tool in options.ToolsToCallFrom)
                    {
                        Dictionary<string, object> toolSchema = ConvertToolToSchema(tool);
                        ChatTool? chatTool = ChatTool.CreateFunctionTool(
                            functionName: tool.Name,
                            functionParameters: BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(toolSchema))
                        );
                        completionOptions.Tools.Add(chatTool);
                    }

                    // Set tool choice if specified
                    if (!string.IsNullOrEmpty(options.ToolChoice?.ToString() ?? null))
                    {
                        if (options.ToolChoice is string optionsToolChoice)
                        {
                            if (optionsToolChoice == "auto")
                            {
                                completionOptions.ToolChoice = ChatToolChoice.CreateAutoChoice();
                            }
                            else if (optionsToolChoice == "none")
                            {
                                completionOptions.ToolChoice = ChatToolChoice.CreateNoneChoice();
                            }
                            else
                            {
                                completionOptions.ToolChoice = ChatToolChoice.CreateFunctionChoice(optionsToolChoice);
                            }
                        }
                    }
                    else
                    {
                        completionOptions.ToolChoice = ChatToolChoice.CreateAutoChoice();
                    }
                }

                // Add other options
                if (options?.StopSequences != null && options.StopSequences.Count > 0)
                {
                    foreach (string stop in options.StopSequences)
                    {
                        completionOptions.StopSequences.Add(stop);
                    }
                }

                if (options?.ResponseFormat != null)
                {
                    // Handle response format if needed
                    // completionOptions.ResponseFormat = ...
                }

                // Call Azure OpenAI API
                ClientResult<ChatCompletion>? completion = await _chatClient.CompleteChatAsync(azureMessages, completionOptions, cancellationToken);

                // Convert response back to smolagents format
                ChatMessage response = ConvertFromAzureOpenAIResponse(completion);

                // Save to cache
                SaveToCache(cacheFilePath, response);

                return response;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating Azure OpenAI response: {ex.Message}", ex);
            }
        }

        public override async Task<ChatMessage> Generate(List<ChatMessage> messages, ModelCompletionOptions? options = null)
        {
            return await GenerateAsync(messages, options);
        }

        public string GenerateCacheKey(List<ChatMessage> messages, ModelCompletionOptions? options)
        {
            // Create a deterministic string representation of the request
            var requestData = new
            {
                ModelId = _modelId,
                Messages = messages.Select(m => new
                {
                    Role = m.Role.ToString(),
                    Content = GetContentAsString(m.Content) ?? m.ContentString ?? "",
                    ToolCalls = m.ToolCalls?.Select(tc => new
                    {
                        Id = tc.Id,
                        Type = tc.Type,
                        Function = new
                        {
                            Name = tc.Function.Name,
                            Arguments = tc.Function.Arguments
                        }
                    }).ToList()
                }).ToList(),
                Options = options != null ? new
                {
                    StopSequences = options.StopSequences,
                    ResponseFormat = options.ResponseFormat,
                    ToolChoice = options.ToolChoice?.ToString(),
                    ToolsToCallFrom = options.ToolsToCallFrom?.Select(t => t.Name).ToList()
                } : null
            };

            string jsonString = JsonSerializer.Serialize(requestData, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Combine with salt and generate SHA256 hash
            string saltedData = $"{_cacheSalt}:{jsonString}";
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedData));
            return Convert.ToHexString(hashBytes).ToLower();
        }

        private ChatMessage? LoadFromCache(string cacheFilePath)
        {
            try
            {
                if (!File.Exists(cacheFilePath))
                    return null;

                string jsonContent = File.ReadAllText(cacheFilePath);
                CacheData? cacheData = JsonSerializer.Deserialize<CacheData>(jsonContent);

                if (cacheData == null)
                    return null;

                // Check if cache is still valid (optional: add TTL logic here)
                if (cacheData.ExpiresAt.HasValue && DateTime.UtcNow > cacheData.ExpiresAt.Value)
                {
                    File.Delete(cacheFilePath);
                    return null;
                }

                // Reconstruct ChatMessage from cache
                List<ChatMessageToolCall>? toolCalls = cacheData.ToolCalls?.Select(tc => new ChatMessageToolCall(
                    new ChatMessageToolCallFunction(tc.Function.Name, tc.Function.Arguments ?? new Dictionary<string, object>()),
                    tc.Id,
                    tc.Type
                )).ToList();

                TokenUsage? tokenUsage = cacheData.TokenUsage != null 
                    ? new TokenUsage(cacheData.TokenUsage.InputTokens, cacheData.TokenUsage.OutputTokens)
                    : null;

                return new ChatMessage(cacheData.Role, cacheData.Content, cacheData.Content, toolCalls)
                {
                    TokenUsage = tokenUsage
                };
            }
            catch (Exception ex)
            {
                // Log cache loading error and continue without cache
                Console.WriteLine($"Failed to load from cache: {ex.Message}");
                return null;
            }
        }

        private void SaveToCache(string cacheFilePath, ChatMessage response)
        {
            try
            {
                CacheData cacheData = new CacheData
                {
                    Role = response.Role,
                    Content = GetContentAsString(response.Content) ?? response.ContentString ?? "",
                    ToolCalls = response.ToolCalls?.Select(tc => new CachedToolCall
                    {
                        Id = tc.Id,
                        Type = tc.Type,
                        Function = new CachedToolCallFunction
                        {
                            Name = tc.Function.Name,
                            Arguments = tc.Function.Arguments
                        }
                    }).ToList(),
                    TokenUsage = response.TokenUsage != null ? new CachedTokenUsage
                    {
                        InputTokens = response.TokenUsage.InputTokens,
                        OutputTokens = response.TokenUsage.OutputTokens
                    } : null,
                    CachedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7) // Cache for 7 days by default
                };

                string jsonContent = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(cacheFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                // Log cache saving error but don't fail the request
                Console.WriteLine($"Failed to save to cache: {ex.Message}");
            }
        }

        // Cache data classes for JSON serialization
        private class CacheData
        {
            public MessageRole Role { get; set; }
            public string Content { get; set; }
            public List<CachedToolCall>? ToolCalls { get; set; }
            public CachedTokenUsage? TokenUsage { get; set; }
            public DateTime CachedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }

        private class CachedToolCall
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
            public CachedToolCallFunction Function { get; set; } = new();
        }

        private class CachedToolCallFunction
        {
            public string Name { get; set; } = "";
            public object? Arguments { get; set; }
        }

        private class CachedTokenUsage
        {
            public int InputTokens { get; set; }
            public int OutputTokens { get; set; }
        }

        private static string? GetContentAsString(object? content)
        {
            switch (content)
            {
                case null:
                    return null;
                // If it's AgentText, get the raw value
                case SmolConv.Models.AgentText agentText:
                {
                    object? rawValue = agentText.ToRaw();
                    return rawValue?.ToString();
                }
                default:
                    // For other types, use ToString()
                    return content.ToString();
            }
        }

        private List<OpenAI.Chat.ChatMessage> ConvertToAzureOpenAIMessages(List<ChatMessage> messages)
        {
            List<OpenAI.Chat.ChatMessage> result = new List<OpenAI.Chat.ChatMessage>();

            foreach (ChatMessage message in messages)
            {
                switch (message.Role)
                {
                    case MessageRole.System:
                        result.Add(new SystemChatMessage(GetMessageContent(message)));
                        break;

                    case MessageRole.User:
                        result.Add(new UserChatMessage(GetMessageContent(message)));
                        break;

                    case MessageRole.Assistant:
                        AssistantChatMessage assistantMessage = new AssistantChatMessage(GetMessageContent(message));

                        // Add tool calls if present
                        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                        {
                            foreach (ChatMessageToolCall toolCall in message.ToolCalls)
                            {
                                string arguments = JsonSerializer.Serialize(toolCall.Function.Arguments ?? new Dictionary<string, object>());
                                assistantMessage.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                                    toolCall.Id,
                                    toolCall.Function.Name,
                                    BinaryData.FromString(arguments)
                                ));
                            }
                        }

                        result.Add(assistantMessage);
                        break;

                    case MessageRole.ToolCall:
                    case MessageRole.ToolResponse:
                        // For tool response messages, we need the tool call ID
                        string toolContent = GetMessageContent(message);
                        string toolCallId = ExtractToolCallIdFromContent(toolContent) ?? "unknown";

                        result.Add(new ToolChatMessage(toolCallId, toolContent));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return result;
        }

        private string GetMessageContent(ChatMessage message)
        {
            if (message.Content is string content)
                return content;

            if (!string.IsNullOrEmpty(message.ContentString))
                return message.ContentString;

            // Handle complex content structures
            if (message.Content is object[] contentArray)
            {
                List<string> textParts = new List<string>();
                foreach (object item in contentArray)
                {
                    if (item is Dictionary<string, object> dict && dict.ContainsKey("text"))
                    {
                        textParts.Add(dict["text"].ToString() ?? "");
                    }
                }
                return string.Join(" ", textParts);
            }

            return JsonSerializer.Serialize(message.Content);
        }

        private string? ExtractToolCallIdFromContent(string content)
        {
            // Try to extract tool call ID from content
            // This is a simplified implementation
            if (content.Contains("Call id:"))
            {
                int startIndex = content.IndexOf("Call id:", StringComparison.Ordinal) + 8;
                int endIndex = content.IndexOf('\n', startIndex);
                if (endIndex == -1) endIndex = content.Length;
                return content.Substring(startIndex, endIndex - startIndex).Trim();
            }
            return null;
        }

        private ChatMessage ConvertFromAzureOpenAIResponse(ChatCompletion completion)
        {
            string content = "";
            if (completion.Content.Count > 0)
            {
                content = completion.Content[0].Text ?? "";
            }

            List<ChatMessageToolCall>? toolCalls = null;

            // Handle tool calls if present
            if (completion.ToolCalls != null && completion.ToolCalls.Count > 0)
            {
                toolCalls = [];

                foreach (ChatToolCall? azureToolCall in completion.ToolCalls)
                {
                    object arguments = ParseToolCallArguments(azureToolCall.FunctionArguments.ToString());

                    ChatMessageToolCallFunction fn = new ChatMessageToolCallFunction(azureToolCall.FunctionName, arguments);

                    toolCalls.Add(new ChatMessageToolCall(fn, azureToolCall.Id, "function"));
                }
            }

            // Set token usage if available
            TokenUsage? tokenUsage = null;
            if (completion.Usage != null)
            {
                tokenUsage = new TokenUsage(completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount);
            }

            return new ChatMessage(MessageRole.Assistant, content, content, toolCalls)
            {
                TokenUsage = tokenUsage
            };
        }

        private object ParseToolCallArguments(string? argumentsJson)
        {
            if (string.IsNullOrEmpty(argumentsJson))
                return new Dictionary<string, object>();

            try
            {
                Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
                return parsed ?? new Dictionary<string, object>();
            }
            catch (JsonException)
            {
                // If it's not valid JSON, wrap it in a simple structure
                return new Dictionary<string, object> { ["value"] = argumentsJson };
            }
        }

        private Dictionary<string, object> ConvertToolToSchema(BaseTool tool)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            List<string> required = new List<string>();

            foreach (KeyValuePair<string, Dictionary<string, object>> input in tool.Inputs)
            {
                Dictionary<string, object> property = new Dictionary<string, object>
                {
                    ["type"] = input.Value.GetValueOrDefault("type", "string"),
                    ["description"] = input.Value.GetValueOrDefault("description", "")
                };

                // Handle additional schema properties
                if (input.Value.ContainsKey("enum"))
                {
                    property["enum"] = input.Value["enum"];
                }

                properties[input.Key] = property;

                // Add to required if not optional
                bool isOptional = input.Value.ContainsKey("optional") && (bool)input.Value["optional"];
                if (!isOptional)
                {
                    required.Add(input.Key);
                }
            }

            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };
        }

        public override ChatMessage ParseToolCalls(ChatMessage message)
        {
            // If the message already has tool calls, return as is
            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                return message;
            }

            // Try to parse tool calls from the content if it's a string
            if (message.Content is string content)
            {
                try
                {
                    List<ChatMessageToolCall> toolCalls = ParseToolCallsFromContent(content);
                    if (toolCalls.Count > 0)
                    {
                        return new ChatMessage(message.Role, message.Content, message.ContentString, toolCalls);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse tool calls: {ex.Message}");
                }
            }

            return message;
        }

        private List<ChatMessageToolCall> ParseToolCallsFromContent(string content)
        {
            List<ChatMessageToolCall> toolCalls = new List<ChatMessageToolCall>();

            // Look for various tool call patterns in the content
            // This is a fallback parser for models that don't support native tool calling

            // Pattern 1: "invoke tool_name: arguments"
            if (content.Contains("invoke ") && content.Contains(":"))
            {
                string[] lines = content.Split('\n');
                foreach (string line in lines)
                {
                    if (line.Trim().StartsWith("invoke "))
                    {
                        ChatMessageToolCall? toolCall = ParseInvokePattern(line.Trim());
                        if (toolCall != null)
                        {
                            toolCalls.Add(toolCall);
                        }
                    }
                }
            }

            // Pattern 2: JSON tool call format
            if (content.Contains("\"type\": \"function\"") || content.Contains("tool_calls"))
            {
                List<ChatMessageToolCall> jsonToolCalls = ParseJsonToolCalls(content);
                toolCalls.AddRange(jsonToolCalls);
            }

            return toolCalls;
        }

        private ChatMessageToolCall? ParseInvokePattern(string line)
        {
            try
            {
                // Parse "invoke final_answer: 'Hello, how are you?'"
                int colonIndex = line.IndexOf(':');
                if (colonIndex == -1) return null;

                string toolPart = line.Substring(7, colonIndex - 7).Trim(); // Remove "invoke "
                string argsPart = line.Substring(colonIndex + 1).Trim();

                // Simple argument parsing - handle quoted strings
                Dictionary<string, object> arguments = new Dictionary<string, object>();

                if (argsPart.StartsWith("'") && argsPart.EndsWith("'"))
                {
                    // Single quoted string
                    string value = argsPart.Substring(1, argsPart.Length - 2);
                    arguments[GetDefaultArgumentName(toolPart)] = value;
                }
                else if (argsPart.StartsWith("\"") && argsPart.EndsWith("\""))
                {
                    // Double quoted string
                    string value = argsPart.Substring(1, argsPart.Length - 2);
                    arguments[GetDefaultArgumentName(toolPart)] = value;
                }
                else
                {
                    // Try to parse as JSON or treat as raw value
                    try
                    {
                        Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(argsPart);
                        arguments = parsed ?? arguments;
                    }
                    catch
                    {
                        arguments[GetDefaultArgumentName(toolPart)] = argsPart;
                    }
                }

                return new ChatMessageToolCall(new ChatMessageToolCallFunction(toolPart, arguments), Guid.NewGuid().ToString(), "function");
            }
            catch
            {
                return null;
            }
        }

        private string GetDefaultArgumentName(string toolName)
        {
            // Map common tool names to their expected argument names
            return toolName switch
            {
                "final_answer" => "answer",
                "search" => "query",
                "calculator" => "expression",
                _ => "input"
            };
        }

        private static List<ChatMessageToolCall> ParseJsonToolCalls(string content)
        {
            List<ChatMessageToolCall> toolCalls = new List<ChatMessageToolCall>();

            try
            {
                // Try to find and parse JSON structures that look like tool calls
                int jsonStart = content.IndexOf('{');
                int jsonEnd = content.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    string jsonContent = content.Substring(jsonStart, jsonEnd - jsonStart + 1);

                    // Try to parse as a single tool call or array of tool calls
                    try
                    {
                        if (jsonContent.TrimStart().StartsWith('['))
                        {
                            object[]? array = JsonSerializer.Deserialize<object[]>(jsonContent);
                            // Parse array of tool calls
                        }
                        else
                        {
                            Dictionary<string, object>? obj = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                            // Parse single tool call
                        }
                    }
                    catch
                    {
                        // JSON parsing failed, ignore
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return toolCalls;
        }

        public override Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> result = base.ToDict();
            result["model_type"] = "AzureOpenAIModel";
            result["endpoint"] = _endpoint;
            result["api_key"] = "[REDACTED]"; // Don't expose API key
            result["cache_directory"] = _cacheDirectory;
            result["cache_enabled"] = true;
            return result;
        }

        /// <summary>
        /// Clears all cached responses for this model
        /// </summary>
        public void ClearCache()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return;
                
                string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                foreach (string file in cacheFiles)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to clear cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        /// <returns>Cache statistics including file count and total size</returns>
        public Dictionary<string, object> GetCacheStats()
        {
            Dictionary<string, object> stats = new Dictionary<string, object>();
            
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                    long totalSize = cacheFiles.Sum(file => new FileInfo(file).Length);
                    
                    stats["file_count"] = cacheFiles.Length;
                    stats["total_size_bytes"] = totalSize;
                    stats["total_size_mb"] = Math.Round(totalSize / (1024.0 * 1024.0), 2);
                    stats["cache_directory"] = _cacheDirectory;
                }
                else
                {
                    stats["file_count"] = 0;
                    stats["total_size_bytes"] = 0;
                    stats["total_size_mb"] = 0.0;
                    stats["cache_directory"] = _cacheDirectory;
                }
            }
            catch (Exception ex)
            {
                stats["error"] = ex.Message;
            }
            
            return stats;
        }

        /// <summary>
        /// Removes expired cache entries
        /// </summary>
        public void CleanupExpiredCache()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                    return;

                string[] cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                int removedCount = 0;

                foreach (string file in cacheFiles)
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(file);
                        CacheData? cacheData = JsonSerializer.Deserialize<CacheData>(jsonContent);

                        if (cacheData?.ExpiresAt.HasValue == true && DateTime.UtcNow > cacheData.ExpiresAt.Value)
                        {
                            File.Delete(file);
                            removedCount++;
                        }
                    }
                    catch
                    {
                        // If we can't read the file, delete it as it might be corrupted
                        File.Delete(file);
                        removedCount++;
                    }
                }

                Console.WriteLine($"Cleaned up {removedCount} expired cache entries");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cleanup expired cache: {ex.Message}");
            }
        }
    }
}
