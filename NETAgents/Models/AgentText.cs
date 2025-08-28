namespace NETAgents.Models
{
    public class AgentText : AgentType
    {
        public AgentText(object value) : base(value) { }

        public override object ToRaw() => _value;
        public override string ToString() => _value?.ToString() ?? string.Empty;

        // Implicit conversion to string to match Python's str inheritance
        public static implicit operator string(AgentText agentText) => agentText.ToString();
        
        // Constructor from string
        public static AgentText FromString(string value) => new AgentText(value);
    }
}