using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NETAgents.Exceptions;
using NETAgents.Models;

namespace NETAgents.Core
{
    /// <summary>
    /// C# equivalent of Python's built-in names
    /// </summary>
    public static class BuiltinNames
    {
        public static readonly HashSet<string> CSharpBuiltins = new()
        {
            "object", "string", "int", "bool", "double", "float", "decimal", "byte", "char",
            "short", "long", "uint", "ulong", "ushort", "sbyte", "DateTime", "TimeSpan",
            "Array", "List", "Dictionary", "IEnumerable", "IList", "IDictionary",
            "Console", "Math", "Convert", "Exception", "ArgumentException", "InvalidOperationException"
        };

        public static readonly HashSet<string> BaseSystemNamespaces = new()
        {
            "System", "System.Collections.Generic", "System.Linq", "System.Text",
            "System.IO", "System.Threading.Tasks", "System.Text.Json", "System.Net.Http"
        };
    }

    /// <summary>
    /// Checks C# method syntax trees for validation rules
    /// </summary>
    public class MethodChecker : CSharpSyntaxWalker
    {
        private readonly HashSet<string> _classAttributes;
        private readonly bool _checkImports;
        
        public HashSet<string> UndefinedNames { get; } = new();
        public Dictionary<string, string> UsingDirectives { get; } = new();
        public HashSet<string> AssignedNames { get; } = new();
        public HashSet<string> ParameterNames { get; } = new();
        public List<string> Errors { get; } = new();
        public HashSet<string> TypeNames { get; } = new() { "object", "string", "int", "bool", "var" };
        public HashSet<string> DefinedClasses { get; } = new();

        public MethodChecker(HashSet<string> classAttributes, bool checkImports = true)
        {
            _classAttributes = classAttributes;
            _checkImports = checkImports;
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            if (node.Identifier.ValueText != null)
            {
                ParameterNames.Add(node.Identifier.ValueText);
            }
            base.VisitParameter(node);
        }

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name != null)
            {
                string namespaceName = node.Name.ToString();
                string alias = node.Alias?.Name.Identifier.ValueText ?? namespaceName.Split('.').Last();
                UsingDirectives[alias] = namespaceName;
            }
            base.VisitUsingDirective(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            AssignedNames.Add(node.Identifier.ValueText);
            base.VisitVariableDeclarator(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.Left is IdentifierNameSyntax identifier)
            {
                AssignedNames.Add(identifier.Identifier.ValueText);
            }
            base.VisitAssignmentExpression(node);
        }

