namespace MCPCSharpRelevancy.Services.Analysis
{
    using MCPCSharpRelevancy.Models;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// Service for analyzing individual C# projects and extracting type information
    /// </summary>
    public class ProjectAnalyzer
    {
        /// <summary>
        /// Analyzes a project and extracts all type nodes
        /// </summary>
        /// <param name="project">The project to analyze</param>
        /// <param name="includeSystemTypes">Whether to include system types</param>
        /// <returns>List of source type nodes</returns>
        public async Task<List<SourceTypeNode>> AnalyzeProjectAsync(Project project, bool includeSystemTypes = false)
        {
            List<SourceTypeNode> typeNodes = [];

            foreach (Document document in project.Documents)
            {
                if (!document.Name.EndsWith(".cs"))
                {
                    continue; // Skip non-C# files
                }

                List<SourceTypeNode> documentTypes = await this.AnalyzeDocumentAsync(document, includeSystemTypes);
                typeNodes.AddRange(documentTypes);
            }

            return typeNodes;
        }

        /// <summary>
        /// Analyzes a document and extracts all type declarations
        /// </summary>
        /// <param name="document">The document to analyze</param>
        /// <param name="includeSystemTypes">Whether to include system types</param>
        /// <returns>List of source type nodes</returns>
        public async Task<List<SourceTypeNode>> AnalyzeDocumentAsync(Document document, bool includeSystemTypes = false)
        {
            List<SourceTypeNode> typeNodes = [];

            SyntaxTree? syntaxTree = await document.GetSyntaxTreeAsync();
            SemanticModel? semanticModel = await document.GetSemanticModelAsync();

            if (syntaxTree == null || semanticModel == null)
            {
                return typeNodes;
            }

            SyntaxNode root = await syntaxTree.GetRootAsync();
            Microsoft.CodeAnalysis.Text.SourceText sourceText = await document.GetTextAsync();

            // Find all type declarations
            IEnumerable<TypeDeclarationSyntax> typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (TypeDeclarationSyntax typeDeclaration in typeDeclarations)
            {
                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                if (!includeSystemTypes && this.IsSystemType(symbol))
                {
                    continue;
                }

                SourceTypeNode? typeNode = await this.CreateSourceTypeNodeAsync(symbol, typeDeclaration, document, sourceText.ToString());
                if (typeNode != null)
                {
                    typeNodes.Add(typeNode);
                }
            }

            return typeNodes;
        }

