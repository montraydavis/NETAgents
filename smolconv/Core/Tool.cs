using SmolConv.Models;
using SmolConv.Tools;

namespace SmolConv.Core
{
    /// <summary>
    /// Main tool implementation class
    /// </summary>
    public abstract class Tool : BaseTool
    {
        private bool _isInitialized;

        /// <summary>
        /// Gets the tool name
        /// </summary>
        public abstract override string Name { get; }

        /// <summary>
        /// Gets the tool description
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Gets the output type
        /// </summary>
        public abstract string OutputType { get; }

        /// <summary>
        /// Authorized input types
        /// </summary>
        protected static readonly string[] AuthorizedTypes = 
        {
            "string", "boolean", "integer", "number", "image", "audio", "array", "object", "any", "null"
        };

        /// <summary>
        /// Initializes a new instance of the Tool class
        /// </summary>
        protected Tool()
        {
            ValidateToolDefinition();
        }

        /// <summary>
        /// Executes the tool with the provided arguments
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <param name="sanitizeInputsOutputs">Whether to sanitize inputs and outputs</param>
        /// <returns>The result of the tool execution</returns>
        public override object? Call(object?[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            if (!_isInitialized)
            {
                Setup();
            }

            // Enhanced argument conversion logic
            kwargs = ConvertArgumentsToKwargs(args, kwargs);

            // Comprehensive validation
            Dictionary<string, object?> nullableKwargs = kwargs?.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value) ?? new Dictionary<string, object?>();
            Validation.ToolArgumentValidator.ValidateToolArguments(this, nullableKwargs);

            // Sanitize inputs if requested
            if (sanitizeInputsOutputs)
            {
                (object?[] args, Dictionary<string, object> kwargs) handled = AgentTypeMapping.HandleAgentInputTypes(args ?? Array.Empty<object?>(), kwargs ?? new Dictionary<string, object>());
                args = handled.args;
                kwargs = handled.kwargs;
            }

            object? result = Forward(args, kwargs);

            // Sanitize outputs if requested
            if (sanitizeInputsOutputs)
            {
                result = AgentTypeMapping.HandleAgentOutputTypes(result, OutputType);
            }

            return result;
        }

        /// <summary>
        /// Converts arguments to kwargs format with enhanced logic
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>Converted kwargs</returns>
        private Dictionary<string, object> ConvertArgumentsToKwargs(object?[]? args, Dictionary<string, object>? kwargs)
        {
            // Handle single dictionary argument conversion
            if (args?.Length == 1 && (kwargs == null || kwargs.Count == 0) && 
                args[0] is Dictionary<string, object> potentialKwargs)
            {
                if (potentialKwargs.Keys.All(key => Inputs.ContainsKey(key)))
                {
                    return potentialKwargs;
                }
            }

            return kwargs ?? new Dictionary<string, object>();
        }



