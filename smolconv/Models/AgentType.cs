namespace SmolConv.Models
{
    // ===============================
    // AGENT TYPES
    // ===============================

    public abstract class AgentType
    {
        protected object _value;

        protected AgentType(object value)
        {
            _value = value;
        }

        public abstract object ToRaw();
        public abstract override string ToString();

        // Default implementations that match Python behavior
        protected virtual object ToRawDefault()
        {
            // Equivalent to Python's logger.error for unknown types
            System.Diagnostics.Debug.WriteLine("This is a raw AgentType of unknown type. Display in notebooks and string conversion will be unreliable");
            return _value;
        }

        protected virtual string ToStringDefault()
        {
            // Equivalent to Python's logger.error for unknown types
            System.Diagnostics.Debug.WriteLine("This is a raw AgentType of unknown type. Display in notebooks and string conversion will be unreliable");
            return _value?.ToString() ?? string.Empty;
        }
    }
}