        /// <summary>
        /// Analyzes dependencies for a project
        /// </summary>
        /// <param name="project">The project to analyze</param>
        /// <param name="graph">The dependency graph to populate</param>
        /// <param name="includeSystemTypes">Whether to include system types</param>
        /// <returns>List of dependencies</returns>
        public async Task<List<SourceTypeDependency>> AnalyzeDependenciesAsync(
            Project project,
            SourceDependencyGraph graph,
            bool includeSystemTypes = false)
        {
            List<SourceTypeDependency> dependencies = [];

            foreach (Document document in project.Documents)
            {
                if (!document.Name.EndsWith(".cs"))
                {
                    continue;
                }

                List<SourceTypeDependency> documentDependencies = await this.AnalyzeDocumentDependenciesAsync(document, graph, includeSystemTypes);
                dependencies.AddRange(documentDependencies);
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes dependencies for a specific document
        /// </summary>
        /// <param name="document">The document to analyze</param>
        /// <param name="graph">The dependency graph</param>
        /// <param name="includeSystemTypes">Whether to include system types</param>
        /// <returns>List of dependencies</returns>
        public async Task<List<SourceTypeDependency>> AnalyzeDocumentDependenciesAsync(
            Document document,
            SourceDependencyGraph graph,
            bool includeSystemTypes = false)
        {
            List<SourceTypeDependency> dependencies = [];

            SyntaxTree? syntaxTree = await document.GetSyntaxTreeAsync();
            SemanticModel? semanticModel = await document.GetSemanticModelAsync();

            if (syntaxTree == null || semanticModel == null)
            {
                return dependencies;
            }

            SyntaxNode root = await syntaxTree.GetRootAsync();

            // Find all type declarations in this document
            IEnumerable<TypeDeclarationSyntax> typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (TypeDeclarationSyntax typeDeclaration in typeDeclarations)
            {
                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol sourceSymbol)
                {
                    continue;
                }

                SourceTypeNode? sourceNode = graph.GetNode(sourceSymbol);
                if (sourceNode == null)
                {
                    continue;
                }

                // Analyze different types of dependencies
                List<SourceTypeDependency> typeDependencies = this.AnalyzeTypeDependencies(
                    sourceSymbol,
                    typeDeclaration,
                    semanticModel,
                    document,
                    includeSystemTypes);

                dependencies.AddRange(typeDependencies);
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes dependencies for a specific type
        /// </summary>
        private List<SourceTypeDependency> AnalyzeTypeDependencies(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Base type dependencies
            if (sourceSymbol.BaseType != null &&
                (!this.IsSystemType(sourceSymbol.BaseType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    sourceSymbol.BaseType,
                    SourceDependencyType.Inheritance,
                    location: typeDeclaration.GetLocation(),
                    document: document,
                    usages: this.ExtractUsageSnippetAsync(typeDeclaration.GetLocation(), document).GetAwaiter().GetResult()));
            }

            // Interface dependencies
            foreach (INamedTypeSymbol interfaceType in sourceSymbol.Interfaces)
            {
                if (!this.IsSystemType(interfaceType) || includeSystemTypes)
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        interfaceType,
                        SourceDependencyType.Interface,
                        location: typeDeclaration.GetLocation(),
                        document: document,
                        usages: this.ExtractUsageSnippetAsync(typeDeclaration.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            // Field dependencies
            IEnumerable<FieldDeclarationSyntax> fields = typeDeclaration.DescendantNodes().OfType<FieldDeclarationSyntax>();
            foreach (FieldDeclarationSyntax field in fields)
            {
                List<SourceTypeDependency> fieldDependencies = this.AnalyzeFieldDependencies(sourceSymbol, field, semanticModel, document, includeSystemTypes);
                dependencies.AddRange(fieldDependencies);
            }

            // Property dependencies
            IEnumerable<PropertyDeclarationSyntax> properties = typeDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (PropertyDeclarationSyntax property in properties)
            {
                List<SourceTypeDependency> propertyDependencies = this.AnalyzePropertyDependencies(sourceSymbol, property, semanticModel, document, includeSystemTypes);
                dependencies.AddRange(propertyDependencies);
            }

            // Method dependencies
            IEnumerable<MethodDeclarationSyntax> methods = typeDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (MethodDeclarationSyntax method in methods)
            {
                List<SourceTypeDependency> methodDependencies = this.AnalyzeMethodDependencies(sourceSymbol, method, semanticModel, document, includeSystemTypes);
                dependencies.AddRange(methodDependencies);
            }

            return dependencies;
        }

        /// <summary>
        /// Creates a SourceTypeNode from a symbol and syntax
        /// </summary>
        private async Task<SourceTypeNode?> CreateSourceTypeNodeAsync(
            INamedTypeSymbol symbol,
            TypeDeclarationSyntax typeDeclaration,
            Document document,
            string fullSourceText)
        {
            string filePath = document.FilePath ?? document.Name;
            Location location = typeDeclaration.GetLocation();
            FileLinePositionSpan span = location.GetLineSpan();

            // Extract just the type source code
            Microsoft.CodeAnalysis.Text.SourceText sourceText = await document.GetTextAsync();
            string typeSource = sourceText.GetSubText(typeDeclaration.Span).ToString();

            // Extract using directives
            SyntaxNode? root = await document.GetSyntaxRootAsync();
            List<string> usingDirectives = root?.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name?.ToString() ?? string.Empty)
                .Where(u => !string.IsNullOrEmpty(u))
                .ToList() ?? [];

            // Extract documentation
            string? documentation = this.ExtractDocumentation(typeDeclaration);

            return new SourceTypeNode(
                symbol,
                typeDeclaration,
                document,
                typeSource,
                filePath,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1,
                document.Project,
                documentation,
                usingDirectives);
        }

        /// <summary>
        /// Extracts XML documentation from a type declaration
        /// </summary>
        private string? ExtractDocumentation(TypeDeclarationSyntax typeDeclaration)
        {
            SyntaxTrivia documentationComment = typeDeclaration.GetLeadingTrivia()
                .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                   t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

            return documentationComment.IsKind(SyntaxKind.None) ? null : documentationComment.ToString();
        }

        /// <summary>
        /// Determines if a type is a system type
        /// </summary>
        private bool IsSystemType(INamedTypeSymbol symbol)
        {
            string namespaceName = symbol.ContainingNamespace.ToDisplayString();
            return namespaceName.StartsWith("System") ||
                   namespaceName.StartsWith("Microsoft") ||
                   namespaceName.StartsWith("mscorlib");
        }

        /// <summary>
        /// Analyzes field dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeFieldDependencies(
            INamedTypeSymbol sourceSymbol,
            FieldDeclarationSyntax field,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            if (semanticModel.GetTypeInfo(field.Declaration.Type).Type is INamedTypeSymbol fieldType && (!this.IsSystemType(fieldType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    fieldType,
                    SourceDependencyType.Field,
                    field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
                    field.GetLocation(),
                    document,
                    usages: this.ExtractUsageSnippetAsync(field.GetLocation(), document).GetAwaiter().GetResult()));
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes property dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzePropertyDependencies(
            INamedTypeSymbol sourceSymbol,
            PropertyDeclarationSyntax property,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            if (semanticModel.GetTypeInfo(property.Type).Type is INamedTypeSymbol propertyType && (!this.IsSystemType(propertyType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    propertyType,
                    SourceDependencyType.Property,
                    property.Identifier.ValueText,
                    property.GetLocation(),
                    document,
                    usages: this.ExtractUsageSnippetAsync(property.GetLocation(), document).GetAwaiter().GetResult()));
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes method dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeMethodDependencies(
            INamedTypeSymbol sourceSymbol,
            MethodDeclarationSyntax method,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Return type
            if (semanticModel.GetTypeInfo(method.ReturnType).Type is INamedTypeSymbol returnType && (!this.IsSystemType(returnType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    returnType,
                    SourceDependencyType.ReturnType,
                    method.Identifier.ValueText,
                    method.GetLocation(),
                    document,
                    usages: this.ExtractUsageSnippetAsync(method.GetLocation(), document).GetAwaiter().GetResult()));
            }

            // Parameters
            foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
            {
                if (semanticModel.GetTypeInfo(parameter.Type!).Type is INamedTypeSymbol parameterType && (!this.IsSystemType(parameterType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        parameterType,
                        SourceDependencyType.Parameter,
                        $"{method.Identifier.ValueText}.{parameter.Identifier.ValueText}",
                        parameter.GetLocation(),
                        document,
                        usages: this.ExtractUsageSnippetAsync(parameter.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            // --- Method body analysis for method calls, field/property accesses, and local variable types ---
            if (method.Body != null)
            {
                // Method calls
                IEnumerable<InvocationExpressionSyntax> invocationExpressions = method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (InvocationExpressionSyntax invocation in invocationExpressions)
                {
                    SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is IMethodSymbol calledMethod)
                    {
                        INamedTypeSymbol targetType = calledMethod.ContainingType;
                        if (targetType != null && (!this.IsSystemType(targetType) || includeSystemTypes))
                        {
                            dependencies.Add(new SourceTypeDependency(
                                sourceSymbol,
                                targetType,
                                SourceDependencyType.MethodCall,
                                calledMethod.Name,
                                invocation.GetLocation(),
                                document,
                                usages: this.ExtractUsageSnippetAsync(invocation.GetLocation(), document).GetAwaiter().GetResult()));
                        }
                    }
                }

                // Field accesses
                IEnumerable<MemberAccessExpressionSyntax> memberAccesses = method.Body.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
                foreach (MemberAccessExpressionSyntax memberAccess in memberAccesses)
                {
                    SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
                    if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
                    {
                        INamedTypeSymbol targetType = fieldSymbol.ContainingType;
                        if (targetType != null && (!this.IsSystemType(targetType) || includeSystemTypes))
                        {
                            dependencies.Add(new SourceTypeDependency(
                                sourceSymbol,
                                targetType,
                                SourceDependencyType.FieldAccess,
                                fieldSymbol.Name,
                                memberAccess.GetLocation(),
                                document,
                                usages: this.ExtractUsageSnippetAsync(memberAccess.GetLocation(), document).GetAwaiter().GetResult()));
                        }
                    }
                    else if (symbolInfo.Symbol is IPropertySymbol propertySymbol)
                    {
                        INamedTypeSymbol targetType = propertySymbol.ContainingType;
                        if (targetType != null && (!this.IsSystemType(targetType) || includeSystemTypes))
                        {
                            dependencies.Add(new SourceTypeDependency(
                                sourceSymbol,
                                targetType,
                                SourceDependencyType.PropertyAccess,
                                propertySymbol.Name,
                                memberAccess.GetLocation(),
                                document,
                                usages: this.ExtractUsageSnippetAsync(memberAccess.GetLocation(), document).GetAwaiter().GetResult()));
                        }
                    }
                }

                // Local variable types
                IEnumerable<VariableDeclarationSyntax> variableDeclarations = method.Body.DescendantNodes().OfType<VariableDeclarationSyntax>();
                foreach (VariableDeclarationSyntax variableDecl in variableDeclarations)
                {
                    if (semanticModel.GetTypeInfo(variableDecl.Type).Type is INamedTypeSymbol localVarType && (!this.IsSystemType(localVarType) || includeSystemTypes))
                    {
                        dependencies.Add(new SourceTypeDependency(
                            sourceSymbol,
                            localVarType,
                            SourceDependencyType.LocalVariableType,
                            variableDecl.Type.ToString(),
                            variableDecl.GetLocation(),
                            document,
                            usages: this.ExtractUsageSnippetAsync(variableDecl.GetLocation(), document).GetAwaiter().GetResult()));
                    }
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Helper to extract code snippet (5 lines before and after) for usages
        /// </summary>
        private async Task<List<string>> ExtractUsageSnippetAsync(Location? location, Document? document)
        {
            if (location == null || document == null)
            {
                return [];
            }

            FileLinePositionSpan lineSpan = location.GetLineSpan();
            int startLine = Math.Max(0, lineSpan.StartLinePosition.Line - 5);
            int endLine = lineSpan.EndLinePosition.Line + 5;

            Microsoft.CodeAnalysis.Text.SourceText text = await document.GetTextAsync();
            Microsoft.CodeAnalysis.Text.TextLineCollection lines = text.Lines;
            int totalLines = lines.Count;

            startLine = Math.Max(0, startLine);
            endLine = Math.Min(totalLines - 1, endLine);

            List<string> snippetLines = [];
            for (int i = startLine; i <= endLine; i++)
            {
                snippetLines.Add(lines[i].ToString());
            }

            return [string.Join(Environment.NewLine, snippetLines)];
        }
    }
}