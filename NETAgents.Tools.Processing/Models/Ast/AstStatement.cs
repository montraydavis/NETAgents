namespace NETAgents.Tools.Processing.Models.Ast
{
    public class AstStatement
    {
        public string Type { get; set; } = string.Empty; // assignment, return, call, declaration, if, loop
        public string? Target { get; set; }
        public string? Value { get; set; }
        public string? Method { get; set; }
        public List<string> Arguments { get; set; } = new();
        public string? Condition { get; set; }
        public List<AstStatement> Body { get; set; } = new();
        public string? Name { get; set; }
        public string? Initializer { get; set; }
    }
}
