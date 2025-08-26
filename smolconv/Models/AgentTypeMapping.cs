using System.Text.Json;

namespace SmolConv.Models
{
    // ===============================
    // AGENT TYPE MAPPING & UTILITIES
    // ===============================

    public static class AgentTypeMapping
    {
        private static readonly Dictionary<string, Type> _agentTypeMapping = new()
        {
            { "string", typeof(AgentText) },
            { "image", typeof(AgentImage) },
            { "audio", typeof(AgentAudio) }
        };

        public static AgentType? HandleAgentOutputTypes(object? output, string? outputType = null)
        {
            if (output == null)
                return null;

            // Handle "object" output type - serialize complex objects into AgentText
            if (outputType == "object")
            {
                try
                {
                    // For Dictionary and other complex objects, serialize to JSON
                    if (output is Dictionary<string, object> dict)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var kvp in dict)
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
                        return new AgentText(sb.ToString());
                    }

                    // For other complex objects, use JSON serialization
                    string jsonOutput = JsonSerializer.Serialize(output, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    return new AgentText(jsonOutput);
                }
                catch
                {
                    // Fallback to string representation
                    return new AgentText(output.ToString() ?? "Complex object");
                }
            }

            // If the class has defined outputs, we can map directly according to the class definition
            if (!string.IsNullOrEmpty(outputType) && _agentTypeMapping.ContainsKey(outputType))
            {
                Type agentType = _agentTypeMapping[outputType];
                return (AgentType?)Activator.CreateInstance(agentType, output);
            }

            // If the class does not have defined output, then we map according to the type
            if (output is string str)
                return new AgentText(str);

            // Note: For cross-platform compatibility, we don't check for System.Drawing.Image
            // Instead, we'll handle byte arrays as potential images
            if (output is byte[] bytes)
            {
                return new AgentImage(bytes);
            }

            // Note: Tensor handling would require ML.NET or similar library
            // For now, we'll handle basic numeric arrays as potential tensors
            if (output is Array array && array.Rank > 0)
            {
                // This is a simplified tensor detection - in practice you'd want proper tensor types
                return new AgentAudio(output);
            }

            // For any other unhandled type, convert to AgentText
            return new AgentText(output.ToString() ?? "Unknown object");
        }

        public static (object?[] args, Dictionary<string, object?> kwargs) HandleAgentInputTypes(params object?[] args)
        {
            object?[] processedArgs = args.Select(arg => arg is AgentType agentType ? agentType.ToRaw() : arg).ToArray();
            return (processedArgs, new Dictionary<string, object?>());
        }

        public static (object?[] args, Dictionary<string, object> kwargs) HandleAgentInputTypes(
            object?[] args,
            Dictionary<string, object> kwargs)
        {
            if (kwargs == null)
            {
                return (args ?? Array.Empty<object>(), new Dictionary<string, object>());
            }

            object?[] processedArgs = args?.Select(arg => arg is AgentType agentType ? agentType.ToRaw() : arg).ToArray() ?? Array.Empty<object>();
            Dictionary<string, object> processedKwargs = kwargs.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value is AgentType agentType ? agentType.ToRaw() : kvp.Value);

            return (processedArgs, processedKwargs);
        }
    }
}
