namespace NETAgents.Tools.Processing.Models.Ast
{
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
}
