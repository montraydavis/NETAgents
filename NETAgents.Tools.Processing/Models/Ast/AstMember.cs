namespace NETAgents.Tools.Processing.Models.Ast;

// Member and parameter classes
public class AstMember
{
    public string Kind { get; set; } = string.Empty; // field, property, method, constructor, etc.
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public string? ReturnType { get; set; }
    public List<AstParameter> Parameters { get; set; } = new();
    public List<AstStatement> Body { get; set; } = new();
    public bool? HasGetter { get; set; }
    public bool? HasSetter { get; set; }
    public string? SetterModifier { get; set; }
}