        public override void VisitUsingStatement(UsingStatementSyntax node)
        {
            // Track using statement variables
            if (node.Declaration != null)
            {
                foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
                {
                    AssignedNames.Add(variable.Identifier.ValueText);
                }
            }
            base.VisitUsingStatement(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            // Track exception variables
            if (node.Declaration?.Identifier != null)
            {
                AssignedNames.Add(node.Declaration.Identifier.Value?.ToString() ?? "");
            }
            base.VisitCatchClause(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            AssignedNames.Add(node.Identifier.ValueText);
            base.VisitForEachStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            if (node.Declaration != null)
            {
                foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
                {
                    AssignedNames.Add(variable.Identifier.ValueText);
                }
            }
            base.VisitForStatement(node);
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            DefinedClasses.Add(node.Identifier.ValueText);
            base.VisitClassDeclaration(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            // Skip validation for member access on 'this'
            if (node.Expression is ThisExpressionSyntax)
            {
                return;
            }
            base.VisitMemberAccessExpression(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            string identifier = node.Identifier.ValueText;
            
            if (!IsValidIdentifier(identifier))
            {
                Errors.Add($"Name '{identifier}' is undefined.");
            }
            
            base.VisitIdentifierName(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier)
            {
                string name = identifier.Identifier.ValueText;
                if (!IsValidIdentifier(name))
                {
                    Errors.Add($"Method '{name}' is undefined.");
                }
            }
            base.VisitInvocationExpression(node);
        }

        private bool IsValidIdentifier(string identifier)
        {
            return BuiltinNames.CSharpBuiltins.Contains(identifier) ||
                   BuiltinNames.BaseSystemNamespaces.Any(ns => identifier.StartsWith(ns)) ||
                   ParameterNames.Contains(identifier) ||
                   identifier == "this" ||
                   _classAttributes.Contains(identifier) ||
                   UsingDirectives.ContainsKey(identifier) ||
                   AssignedNames.Contains(identifier) ||
                   TypeNames.Contains(identifier) ||
                   DefinedClasses.Contains(identifier);
        }
    }

    /// <summary>
    /// Validates tool attributes and structure
    /// </summary>
    public static class ToolValidator
    {
        /// <summary>
        /// Validates that a Tool class follows proper patterns
        /// </summary>
        /// <param name="toolType">Tool type to validate</param>
        /// <param name="checkImports">Whether to check imports</param>
        public static void ValidateToolAttributes(Type toolType, bool checkImports = true)
        {
            if (toolType == null)
                throw new ArgumentNullException(nameof(toolType));

            if (!typeof(Tool).IsAssignableFrom(toolType))
                throw new ArgumentException($"Type {toolType.Name} is not a Tool", nameof(toolType));

            List<string> errors = new List<string>();

            // Validate class structure using reflection
            ValidateClassStructure(toolType, errors);
            
            // Validate constructor parameters
            ValidateConstructorParameters(toolType, errors);
            
            // Validate required properties
            ValidateRequiredProperties(toolType, errors);

            // If we have source code, validate syntax
            if (checkImports)
            {
                ValidateMethodsSyntax(toolType, errors);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Tool validation failed for {toolType.Name}:\n" + string.Join("\n", errors));
            }
        }

        private static void ValidateClassStructure(Type toolType, List<string> errors)
        {
            // Check that class is public and not abstract (unless it's a base class)
            if (!toolType.IsPublic && toolType != typeof(Tool))
            {
                errors.Add("Tool class must be public");
            }

            // Validate inheritance
            if (!typeof(Tool).IsAssignableFrom(toolType))
            {
                errors.Add("Class must inherit from Tool");
            }
        }

        private static void ValidateConstructorParameters(Type toolType, List<string> errors)
        {
            ConstructorInfo[] constructors = toolType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (ConstructorInfo constructor in constructors)
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                List<ParameterInfo> nonDefaultParams = parameters.Where(p => !p.HasDefaultValue && p.ParameterType != typeof(object[])).ToList();
                
                if (nonDefaultParams.Count > 0)
                {
                    errors.Add($"Constructor parameters must have default values. Found required parameters: {string.Join(", ", nonDefaultParams.Select(p => p.Name))}");
                }
            }
        }

        private static void ValidateRequiredProperties(Type toolType, List<string> errors)
        {
            string[] requiredProperties = new[] { "Name", "Description", "Inputs", "OutputType" };
            
            foreach (string propName in requiredProperties)
            {
                PropertyInfo? property = toolType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null)
                {
                    errors.Add($"Required property '{propName}' is missing");
                    continue;
                }

                // Validate property types
                switch (propName)
                {
                    case "Name":
                    case "Description":
                    case "OutputType":
                        if (property.PropertyType != typeof(string))
                        {
                            errors.Add($"Property '{propName}' must be of type string");
                        }
                        break;
                    case "Inputs":
                        if (!typeof(Dictionary<string, Dictionary<string, object>>).IsAssignableFrom(property.PropertyType))
                        {
                            errors.Add($"Property '{propName}' must be of type Dictionary<string, Dictionary<string, object>>");
                        }
                        break;
                }
            }

            // Validate Name property value if accessible
            try
            {
                PropertyInfo? nameProperty = toolType.GetProperty("Name");
                if (nameProperty?.GetValue(Activator.CreateInstance(toolType)) is string nameValue)
                {
                    if (!IsValidToolName(nameValue))
                    {
                        errors.Add($"Tool name '{nameValue}' must be a valid identifier and not a reserved keyword");
                    }
                }
            }
            catch
            {
                // If we can't instantiate to check, skip this validation
            }
        }

        private static void ValidateMethodsSyntax(Type toolType, List<string> errors)
        {
            // This would require having access to the source code
            // In a real implementation, you might:
            // 1. Use Roslyn to parse source files
            // 2. Use reflection to analyze compiled code
            // 3. Store source code metadata with tools
            
            // For now, we'll do basic reflection-based validation
            MethodInfo[] methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            
            foreach (MethodInfo method in methods)
            {
                // Skip special methods
                if (method.IsSpecialName || method.Name.StartsWith("get_") || method.Name.StartsWith("set_"))
                    continue;

                // Validate method signatures
                if (method.Name == "Forward" || method.Name == "Call")
                {
                    ValidateToolMethodSignature(method, errors);
                }
            }
        }

        private static void ValidateToolMethodSignature(MethodInfo method, List<string> errors)
        {
            // Validate that Forward method has reasonable signature
            if (method.Name == "Forward")
            {
                ParameterInfo[] parameters = method.GetParameters();
                
                // Should have args and kwargs parameters typically
                if (parameters.Length == 0)
                {
                    errors.Add($"Method '{method.Name}' should have parameters");
                }
            }
        }

        /// <summary>
        /// Validates C# source code using Roslyn
        /// </summary>
        /// <param name="sourceCode">C# source code</param>
        /// <param name="classAttributes">Known class attributes</param>
        /// <param name="checkImports">Whether to validate imports</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateSourceCode(string sourceCode, HashSet<string> classAttributes, bool checkImports = true)
        {
            List<string> errors = new List<string>();
            
            try
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
                SyntaxNode root = tree.GetRoot();

                // Check for compilation errors first
                IEnumerable<Diagnostic> diagnostics = tree.GetDiagnostics();
                foreach (Diagnostic diagnostic in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    errors.Add($"Syntax error: {diagnostic.GetMessage()}");
                }

                if (errors.Count == 0)
                {
                    // Perform semantic validation
                    MethodChecker checker = new MethodChecker(classAttributes, checkImports);
                    checker.Visit(root);
                    errors.AddRange(checker.Errors);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse source code: {ex.Message}");
            }

            return errors;
        }

        /// <summary>
        /// Checks if a tool name is valid
        /// </summary>
        /// <param name="name">Tool name to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidToolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Must be valid C# identifier
            if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                return false;

            // Must not be a C# keyword
            HashSet<string> keywords = new HashSet<string>
            {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
                "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
                "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
                "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
                "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
                "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
                "virtual", "void", "volatile", "while"
            };

            return !keywords.Contains(name.ToLower());
        }

        /// <summary>
        /// Validates tool input specifications
        /// </summary>
        /// <param name="inputs">Input specifications to validate</param>
        /// <returns>List of validation errors</returns>
        public static List<string> ValidateInputSpecifications(Dictionary<string, Dictionary<string, object>> inputs)
        {
            List<string> errors = new List<string>();
            HashSet<string> authorizedTypes = new HashSet<string>
            {
                "string", "boolean", "integer", "number", "image", "audio", "array", "object", "any", "null"
            };

            foreach ((string inputName, Dictionary<string, object> inputSpec) in inputs)
            {
                if (!inputSpec.ContainsKey("type") || !inputSpec.ContainsKey("description"))
                {
                    errors.Add($"Input '{inputName}' must have 'type' and 'description' keys");
                    continue;
                }

                object inputType = inputSpec["type"];
                switch (inputType)
                {
                    case string singleType:
                        if (!authorizedTypes.Contains(singleType))
                        {
                            errors.Add($"Input '{inputName}' type '{singleType}' is not authorized. Must be one of: {string.Join(", ", authorizedTypes)}");
                        }
                        break;
                    case string[] multipleTypes:
                        List<string> invalidTypes = multipleTypes.Where(t => !authorizedTypes.Contains(t)).ToList();
                        if (invalidTypes.Count > 0)
                        {
                            errors.Add($"Input '{inputName}' contains invalid types: {string.Join(", ", invalidTypes)}");
                        }
                        break;
                    default:
                        errors.Add($"Input '{inputName}' type must be string or string array");
                        break;
                }
            }

            return errors;
        }
    }

