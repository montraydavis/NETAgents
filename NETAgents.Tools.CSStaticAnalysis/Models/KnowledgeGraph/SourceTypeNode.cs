namespace MCPCSharpRelevancy.Models
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// Represents a type node in the source dependency graph with detailed metadata
    /// </summary>
    public class SourceTypeNode(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax? declarationSyntax,
        Document document,
        string sourceCode,
        string filePath,
        int startLine,
        int endLine,
        Project project,
        string? documentation = null,
        IList<string>? usingDirectives = null)
    {
        /// <summary>
        /// The Roslyn symbol representing this type
        /// </summary>
        public INamedTypeSymbol Symbol { get; } = symbol;

        /// <summary>
        /// The syntax node representing the type declaration
        /// </summary>
        public TypeDeclarationSyntax? DeclarationSyntax { get; } = declarationSyntax;

        /// <summary>
        /// The document containing this type
        /// </summary>
        public Document Document { get; } = document;

        /// <summary>
        /// The full source code of this type
        /// </summary>
        public string SourceCode { get; } = sourceCode;

        /// <summary>
        /// The file path where this type is defined
        /// </summary>
        public string FilePath { get; } = filePath;

        /// <summary>
        /// The line number where this type starts
        /// </summary>
        public int StartLine { get; } = startLine;

        /// <summary>
        /// The line number where this type ends
        /// </summary>
        public int EndLine { get; } = endLine;

        /// <summary>
        /// The project this type belongs to
        /// </summary>
        public Project Project { get; } = project;

        /// <summary>
        /// XML documentation comments for this type
        /// </summary>
        public string? Documentation { get; } = documentation;

        /// <summary>
        /// Namespace imports/using statements in the file
        /// </summary>
        public IList<string> UsingDirectives { get; } = usingDirectives ?? [];

        /// <summary>
        /// Dependencies of this type
        /// </summary>
        public List<SourceTypeDependency> Dependencies { get; } = [];

        /// <summary>
        /// Types that depend on this type
        /// </summary>
        public List<SourceTypeDependency> Dependents { get; } = [];

        /// <summary>
        /// AI-generated summary/documentation for this type
        /// </summary>
        public string? Summary { get; set; }


        /// <summary>
        /// Gets the namespace of this type
        /// </summary>
        public string Namespace => this.Symbol.ContainingNamespace.ToDisplayString();

        /// <summary>
        /// Gets the full name of this type
        /// </summary>
        public string FullName => this.Symbol.ToDisplayString();

        /// <summary>
        /// Gets the simple name of this type
        /// </summary>
        public string Name => this.Symbol.Name;

        /// <summary>
        /// Indicates if this type is abstract
        /// </summary>
        public bool IsAbstract { get; } = symbol.IsAbstract;

        /// <summary>
        /// Indicates if this type is static
        /// </summary>
        public bool IsStatic { get; } = symbol.IsStatic;

        /// <summary>
        /// Indicates if this type is sealed
        /// </summary>
        public bool IsSealed { get; } = symbol.IsSealed;

        /// <summary>
        /// Indicates if this type is an interface
        /// </summary>
        public bool IsInterface { get; } = symbol.TypeKind == TypeKind.Interface;

        /// <summary>
        /// Indicates if this type is a class
        /// </summary>
        public bool IsClass => this.Symbol.TypeKind == TypeKind.Class;

        /// <summary>
        /// Indicates if this type is a struct
        /// </summary>
        public bool IsStruct => this.Symbol.TypeKind == TypeKind.Struct;

        /// <summary>
        /// Indicates if this type is an enum
        /// </summary>
        public bool IsEnum => this.Symbol.TypeKind == TypeKind.Enum;

        /// <summary>
        /// Gets the base type of this type
        /// </summary>
        public INamedTypeSymbol? BaseType => this.Symbol.BaseType;

        /// <summary>
        /// Gets the interfaces implemented by this type
        /// </summary>
        public IEnumerable<INamedTypeSymbol> Interfaces => this.Symbol.Interfaces;

        /// <summary>
        /// Gets all members of this type
        /// </summary>
        public IEnumerable<ISymbol> Members => this.Symbol.GetMembers();

        /// <summary>
        /// Gets the type parameters if this is a generic type
        /// </summary>
        public IEnumerable<ITypeParameterSymbol> TypeParameters => this.Symbol.TypeParameters;

        /// <summary>
        /// Gets the location information for this type
        /// </summary>
        public Location Location => this.Symbol.Locations.FirstOrDefault() ?? Location.None;

        /// <summary>
        /// Calculates the aggregate relationship strength with a target type
        /// </summary>
        public double CalculateRelationshipStrength(string targetTypeName)
        {
            IEnumerable<SourceTypeDependency> relevantDependencies = this.Dependencies.Where(d => d.TargetType.ToDisplayString() == targetTypeName);
            if (!relevantDependencies.Any())
            {
                return 0.0;
            }

            // Calculate weighted average based on dependency frequency and strength
            double totalWeight = 0.0;
            double totalStrength = 0.0;

            foreach (SourceTypeDependency? dependency in relevantDependencies)
            {
                totalWeight += dependency.Weight;
                totalStrength += dependency.Strength * dependency.Weight;
            }

            return totalWeight > 0 ? totalStrength / totalWeight : 0.0;
        }

        /// <summary>
        /// Gets the strongest dependencies from this type
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetStrongestDependencies(int count = 10)
        {
            return this.Dependencies.OrderByDescending(d => d.Strength).Take(count);
        }

        /// <summary>
        /// Gets the strongest dependents of this type
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetStrongestDependents(int count = 10)
        {
            return this.Dependents.OrderByDescending(d => d.Strength).Take(count);
        }

        public override string ToString()
        {
            return $"{this.Name} ({this.Project.Name}) - {this.Dependencies.Count} deps, {this.Dependents.Count} dependents";
        }
    }
}