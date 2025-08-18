namespace MCPCSharpRelevancy.Services.Analysis
{
    using MCPCSharpRelevancy.Models;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// Advanced semantic analyzer for extracting comprehensive type dependencies
    /// </summary>
    public class SemanticAnalyzer
    {
        /// <summary>
        /// Analyzes all types of dependencies in a syntax tree
        /// </summary>
        public async Task<List<SourceTypeDependency>> AnalyzeAllDependenciesAsync(
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

            // Analyze using directives
            List<SourceTypeDependency> usingDependencies = this.AnalyzeUsingDirectives(root, semanticModel, document, includeSystemTypes);
            dependencies.AddRange(usingDependencies);

            // Find all type declarations
            IEnumerable<TypeDeclarationSyntax> typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (TypeDeclarationSyntax typeDeclaration in typeDeclarations)
            {
                if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol sourceSymbol || !includeSystemTypes && this.IsSystemType(sourceSymbol))
                {
                    continue;
                }

                List<SourceTypeDependency> typeDependencies = this.AnalyzeTypeDeclaration(
                    sourceSymbol, typeDeclaration, semanticModel, document, includeSystemTypes);
                dependencies.AddRange(typeDependencies);
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes using directives for namespace dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeUsingDirectives(
            SyntaxNode root,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            IEnumerable<UsingDirectiveSyntax> usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            foreach (UsingDirectiveSyntax usingDirective in usingDirectives)
            {
                if (semanticModel.GetSymbolInfo(usingDirective.Name!).Symbol is INamespaceSymbol namespaceSymbol)
                {
                    string namespaceName = namespaceSymbol.ToDisplayString();
                    if (includeSystemTypes || !this.IsSystemNamespace(namespaceName))
                    {
                        // For using directives, we create a dependency from the document to the namespace
                        // This is a bit of a stretch since we need type symbols, but it helps track namespace usage
                        // We'll handle this differently in the actual implementation
                    }
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes a complete type declaration for all dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeTypeDeclaration(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies =
            [
                // Base type and interfaces
                .. this.AnalyzeInheritanceDependencies(sourceSymbol, typeDeclaration, document, includeSystemTypes),
                // Attributes
                .. this.AnalyzeAttributeDependencies(sourceSymbol, typeDeclaration, semanticModel, document, includeSystemTypes),
                // Generic constraints
                .. this.AnalyzeGenericConstraints(sourceSymbol, typeDeclaration, document, includeSystemTypes),
                // Members
                .. this.AnalyzeMemberDependencies(sourceSymbol, typeDeclaration, semanticModel, document, includeSystemTypes),
                // Method bodies and expressions
                .. this.AnalyzeMethodBodies(sourceSymbol, typeDeclaration, semanticModel, document, includeSystemTypes),
            ];

            return dependencies;
        }

        /// <summary>
        /// Analyzes inheritance dependencies (base types and interfaces)
        /// </summary>
        private List<SourceTypeDependency> AnalyzeInheritanceDependencies(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Base type
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

            // Interfaces
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

            return dependencies;
        }

        /// <summary>
        /// Analyzes attribute dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeAttributeDependencies(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            SyntaxList<AttributeListSyntax> attributeLists = typeDeclaration.AttributeLists;
            foreach (AttributeListSyntax attributeList in attributeLists)
            {
                foreach (AttributeSyntax attribute in attributeList.Attributes)
                {
                    ISymbol? attributeSymbol = semanticModel.GetSymbolInfo(attribute).Symbol;
                    if (attributeSymbol?.ContainingType != null &&
                        (!this.IsSystemType(attributeSymbol.ContainingType) || includeSystemTypes))
                    {
                        dependencies.Add(new SourceTypeDependency(
                            sourceSymbol,
                            attributeSymbol.ContainingType,
                            SourceDependencyType.Attribute,
                            attribute.Name.ToString(),
                            attribute.GetLocation(),
                            document,
                            usages: this.ExtractUsageSnippetAsync(attribute.GetLocation(), document).GetAwaiter().GetResult()));
                    }
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes generic constraint dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeGenericConstraints(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            foreach (ITypeParameterSymbol typeParameter in sourceSymbol.TypeParameters)
            {
                foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes)
                {
                    if (constraint is INamedTypeSymbol namedConstraint &&
                        (!this.IsSystemType(namedConstraint) || includeSystemTypes))
                    {
                        dependencies.Add(new SourceTypeDependency(
                            sourceSymbol,
                            namedConstraint,
                            SourceDependencyType.GenericArgument,
                            typeParameter.Name,
                            typeDeclaration.GetLocation(),
                            document,
                            usages: this.ExtractUsageSnippetAsync(typeDeclaration.GetLocation(), document).GetAwaiter().GetResult()));
                    }
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes member dependencies (fields, properties, methods, events)
        /// </summary>
        private List<SourceTypeDependency> AnalyzeMemberDependencies(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Fields
            IEnumerable<FieldDeclarationSyntax> fields = typeDeclaration.DescendantNodes().OfType<FieldDeclarationSyntax>();
            foreach (FieldDeclarationSyntax field in fields)
            {
                dependencies.AddRange(this.AnalyzeFieldDependencies(sourceSymbol, field, semanticModel, document, includeSystemTypes));
            }

            // Properties
            IEnumerable<PropertyDeclarationSyntax> properties = typeDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (PropertyDeclarationSyntax property in properties)
            {
                dependencies.AddRange(this.AnalyzePropertyDependencies(sourceSymbol, property, semanticModel, document, includeSystemTypes));
            }

            // Methods
            IEnumerable<MethodDeclarationSyntax> methods = typeDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (MethodDeclarationSyntax method in methods)
            {
                dependencies.AddRange(this.AnalyzeMethodSignatureDependencies(sourceSymbol, method, semanticModel, document, includeSystemTypes));
            }

            // Constructors
            IEnumerable<ConstructorDeclarationSyntax> constructors = typeDeclaration.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            foreach (ConstructorDeclarationSyntax constructor in constructors)
            {
                dependencies.AddRange(this.AnalyzeConstructorDependencies(sourceSymbol, constructor, semanticModel, document, includeSystemTypes));
            }

            // Events
            IEnumerable<EventDeclarationSyntax> events = typeDeclaration.DescendantNodes().OfType<EventDeclarationSyntax>();
            foreach (EventDeclarationSyntax eventDecl in events)
            {
                dependencies.AddRange(this.AnalyzeEventDependencies(sourceSymbol, eventDecl, semanticModel, document, includeSystemTypes));
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes method bodies for expression-level dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeMethodBodies(
            INamedTypeSymbol sourceSymbol,
            TypeDeclarationSyntax typeDeclaration,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Method bodies
            IEnumerable<MethodDeclarationSyntax> methods = typeDeclaration.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (MethodDeclarationSyntax method in methods)
            {
                if (method.Body != null)
                {
                    dependencies.AddRange(this.AnalyzeBlockDependencies(sourceSymbol, method.Body, semanticModel, document, includeSystemTypes));
                }

                if (method.ExpressionBody != null)
                {
                    dependencies.AddRange(this.AnalyzeExpressionDependencies(sourceSymbol, method.ExpressionBody.Expression, semanticModel, document, includeSystemTypes));
                }
            }

            // Property accessors
            IEnumerable<PropertyDeclarationSyntax> properties = typeDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>();
            foreach (PropertyDeclarationSyntax property in properties)
            {
                if (property.AccessorList != null)
                {
                    foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
                    {
                        if (accessor.Body != null)
                        {
                            dependencies.AddRange(this.AnalyzeBlockDependencies(sourceSymbol, accessor.Body, semanticModel, document, includeSystemTypes));
                        }

                        if (accessor.ExpressionBody != null)
                        {
                            dependencies.AddRange(this.AnalyzeExpressionDependencies(sourceSymbol, accessor.ExpressionBody.Expression, semanticModel, document, includeSystemTypes));
                        }
                    }
                }

                if (property.ExpressionBody != null)
                {
                    dependencies.AddRange(this.AnalyzeExpressionDependencies(sourceSymbol, property.ExpressionBody.Expression, semanticModel, document, includeSystemTypes));
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes a block statement for dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeBlockDependencies(
            INamedTypeSymbol sourceSymbol,
            BlockSyntax block,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Variable declarations
            IEnumerable<VariableDeclarationSyntax> variableDeclarations = block.DescendantNodes().OfType<VariableDeclarationSyntax>();
            foreach (VariableDeclarationSyntax varDecl in variableDeclarations)
            {
                TypeInfo typeInfo = semanticModel.GetTypeInfo(varDecl.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType &&
                    (!this.IsSystemType(namedType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        namedType,
                        SourceDependencyType.LocalVariable,
                        varDecl.Variables.FirstOrDefault()?.Identifier.ValueText,
                        varDecl.GetLocation(),
                        document,
                        usages: this.ExtractUsageSnippetAsync(varDecl.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            // Object creation expressions
            IEnumerable<ObjectCreationExpressionSyntax> objectCreations = block.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (ObjectCreationExpressionSyntax objCreation in objectCreations)
            {
                TypeInfo typeInfo = semanticModel.GetTypeInfo(objCreation);
                if (typeInfo.Type is INamedTypeSymbol namedType &&
                    (!this.IsSystemType(namedType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        namedType,
                        SourceDependencyType.NewExpression,
                        location: objCreation.GetLocation(),
                        document: document,
                        usages: this.ExtractUsageSnippetAsync(objCreation.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            // Method invocations
            IEnumerable<InvocationExpressionSyntax> invocations = block.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (InvocationExpressionSyntax invocation in invocations)
            {
                SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation);
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol &&
                    methodSymbol.ContainingType != null &&
                    (!this.IsSystemType(methodSymbol.ContainingType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        methodSymbol.ContainingType,
                        SourceDependencyType.Method,
                        methodSymbol.Name,
                        invocation.GetLocation(),
                        document,
                        usages: this.ExtractUsageSnippetAsync(invocation.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            // Cast expressions
            IEnumerable<CastExpressionSyntax> casts = block.DescendantNodes().OfType<CastExpressionSyntax>();
            foreach (CastExpressionSyntax cast in casts)
            {
                TypeInfo typeInfo = semanticModel.GetTypeInfo(cast.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType &&
                    (!this.IsSystemType(namedType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        namedType,
                        SourceDependencyType.CastOperation,
                        location: cast.GetLocation(),
                        document: document,
                        usages: this.ExtractUsageSnippetAsync(cast.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            return dependencies;
        }

        /// <summary>
        /// Analyzes expression dependencies
        /// </summary>
        private List<SourceTypeDependency> AnalyzeExpressionDependencies(
            INamedTypeSymbol sourceSymbol,
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // This is a simplified version - in a full implementation, you'd want to
            // recursively analyze all expression types
            TypeInfo typeInfo = semanticModel.GetTypeInfo(expression);
            if (typeInfo.Type is INamedTypeSymbol namedType &&
                (!this.IsSystemType(namedType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    namedType,
                    SourceDependencyType.ReturnType,
                    location: expression.GetLocation(),
                    document: document,
                    usages: this.ExtractUsageSnippetAsync(expression.GetLocation(), document).GetAwaiter().GetResult()));
            }

            return dependencies;
        }

        // Helper methods for analyzing specific member types
        private List<SourceTypeDependency> AnalyzeFieldDependencies(
            INamedTypeSymbol sourceSymbol,
            FieldDeclarationSyntax field,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            TypeInfo typeInfo = semanticModel.GetTypeInfo(field.Declaration.Type);
            if (typeInfo.Type is INamedTypeSymbol namedType &&
                (!this.IsSystemType(namedType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    namedType,
                    SourceDependencyType.Field,
                    field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
                    field.GetLocation(),
                    document,
                    usages: this.ExtractUsageSnippetAsync(field.GetLocation(), document).GetAwaiter().GetResult()));
            }

            return dependencies;
        }

        private List<SourceTypeDependency> AnalyzePropertyDependencies(
            INamedTypeSymbol sourceSymbol,
            PropertyDeclarationSyntax property,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            TypeInfo typeInfo = semanticModel.GetTypeInfo(property.Type);
            if (typeInfo.Type is INamedTypeSymbol namedType &&
                (!this.IsSystemType(namedType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    namedType,
                    SourceDependencyType.Property,
                    property.Identifier.ValueText,
                    property.GetLocation(),
                    document,
                    usages: this.ExtractUsageSnippetAsync(property.GetLocation(), document).GetAwaiter().GetResult()));
            }

            return dependencies;
        }

        private List<SourceTypeDependency> AnalyzeMethodSignatureDependencies(
            INamedTypeSymbol sourceSymbol,
            MethodDeclarationSyntax method,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Return type
            TypeInfo returnTypeInfo = semanticModel.GetTypeInfo(method.ReturnType);
            if (returnTypeInfo.Type is INamedTypeSymbol returnType &&
                (!this.IsSystemType(returnType) || includeSystemTypes))
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
                TypeInfo paramTypeInfo = semanticModel.GetTypeInfo(parameter.Type!);
                if (paramTypeInfo.Type is INamedTypeSymbol paramType &&
                    (!this.IsSystemType(paramType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        paramType,
                        SourceDependencyType.Parameter,
                        $"{method.Identifier.ValueText}.{parameter.Identifier.ValueText}",
                        parameter.GetLocation(),
                        document,
                        usages: this.ExtractUsageSnippetAsync(parameter.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            return dependencies;
        }

        private List<SourceTypeDependency> AnalyzeConstructorDependencies(
            INamedTypeSymbol sourceSymbol,
            ConstructorDeclarationSyntax constructor,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            // Parameters
            foreach (ParameterSyntax parameter in constructor.ParameterList.Parameters)
            {
                TypeInfo paramTypeInfo = semanticModel.GetTypeInfo(parameter.Type!);
                if (paramTypeInfo.Type is INamedTypeSymbol paramType &&
                    (!this.IsSystemType(paramType) || includeSystemTypes))
                {
                    dependencies.Add(new SourceTypeDependency(
                        sourceSymbol,
                        paramType,
                        SourceDependencyType.Constructor,
                        $"ctor.{parameter.Identifier.ValueText}",
                        parameter.GetLocation(),
                        document,
                        usages: this.ExtractUsageSnippetAsync(parameter.GetLocation(), document).GetAwaiter().GetResult()));
                }
            }

            return dependencies;
        }

        private List<SourceTypeDependency> AnalyzeEventDependencies(
            INamedTypeSymbol sourceSymbol,
            EventDeclarationSyntax eventDecl,
            SemanticModel semanticModel,
            Document document,
            bool includeSystemTypes)
        {
            List<SourceTypeDependency> dependencies = [];

            TypeInfo typeInfo = semanticModel.GetTypeInfo(eventDecl.Type);
            if (typeInfo.Type is INamedTypeSymbol namedType &&
                (!this.IsSystemType(namedType) || includeSystemTypes))
            {
                dependencies.Add(new SourceTypeDependency(
                    sourceSymbol,
                    namedType,
                    SourceDependencyType.Event,
                    eventDecl.Identifier.ValueText,
                    eventDecl.GetLocation(),
                    document,
                    usages: this.ExtractUsageSnippetAsync(eventDecl.GetLocation(), document).GetAwaiter().GetResult()));
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

        /// <summary>
        /// Determines if a type is a system type that should be filtered out
        /// </summary>
        private bool IsSystemType(INamedTypeSymbol symbol)
        {
            string namespaceName = symbol.ContainingNamespace.ToDisplayString();
            return this.IsSystemNamespace(namespaceName);
        }

        /// <summary>
        /// Determines if a namespace is a system namespace
        /// </summary>
        private bool IsSystemNamespace(string namespaceName)
        {
            return namespaceName.StartsWith("System") ||
                   namespaceName.StartsWith("Microsoft") ||
                   namespaceName.StartsWith("mscorlib") ||
                   namespaceName == "System";
        }
    }
}