namespace NETAgents.Tools.Processing.Models.Ast
{
    // Base interface for all AST elements
    public interface IAstElement
    {
        string Name { get; set; }
        List<string> Modifiers { get; set; }
        List<string> Attributes { get; set; }
    }
}