    /// <summary>
    /// Class-level syntax checker for validating tool class structure
    /// </summary>
    public class ClassLevelChecker : CSharpSyntaxWalker
    {
        public HashSet<string> ClassAttributes { get; } = new();
        public HashSet<string> ComplexAttributes { get; } = new();
        public HashSet<string> NonDefaultParameters { get; } = new();
        public HashSet<string> NonLiteralDefaults { get; } = new();
        public List<string> InvalidAttributes { get; } = new();
        
        private bool _inMethod;

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Identifier.ValueText == "ctor" || node.Identifier.ValueText.Contains("ctor"))
            {
                CheckConstructorParameters(node);
            }
            
            bool oldContext = _inMethod;
            _inMethod = true;
            base.VisitMethodDeclaration(node);
            _inMethod = oldContext;
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            CheckConstructorParameters(node.ParameterList);
            
            bool oldContext = _inMethod;
            _inMethod = true;
            base.VisitConstructorDeclaration(node);
            _inMethod = oldContext;
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (!_inMethod)
            {
                ClassAttributes.Add(node.Identifier.ValueText);
                
                // Check for complex initialization
                if (node.Initializer?.Value != null && !IsSimpleLiteral(node.Initializer.Value))
                {
                    ComplexAttributes.Add(node.Identifier.ValueText);
                }

                // Validate specific attributes
                if (node.Identifier.ValueText == "Name")
                {
                    ValidateNameProperty(node);
                }
            }
            
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (!_inMethod)
            {
                foreach (VariableDeclaratorSyntax variable in node.Declaration.Variables)
                {
                    ClassAttributes.Add(variable.Identifier.ValueText);
                    
                    if (variable.Initializer?.Value != null && !IsSimpleLiteral(variable.Initializer.Value))
                    {
                        ComplexAttributes.Add(variable.Identifier.ValueText);
                    }
                }
            }
            
