using System.Text.Json;
using NETAgents.Models;
using NETAgents.Core.TypeSystem;
using NETAgents.Tools;

namespace NETAgents.Core.Validation
{
    /// <summary>
    /// Validates tool arguments against JSON schema specifications
    /// </summary>
    public static class ToolArgumentValidator
    {
        private static readonly Dictionary<Type, string> TypeMapping = new()
        {
            { typeof(string), "string" },
            { typeof(int), "integer" },
            { typeof(long), "integer" },
            { typeof(short), "integer" },
            { typeof(byte), "integer" },
            { typeof(float), "number" },
            { typeof(double), "number" },
            { typeof(decimal), "number" },
            { typeof(bool), "boolean" },
            { typeof(object[]), "array" },
            { typeof(List<object>), "array" },
            { typeof(Dictionary<string, object>), "object" },
            { typeof(AgentImage), "image" },
            { typeof(AgentAudio), "audio" },
            { typeof(AgentText), "string" }
        };

        /// <summary>
        /// Validates tool arguments against the tool's input schema
        /// </summary>
        /// <param name="tool">The tool to validate arguments for</param>
        /// <param name="arguments">The arguments to validate</param>
        public static void ValidateToolArguments(BaseTool tool, Dictionary<string, object?> arguments)
        {
            if (!(tool is Tool t)) return;

            // 1. Check for unknown arguments
            foreach (string key in arguments.Keys)
            {
                if (!t.Inputs.ContainsKey(key))
                    throw new ArgumentException($"Argument '{key}' is not in the tool's input schema");
            }

            // 2. Validate nullable parameters
            NullableParameterHandler.ValidateNullableParameters(arguments, t.Inputs);

            // 3. Validate each argument against schema
            foreach ((string key, Dictionary<string, object> inputSchema) in t.Inputs)
            {
                ValidateArgument(key, arguments, inputSchema);
            }

            // 4. Check for missing required arguments
            ValidateRequiredArguments(arguments, t.Inputs);
        }

        /// <summary>
        /// Validates a single argument against its schema
        /// </summary>
        /// <param name="key">The argument key</param>
        /// <param name="arguments">The arguments dictionary</param>
        /// <param name="inputSchema">The input schema for this argument</param>
        private static void ValidateArgument(string key, Dictionary<string, object?> arguments, 
                                           Dictionary<string, object> inputSchema)
        {
            bool isNullable = inputSchema.ContainsKey("nullable") && (bool)inputSchema["nullable"];
            string? expectedType = inputSchema.TryGetValue("type", out object? typeVal) ? typeVal?.ToString() : null;

            if (!arguments.TryGetValue(key, out object? value))
            {
                if (!isNullable)
                    throw new ArgumentException($"Required argument '{key}' is missing");
                return;
            }

            if (value == null && !isNullable)
                throw new ArgumentException($"Argument '{key}' cannot be null");

            if (value != null && expectedType != null)
            {
                string actualType = GetJsonSchemaType(value.GetType());
                if (!IsValidType(actualType, expectedType, isNullable))
                {
                    // Try type coercion before failing
                    if (!TypeCoercion.TryCoerceValue(value, expectedType, out object coercedValue))
                    {
                        throw new ArgumentException(
                            $"Argument '{key}' has type '{actualType}' but should be '{expectedType}'");
                    }
                    // Update the arguments with coerced value
                    arguments[key] = coercedValue;
                }
            }
        }



        /// <summary>
        /// Gets the JSON schema type for a .NET type
        /// </summary>
        /// <param name="type">The .NET type</param>
        /// <returns>The JSON schema type string</returns>
        private static string GetJsonSchemaType(Type type)
        {
            if (TypeMapping.TryGetValue(type, out string? jsonType))
                return jsonType;

            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return "array";

            if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
                return "object";

            // Handle JsonElement types
            if (type == typeof(JsonElement))
                return "any";

            return "any";
        }

        /// <summary>
        /// Checks if an actual type is valid for an expected type
        /// </summary>
        /// <param name="actualType">The actual type</param>
        /// <param name="expectedType">The expected type</param>
        /// <param name="isNullable">Whether the parameter is nullable</param>
        /// <returns>True if the types are compatible</returns>
        private static bool IsValidType(string actualType, string expectedType, bool isNullable)
        {
            if (actualType == "any" || expectedType == "any")
                return true;

            if (actualType == expectedType)
                return true;

            // Handle number/integer compatibility
            if ((actualType == "integer" && expectedType == "number") ||
                (actualType == "number" && expectedType == "integer"))
                return true;

            return false;
        }

        /// <summary>
        /// Validates that all required arguments are present
        /// </summary>
        /// <param name="arguments">The provided arguments</param>
        /// <param name="inputs">The tool's input schema</param>
        private static void ValidateRequiredArguments(Dictionary<string, object?> arguments, 
                                                    Dictionary<string, Dictionary<string, object>> inputs)
        {
            foreach ((string key, Dictionary<string, object> schema) in inputs)
            {
                bool isNullable = schema.ContainsKey("nullable") && (bool)schema["nullable"];
                if (!arguments.ContainsKey(key) && !isNullable)
                    throw new ArgumentException($"Required argument '{key}' is missing");
            }
        }
    }
}
