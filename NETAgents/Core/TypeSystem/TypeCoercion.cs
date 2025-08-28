using System.Text.Json;

namespace NETAgents.Core.TypeSystem
{
    /// <summary>
    /// Provides advanced type coercion capabilities for tool arguments
    /// </summary>
    public static class TypeCoercion
    {
        /// <summary>
        /// Attempts to coerce a value to the target type
        /// </summary>
        /// <param name="value">The value to coerce</param>
        /// <param name="targetType">The target type</param>
        /// <param name="coercedValue">The coerced value if successful</param>
        /// <returns>True if coercion was successful, false otherwise</returns>
        public static bool TryCoerceValue(object value, string targetType, out object coercedValue)
        {
            coercedValue = value;
            
            try
            {
                switch (targetType.ToLower())
                {
                    case "number":
                        return TryCoerceToNumber(value, out coercedValue);
                    case "integer":
                        return TryCoerceToInteger(value, out coercedValue);
                    case "string":
                        coercedValue = value?.ToString() ?? "";
                        return true;
                    case "boolean":
                        return TryCoerceToBoolean(value, out coercedValue);
                    case "array":
                        return TryCoerceToArray(value, out coercedValue);
                    case "object":
                        return TryCoerceToObject(value, out coercedValue);
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to coerce a value to a number
        /// </summary>
        /// <param name="value">The value to coerce</param>
        /// <param name="result">The coerced number</param>
        /// <returns>True if coercion was successful</returns>
        private static bool TryCoerceToNumber(object value, out object result)
        {
            result = value;
            
            return value switch
            {
                int i => SetResult(out result, (double)i),
                long l => SetResult(out result, (double)l),
                short s => SetResult(out result, (double)s),
                byte b => SetResult(out result, (double)b),
                float f => SetResult(out result, (double)f),
                decimal d => SetResult(out result, (double)d),
                double => true, // Already a number
                string s when double.TryParse(s, out double d) => SetResult(out result, d),
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => 
                    SetResult(out result, jsonElement.GetDouble()),
                _ => false
            };
        }

        /// <summary>
        /// Attempts to coerce a value to an integer
        /// </summary>
        /// <param name="value">The value to coerce</param>
        /// <param name="result">The coerced integer</param>
        /// <returns>True if coercion was successful</returns>
        private static bool TryCoerceToInteger(object value, out object result)
        {
            result = value;
            
            return value switch
            {
                int => true, // Already an integer
                long l => SetResult(out result, (int)l),
                short s => SetResult(out result, (int)s),
                byte b => SetResult(out result, (int)b),
                double d when d % 1 == 0 => SetResult(out result, (int)d),
                float f when f % 1 == 0 => SetResult(out result, (int)f),
                decimal dec when dec % 1 == 0 => SetResult(out result, (int)dec),
                string s when int.TryParse(s, out int i) => SetResult(out result, i),
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number && 
                                           jsonElement.TryGetInt64(out long l) => 
                    SetResult(out result, (int)l),
                _ => false
            };
        }

        /// <summary>
        /// Attempts to coerce a value to a boolean
        /// </summary>
        /// <param name="value">The value to coerce</param>
        /// <param name="result">The coerced boolean</param>
        /// <returns>True if coercion was successful</returns>
        private static bool TryCoerceToBoolean(object value, out object result)
        {
            result = value;
            
            return value switch
            {
                bool => true, // Already a boolean
                string s when bool.TryParse(s, out bool b) => SetResult(out result, b),
                int i => SetResult(out result, i != 0),
                long l => SetResult(out result, l != 0),
                double d => SetResult(out result, d != 0),
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.True => 
                    SetResult(out result, true),
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.False => 
                    SetResult(out result, false),
                _ => false
            };
        }

        /// <summary>
        /// Attempts to coerce a value to an array
        /// </summary>
        /// <param name="value">The value to coerce</param>
        /// <param name="result">The coerced array</param>
        /// <returns>True if coercion was successful</returns>
        private static bool TryCoerceToArray(object value, out object result)
        {
            result = value;
            
            return value switch
            {
                Array => true, // Already an array
                IEnumerable<object> => true, // Already enumerable
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Array => 
                    SetResult(out result, jsonElement.EnumerateArray().Select(e => (object?)e).ToArray() ?? Array.Empty<object?>()),
                string s when s.StartsWith("[") && s.EndsWith("]") => 
                    TryParseJsonArray(s, out result),
                _ => false
            };
        }

        /// <summary>
        /// Attempts to coerce a value to an object
        /// </summary>
        /// <param name="value">The value to coerce</param>
        /// <param name="result">The coerced object</param>
        /// <returns>True if coercion was successful</returns>
        private static bool TryCoerceToObject(object value, out object result)
        {
            result = value;
            
            return value switch
            {
                Dictionary<string, object> => true, // Already an object
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Object => 
                    SetResult(out result, JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonElement.GetRawText()) ?? new Dictionary<string, object?>()),
                string s when s.StartsWith("{") && s.EndsWith("}") => 
                    TryParseJsonObject(s, out result),
                _ => false
            };
        }

        /// <summary>
        /// Attempts to parse a JSON array string
        /// </summary>
        /// <param name="jsonString">The JSON array string</param>
        /// <param name="result">The parsed array</param>
        /// <returns>True if parsing was successful</returns>
        private static bool TryParseJsonArray(string jsonString, out object result)
        {
            result = new object();
            try
            {
                object?[]? array = JsonSerializer.Deserialize<object?[]?>(jsonString);
                result = array ?? Array.Empty<object?>();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to parse a JSON object string
        /// </summary>
        /// <param name="jsonString">The JSON object string</param>
        /// <param name="result">The parsed object</param>
        /// <returns>True if parsing was successful</returns>
        private static bool TryParseJsonObject(string jsonString, out object result)
        {
            result = new object();
            try
            {
                Dictionary<string, object?>? obj = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonString);
                result = obj ?? new Dictionary<string, object?>();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sets the result value and returns true
        /// </summary>
        /// <param name="result">The result parameter</param>
        /// <param name="value">The value to set</param>
        /// <returns>True</returns>
        private static bool SetResult(out object result, object? value)
        {
            result = value ?? new object();
            return true;
        }
    }
}
