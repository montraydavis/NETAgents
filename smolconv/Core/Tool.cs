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
        /// Gets the input specifications
        /// </summary>
        public abstract Dictionary<string, Dictionary<string, object>> Inputs { get; }

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
        public override object? Call(object[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            if (!_isInitialized)
            {
                Setup();
            }

            // Handle case where args contains a single dictionary that should be kwargs
            if (args?.Length == 1 && (kwargs == null || kwargs.Count == 0) && args[0] is Dictionary<string, object> potentialKwargs)
            {
                // Check if all dictionary keys match our input parameters
                if (potentialKwargs.Keys.All(key => Inputs.ContainsKey(key)))
                {
                    args = null;
                    kwargs = potentialKwargs;
                }
            }

            // Validate arguments
            var validation = ValidateArguments(args, kwargs);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Tool validation failed: {string.Join(", ", validation.Errors)}");
            }

            // Sanitize inputs if requested
            if (sanitizeInputsOutputs)
            {
                (args, kwargs) = AgentTypeMapping.HandleAgentInputTypes(args, kwargs);
            }

            // Execute the tool
            var result = Forward(args, kwargs);

            // Sanitize outputs if requested
            if (sanitizeInputsOutputs)
            {
                result = AgentTypeMapping.HandleAgentOutputTypes(result, OutputType);
            }

            return result;
        }



        public virtual object? InvokeCall(object[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false)
        {
            return Call(args, kwargs, sanitizeInputsOutputs);
        }

        /// <summary>
        /// The main execution logic for the tool - to be implemented by subclasses
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>The tool execution result</returns>
        protected abstract object? Forward(object[]? args, Dictionary<string, object>? kwargs);

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
            foreach (var (inputName, inputSpec) in Inputs)
            {
                if (!inputSpec.ContainsKey("type") || !inputSpec.ContainsKey("description"))
                    throw new InvalidOperationException($"Input '{inputName}' must have 'type' and 'description' keys");

                var inputType = inputSpec["type"];
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
            var errors = new List<string>();

            if (kwargs != null)
            {
                // Check for unknown arguments
                foreach (var key in kwargs.Keys)
                {
                    if (!Inputs.ContainsKey(key))
                        errors.Add($"Unknown argument: {key}");
                }

                // Check required arguments
                foreach (var (inputName, inputSpec) in Inputs)
                {
                    var isNullable = inputSpec.TryGetValue("nullable", out var nullableValue) && 
                                   nullableValue is bool nullable && nullable;

                    if (!kwargs.ContainsKey(inputName) && !isNullable)
                        errors.Add($"Required argument missing: {inputName}");
                }

                // Validate argument types
                foreach (var (key, value) in kwargs)
                {
                    if (Inputs.TryGetValue(key, out var inputSpec))
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
            if (!inputSpec.TryGetValue("type", out var typeObj))
                return false;

            if (value == null)
            {
                return inputSpec.TryGetValue("nullable", out var nullableValue) && 
                       nullableValue is bool nullable && nullable;
            }

            var expectedTypes = typeObj switch
            {
                string singleType => new[] { singleType },
                string[] multipleTypes => multipleTypes,
                _ => Array.Empty<string>()
            };

            var actualType = GetJsonSchemaType(value);
            
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
            return value switch
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