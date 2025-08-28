namespace NETAgents.Tools.Processing.Models.Ast
{
    public class AstParameter
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? DefaultValue { get; set; }
        public bool IsParams { get; set; }
        public bool IsRef { get; set; }
        public bool IsOut { get; set; }
    }
}
