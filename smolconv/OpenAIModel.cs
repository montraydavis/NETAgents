using OpenAI.Chat;
using SmolConv.Core;
using SmolConv.Models;
using SmolConv.Tools;
using System.Text.Json;
using ChatMessage = SmolConv.Models.ChatMessage;

namespace SmolConv.Exceptions
{
    public class OpenAIModel : Model
    {
        private readonly ChatClient _client;
        private readonly string _modelId;
        private readonly string _apiKey;

        public OpenAIModel(string modelId, string apiKey) : base()
        {
            _modelId = modelId;
            _apiKey = apiKey;
            _client = new ChatClient(model: modelId, apiKey: apiKey);
            ModelId = modelId;
        }

        public override async Task<ChatMessage> Generate(List<ChatMessage> messages, ModelCompletionOptions? options = null)
        {
            try
            {
                // Convert smolagents messages to OpenAI format
                var openAiMessages = ConvertToOpenAIMessages(messages);
                
                // Prepare completion options
                var completionOptions = new ChatCompletionOptions();
                
                // Add tools if provided
                if (options?.ToolsToCallFrom != null && options.ToolsToCallFrom.Count > 0)
                {
                    foreach (var tool in options.ToolsToCallFrom)
                    {
                        var toolSchema = ConvertToolToSchema(tool);
                        var chatTool = ChatTool.CreateFunctionTool(
                            functionName: tool.Name,
                            // functionDescription: tool.Description ?? "",
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
                            else{
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
                    foreach (var stop in options.StopSequences)
                    {
                        completionOptions.StopSequences.Add(stop);
                    }
                }

                if (options?.ResponseFormat != null)
                {
                    // Handle response format if needed
                    // completionOptions.ResponseFormat = ...
                }

                // Call OpenAI API
                var completion = await _client.CompleteChatAsync(openAiMessages, completionOptions);
                
                // Convert response back to smolagents format
                return ConvertFromOpenAIResponse(completion);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating OpenAI response: {ex.Message}", ex);
            }
        }

        private List<OpenAI.Chat.ChatMessage> ConvertToOpenAIMessages(List<ChatMessage> messages)
        {
            var result = new List<OpenAI.Chat.ChatMessage>();

            foreach (var message in messages)
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
                        var assistantMessage = new AssistantChatMessage(GetMessageContent(message));
                        
                        // Add tool calls if present
                        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                        {
                            foreach (var toolCall in message.ToolCalls)
                            {
                                var arguments = JsonSerializer.Serialize(toolCall.Function.Arguments ?? new Dictionary<string, object>());
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
                        // This assumes the message content contains tool call ID info
                        var toolContent = GetMessageContent(message);
                        
                        // Try to extract tool call ID from content or use a default
                        var toolCallId = ExtractToolCallIdFromContent(toolContent) ?? "unknown";
                        
                        result.Add(new ToolChatMessage(toolCallId, toolContent));
                        break;
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
                var textParts = new List<string>();
                foreach (var item in contentArray)
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
                var startIndex = content.IndexOf("Call id:") + 8;
                var endIndex = content.IndexOf('\n', startIndex);
                if (endIndex == -1) endIndex = content.Length;
                return content.Substring(startIndex, endIndex - startIndex).Trim();
            }
            return null;
        }

        private ChatMessage ConvertFromOpenAIResponse(ChatCompletion completion)
        {
            var content = "";
            if (completion.Content.Count > 0)
            {
                content = completion.Content[0].Text ?? "";
            }
            
            List<ChatMessageToolCall>? toolCalls = null;
            
            // Handle tool calls if present
            if (completion.ToolCalls != null && completion.ToolCalls.Count > 0)
            {
                toolCalls = new List<ChatMessageToolCall>();
                
                foreach (var openAiToolCall in completion.ToolCalls)
                {
                    var arguments = ParseToolCallArguments(openAiToolCall.FunctionArguments.ToString());
                    
                    var fn = new ChatMessageToolCallFunction(openAiToolCall.FunctionName, arguments);

                    toolCalls.Add(new ChatMessageToolCall(fn, openAiToolCall.Id, "function"));
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
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson);
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
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var input in tool.Inputs)
            {
                var property = new Dictionary<string, object>
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
                var isOptional = input.Value.ContainsKey("optional") && (bool)input.Value["optional"];
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
                    var toolCalls = ParseToolCallsFromContent(content);
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
            var toolCalls = new List<ChatMessageToolCall>();
            
            // Look for various tool call patterns in the content
            // This is a fallback parser for models that don't support native tool calling
            
            // Pattern 1: "invoke tool_name: arguments"
            if (content.Contains("invoke ") && content.Contains(":"))
            {
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("invoke "))
                    {
                        var toolCall = ParseInvokePattern(line.Trim());
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
                var jsonToolCalls = ParseJsonToolCalls(content);
                toolCalls.AddRange(jsonToolCalls);
            }

            return toolCalls;
        }

        private ChatMessageToolCall? ParseInvokePattern(string line)
        {
            try
            {
                // Parse "invoke final_answer: 'Hello, how are you?'"
                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1) return null;
                
                var toolPart = line.Substring(7, colonIndex - 7).Trim(); // Remove "invoke "
                var argsPart = line.Substring(colonIndex + 1).Trim();
                
                // Simple argument parsing - handle quoted strings
                var arguments = new Dictionary<string, object>();
                
                if (argsPart.StartsWith("'") && argsPart.EndsWith("'"))
                {
                    // Single quoted string
                    var value = argsPart.Substring(1, argsPart.Length - 2);
                    arguments[GetDefaultArgumentName(toolPart)] = value;
                }
                else if (argsPart.StartsWith("\"") && argsPart.EndsWith("\""))
                {
                    // Double quoted string
                    var value = argsPart.Substring(1, argsPart.Length - 2);
                    arguments[GetDefaultArgumentName(toolPart)] = value;
                }
                else
                {
                    // Try to parse as JSON or treat as raw value
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(argsPart);
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

        private List<ChatMessageToolCall> ParseJsonToolCalls(string content)
        {
            var toolCalls = new List<ChatMessageToolCall>();
            
            try
            {
                // Try to find and parse JSON structures that look like tool calls
                var jsonStart = content.IndexOf('{');
                var jsonEnd = content.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonContent = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    
                    // Try to parse as a single tool call or array of tool calls
                    try
                    {
                        if (jsonContent.TrimStart().StartsWith('['))
                        {
                            var array = JsonSerializer.Deserialize<object[]>(jsonContent);
                            // Parse array of tool calls
                        }
                        else
                        {
                            var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
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
            var result = base.ToDict();
            result["model_type"] = "OpenAIModel";
            result["api_key"] = "[REDACTED]"; // Don't expose API key
            return result;
        }
    }
}