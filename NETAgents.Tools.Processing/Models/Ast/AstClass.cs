namespace NETAgents.Tools.Processing.Models.Ast
{
    // Type declarations
    public class AstClass : IAstElement
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Modifiers { get; set; } = new();
        public List<string> BaseTypes { get; set; } = new();
        public List<string> Attributes { get; set; } = new();
        public List<AstMember> Members { get; set; } = new();
    }
}
