namespace NETAgents.Core.Validation
{
    /// <summary>
    /// Handles nullable parameter validation for tool arguments
    /// </summary>
    public static class NullableParameterHandler
    {
        /// <summary>
        /// Checks if a parameter is nullable based on its schema
        /// </summary>
        /// <param name="inputSchema">The input schema for the parameter</param>
        /// <returns>True if the parameter is nullable</returns>
        public static bool IsNullable(Dictionary<string, object> inputSchema)
        {
            return inputSchema.ContainsKey("nullable") && 
                   inputSchema["nullable"] is bool nullable && 
                   nullable;
        }

        /// <summary>
        /// Checks if a parameter is optional based on its schema
        /// </summary>
        /// <param name="inputSchema">The input schema for the parameter</param>
        /// <returns>True if the parameter is optional</returns>
        public static bool IsOptional(Dictionary<string, object> inputSchema)
        {
            return inputSchema.ContainsKey("optional") && 
                   inputSchema["optional"] is bool optional && 
                   optional;
        }

        /// <summary>
        /// Validates nullable parameters against the provided arguments
        /// </summary>
        /// <param name="arguments">The arguments to validate</param>
        /// <param name="inputs">The tool's input schema</param>
        public static void ValidateNullableParameters(Dictionary<string, object?> arguments, 
                                                    Dictionary<string, Dictionary<string, object>> inputs)
        {
            foreach ((string paramName, Dictionary<string, object> schema) in inputs)
            {
                bool isNullable = IsNullable(schema);
                bool isOptional = IsOptional(schema);

                if (arguments.TryGetValue(paramName, out object? value))
                {
                    if (value == null && !isNullable)
                    {
                        throw new ArgumentNullException(paramName, 
                            $"Parameter '{paramName}' cannot be null");
                    }
                }
                else if (!isOptional && !isNullable)
                {
                    throw new ArgumentException($"Required parameter '{paramName}' is missing");
                }
            }
        }
    }
}