        public virtual object? InvokeCall(object?[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            return Call(args, kwargs, sanitizeInputsOutputs);
        }

        /// <summary>
        /// The main execution logic for the tool - to be implemented by subclasses
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>The tool execution result</returns>
        protected abstract object? Forward(object?[]? args, Dictionary<string, object>? kwargs);

        /// <summary>
        /// Setup method called before first use - override for expensive initialization
        /// </summary>
        protected virtual void Setup()
        {
            _isInitialized = true;
        }

        /// <summary>
        /// Validates the tool definition
        /// </summary>
        protected virtual void ValidateToolDefinition()
        {
            if (string.IsNullOrEmpty(Name))
                throw new InvalidOperationException("Tool name cannot be null or empty");

            if (string.IsNullOrEmpty(Description))
                throw new InvalidOperationException("Tool description cannot be null or empty");

            if (Inputs == null)
                throw new InvalidOperationException("Tool inputs cannot be null");

            if (string.IsNullOrEmpty(OutputType))
                throw new InvalidOperationException("Tool output type cannot be null or empty");

            if (!AuthorizedTypes.Contains(OutputType))
                throw new InvalidOperationException($"Output type '{OutputType}' is not authorized. Must be one of: {string.Join(", ", AuthorizedTypes)}");

            // Validate inputs
            foreach ((string inputName, Dictionary<string, object> inputSpec) in Inputs)
            {
                if (!inputSpec.ContainsKey("type") || !inputSpec.ContainsKey("description"))
                    throw new InvalidOperationException($"Input '{inputName}' must have 'type' and 'description' keys");

                object inputType = inputSpec["type"];
                if (inputType is string typeStr)
                {
                    if (!AuthorizedTypes.Contains(typeStr))
                        throw new InvalidOperationException($"Input '{inputName}' type '{typeStr}' is not authorized");
                }
                else if (inputType is string[] typeArray)
                {
                    if (typeArray.Any(t => !AuthorizedTypes.Contains(t)))
                        throw new InvalidOperationException($"Input '{inputName}' contains unauthorized types");
                }
                else
                {
                    throw new InvalidOperationException($"Input '{inputName}' type must be string or string array");
                }
            }
        }

        /// <summary>
        /// Validates arguments against the tool's input specification
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>Validation result</returns>
        protected virtual ToolValidationResult ValidateArguments(object[]? args, Dictionary<string, object>? kwargs)
        {
            List<string> errors = new List<string>();

            if (kwargs != null)
            {
                // Check for unknown arguments
                foreach (string key in kwargs.Keys)
                {
                    if (!Inputs.ContainsKey(key))
                        errors.Add($"Unknown argument: {key}");
                }

                // Check required arguments
                foreach ((string inputName, Dictionary<string, object> inputSpec) in Inputs)
                {
                    bool isNullable = inputSpec.TryGetValue("nullable", out object? nullableValue) && 
                                      nullableValue is bool nullable && nullable;

                    if (!kwargs.ContainsKey(inputName) && !isNullable)
                        errors.Add($"Required argument missing: {inputName}");
                }

                // Validate argument types
                foreach ((string key, object value) in kwargs)
                {
                    if (Inputs.TryGetValue(key, out Dictionary<string, object>? inputSpec))
                    {
                        if (!ValidateArgumentType(value, inputSpec))
                            errors.Add($"Argument '{key}' has invalid type");
                    }
                }
            }

            return errors.Count == 0 ? ToolValidationResult.Success : ToolValidationResult.Failure(errors.ToArray());
        }

        /// <summary>
        /// Validates a single argument type
        /// </summary>
        /// <param name="value">The argument value</param>
        /// <param name="inputSpec">The input specification</param>
        /// <returns>True if valid, false otherwise</returns>
        protected virtual bool ValidateArgumentType(object value, Dictionary<string, object> inputSpec)
        {
            if (!inputSpec.TryGetValue("type", out object? typeObj))
                return false;

            if (value == null)
            {
                return inputSpec.TryGetValue("nullable", out object? nullableValue) && 
                       nullableValue is bool nullable && nullable;
            }

            string[] expectedTypes = typeObj switch
            {
                string singleType => new[] { singleType },
                string[] multipleTypes => multipleTypes,
                _ => Array.Empty<string>()
            };

            string actualType = GetJsonSchemaType(value);
            
            return expectedTypes.Contains("any") || 
                   expectedTypes.Contains(actualType) ||
                   (actualType == "integer" && expectedTypes.Contains("number"));
        }

        /// <summary>
        /// Gets the JSON schema type for a value
        /// </summary>
        /// <param name="value">The value to get the type for</param>
        /// <returns>The JSON schema type string</returns>
        protected virtual string GetJsonSchemaType(object value)
        {
            string result = value switch
            {
                null => "null",
                bool => "boolean",
                int or long or short or byte => "integer",
                float or double or decimal => "number",
                string => "string",
                Array or IList<object> => "array",
                Dictionary<string, object> => "object",
                AgentImage => "image",
                AgentAudio => "audio",
                System.Text.Json.JsonElement jsonElement => GetJsonElementSchemaType(jsonElement),
                _ => "object"
            };
            
            return result;
        }

        /// <summary>
        /// Gets the JSON schema type for a JsonElement
        /// </summary>
        /// <param name="jsonElement">The JsonElement to get the type for</param>
        /// <returns>The JSON schema type string</returns>
        private string GetJsonElementSchemaType(System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => "string",
                System.Text.Json.JsonValueKind.Number => jsonElement.TryGetInt64(out _) ? "integer" : "number",
                System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
                System.Text.Json.JsonValueKind.Null => "null",
                System.Text.Json.JsonValueKind.Array => "array",
                System.Text.Json.JsonValueKind.Object => "object",
                _ => "object"
            };
        }

        /// <summary>
        /// Sanitizes input arguments
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>Sanitized arguments</returns>
        protected virtual (object[]?, Dictionary<string, object>?) SanitizeInputs(object[]? args, Dictionary<string, object>? kwargs)
        {
            // Convert AgentType instances to raw values
            object[]? sanitizedArgs = args?.Select(arg => arg is AgentType agentType ? agentType.ToRaw() : arg).ToArray();
            
            Dictionary<string, object>? sanitizedKwargs = kwargs?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value is AgentType agentType ? agentType.ToRaw() : kvp.Value
            );

            return (sanitizedArgs, sanitizedKwargs);
        }

        /// <summary>
        /// Sanitizes output values
        /// </summary>
        /// <param name="output">The output to sanitize</param>
        /// <returns>Sanitized output</returns>
        protected virtual object? SanitizeOutput(object? output)
        {
            return OutputType switch
            {
                "string" when output is string => new AgentText(output),
                "image" when output != null => new AgentImage(output),
                "audio" when output != null => new AgentAudio(output),
                _ => output
            };
        }

        /// <summary>
        /// Converts the tool to a dictionary representation
        /// </summary>
        /// <returns>Dictionary representation</returns>
        public virtual Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["name"] = Name,
                ["description"] = Description,
                ["inputs"] = Inputs,
                ["output_type"] = OutputType
            };
        }

        /// <summary>
        /// Creates a tool instance from a dictionary
        /// </summary>
        /// <param name="toolDict">Tool dictionary</param>
        /// <returns>Tool instance</returns>
        public static Tool FromDict(Dictionary<string, object> toolDict)
        {
            throw new NotImplementedException("FromDict must be implemented by concrete tool classes");
        }
    }
}