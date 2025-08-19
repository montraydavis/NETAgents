namespace NETAgents.Tools.Processing.Models.Ast;

public class AstEnum : IAstElement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<string> Values { get; set; } = new();
}