            base.VisitFieldDeclaration(node);
        }

        private void CheckConstructorParameters(BaseMethodDeclarationSyntax method)
        {
            if (method.ParameterList != null)
            {
                CheckConstructorParameters(method.ParameterList);
            }
        }

        private void CheckConstructorParameters(ParameterListSyntax parameterList)
        {
            foreach (ParameterSyntax parameter in parameterList.Parameters)
            {
                if (parameter.Default == null && parameter.Identifier.ValueText != "this")
                {
                    NonDefaultParameters.Add(parameter.Identifier.ValueText);
                }
                else if (parameter.Default != null && !IsSimpleLiteral(parameter.Default.Value))
                {
                    NonLiteralDefaults.Add(parameter.Identifier.ValueText);
                }
            }
        }

        private void ValidateNameProperty(PropertyDeclarationSyntax node)
        {
            if (node.Initializer?.Value is LiteralExpressionSyntax literal)
            {
                if (literal.Token.IsKind(SyntaxKind.StringLiteralToken))
                {
                    string nameValue = literal.Token.ValueText;
                    if (!ToolValidator.IsValidToolName(nameValue))
                    {
                        InvalidAttributes.Add($"Property 'Name' must be a valid identifier and not a reserved keyword, found '{nameValue}'");
                    }
                }
                else
                {
                    InvalidAttributes.Add($"Property 'Name' must be a string literal, found '{literal.Token.ValueText}'");
                }
            }
            else if (node.Initializer != null)
            {
                InvalidAttributes.Add($"Property 'Name' must be a constant string, found complex expression");
            }
        }

        private static bool IsSimpleLiteral(ExpressionSyntax expression)
        {
            return expression.IsKind(SyntaxKind.StringLiteralExpression) ||
                   expression.IsKind(SyntaxKind.NumericLiteralExpression) ||
                   expression.IsKind(SyntaxKind.TrueLiteralExpression) ||
                   expression.IsKind(SyntaxKind.FalseLiteralExpression) ||
                   expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                   expression.IsKind(SyntaxKind.ArrayCreationExpression) ||
                   expression.IsKind(SyntaxKind.ObjectCreationExpression);
        }
    }
    
    // ===============================
    // LOGGING & MONITORING
    // ===============================

    /// <summary>
    /// Agent logger for handling different types of log output
    /// </summary>
    public class AgentLogger : IAgentLogger
    {
        private readonly LogLevel _level;
        private readonly TextWriter _output;
        private readonly object _lockObject = new();

        /// <summary>
        /// Gets the current log level
        /// </summary>
        public LogLevel Level => _level;

        /// <summary>
        /// Initializes a new instance of the AgentLogger class
        /// </summary>
        /// <param name="level">The log level</param>
        /// <param name="output">The output writer (defaults to Console.Out)</param>
        public AgentLogger(LogLevel level = LogLevel.Info, TextWriter? output = null)
        {
            _level = level;
            _output = output ?? Console.Out;
        }

        /// <summary>
        /// Logs a message at the specified level
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="level">The log level</param>
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (level <= _level)
            {
                lock (_lockObject)
                {
                    _output.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{level}] {message}");
                }
            }
        }

        /// <summary>
        /// Logs an error message
        /// </summary>
        /// <param name="message">The error message</param>
        public void LogError(string message)
        {
            Log($"ERROR: {EscapeMarkdown(message)}", LogLevel.Error);
        }

        /// <summary>
        /// Logs a markdown formatted message
        /// </summary>
        /// <param name="content">The markdown content</param>
        /// <param name="title">Optional title</param>
        /// <param name="level">Log level</param>
        public void LogMarkdown(string content, string? title = null, LogLevel level = LogLevel.Info)
        {
            string message = title != null ? $"{title}\n{content}" : content;
            Log(message, level);
        }

        /// <summary>
        /// Logs code with syntax highlighting information
        /// </summary>
        /// <param name="title">The title for the code block</param>
        /// <param name="content">The code content</param>
        /// <param name="level">Log level</param>
        public void LogCode(string title, string content, LogLevel level = LogLevel.Info)
        {
            Log($"{title}\n```\n{content}\n```", level);
        }

        /// <summary>
        /// Logs a rule/separator
        /// </summary>
        /// <param name="title">The rule title</param>
        /// <param name="level">Log level</param>
        public void LogRule(string title, LogLevel level = LogLevel.Info)
        {
            Log($"━━━ {title} ━━━", level);
        }

        /// <summary>
        /// Logs a task with formatting
        /// </summary>
        /// <param name="content">Task content</param>
        /// <param name="subtitle">Task subtitle</param>
        /// <param name="title">Optional title</param>
        /// <param name="level">Log level</param>
        public void LogTask(string content, string subtitle, string? title = null, LogLevel level = LogLevel.Info)
        {
            string taskTitle = title != null ? $"New run - {title}" : "New run";
            Log($"┌─ {taskTitle}\n│  {subtitle}\n│  {EscapeMarkdown(content)}\n└─", level);
        }

        /// <summary>
        /// Logs a list of messages
        /// </summary>
        /// <param name="messages">The messages to log</param>
        /// <param name="level">Log level</param>
        public void LogMessages(List<Dictionary<string, object>> messages, LogLevel level = LogLevel.Debug)
        {
            if (level <= _level)
            {
                string json = JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true });
                Log($"Messages:\n{json}", level);
            }
        }

        /// <summary>
        /// Escapes markdown characters in text
        /// </summary>
        /// <param name="text">Text to escape</param>
        /// <returns>Escaped text</returns>
        private static string EscapeMarkdown(string text)
        {
            return text.Replace("[", "\\[").Replace("]", "\\]");
        }
    }
}