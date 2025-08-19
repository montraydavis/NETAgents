namespace NETAgents.Tools.Processing.Models.Ast;

// Base interface for all AST elements
public interface IAstElement
{
    string Name { get; set; }
    List<string> Modifiers { get; set; }
    List<string> Attributes { get; set; }
}

// Main AST compilation unit
public class AstCompilationUnit
{
    public string Type { get; set; } = "CompilationUnit";
    public string? Namespace { get; set; }
    public List<string> Usings { get; set; } = new();
    public List<AstClass> Classes { get; set; } = new();
    public List<AstInterface> Interfaces { get; set; } = new();
    public List<AstEnum> Enums { get; set; } = new();
    public List<AstRecord> Records { get; set; } = new();
    public List<AstStruct> Structs { get; set; } = new();
}

// Type declarations
public class AstClass : IAstElement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> BaseTypes { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<AstMember> Members { get; set; } = new();
}

public class AstInterface : IAstElement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> BaseTypes { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<AstMember> Members { get; set; } = new();
}

public class AstEnum : IAstElement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<string> Values { get; set; } = new();
}

public class AstRecord : IAstElement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> BaseTypes { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<AstMember> Members { get; set; } = new();
}

public class AstStruct : IAstElement
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<string> BaseTypes { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
    public List<AstMember> Members { get; set; } = new();
}

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

public class AstParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public bool IsParams { get; set; }
    public bool IsRef { get; set; }
    public bool IsOut { get; set; }
}

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

// Query result models (for cache service)
public class AstQueryResult
{
    public string FilePath { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Namespace { get; set; }
    public List<string> Modifiers { get; set; } = new();
    public List<string> BaseTypes { get; set; } = new();
    public List<string> Attributes { get; set; } = new();
}
