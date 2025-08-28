using Microsoft.CodeAnalysis;
using MCPCSharpRelevancy.Services.Analysis;
using NETAgents.Core;

namespace NETAgents
{
    /// <summary>
    /// Tool for analyzing C# solutions using the SolutionAnalyzer
    /// </summary>
    public class SolutionAnalysisTool : Tool, IDisposable
    {
        private readonly SolutionAnalyzer _solutionAnalyzer;

        public override string Name => "solution_analyzer";

        public override string Description => "Analyzes C# solutions to provide dependency graphs, project information, and code metrics";

        public override string OutputType => "object";

        public override Dictionary<string, Dictionary<string, object>> Inputs => new Dictionary<string, Dictionary<string, object>>
        {
            ["solution_path"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Path to the .sln file to analyze",
                ["nullable"] = false
            },
            ["include_system_types"] = new Dictionary<string, object>
            {
                ["type"] = "boolean",
                ["description"] = "Whether to include system types in the analysis",
                ["nullable"] = true
            },
            ["analysis_type"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Type of analysis to perform: basic (project info), validation (solution validation), dependency_graph (full dependency analysis), or comprehensive (all of the above)",
                ["nullable"] = true,
                ["enum"] = new[] { "basic", "validation", "dependency_graph", "comprehensive" }
            }
        };

        public SolutionAnalysisTool()
        {
            _solutionAnalyzer = new SolutionAnalyzer();
        }

        /// <summary>
        /// Override validation to handle enum values properly
        /// </summary>
        protected override void ValidateToolDefinition()
        {
            if (string.IsNullOrEmpty(Name))
                throw new InvalidOperationException("Tool name cannot be null or empty");

            if (string.IsNullOrEmpty(Description))
                throw new InvalidOperationException("Tool description cannot be null or empty");

            if (Inputs == null)
                throw new InvalidOperationException("Tool inputs cannot be null");

            if (string.IsNullOrEmpty(OutputType))
                throw new InvalidOperationException("Tool output type cannot be null or empty");

            if (!AuthorizedTypes.Contains(OutputType))
                throw new InvalidOperationException($"Output type '{OutputType}' is not authorized. Must be one of: {string.Join(", ", AuthorizedTypes)}");

            // Validate inputs
            foreach ((string inputName, Dictionary<string, object> inputSpec) in Inputs)
            {
                if (!inputSpec.ContainsKey("type") || !inputSpec.ContainsKey("description"))
                    throw new InvalidOperationException($"Input '{inputName}' must have 'type' and 'description' keys");

                object inputType = inputSpec["type"];
                if (inputType is string typeStr)
                {
                    if (!AuthorizedTypes.Contains(typeStr))
                        throw new InvalidOperationException($"Input '{inputName}' type '{typeStr}' is not authorized");
                }
                else if (inputType is string[] typeArray)
                {
                    if (typeArray.Any(t => !AuthorizedTypes.Contains(t)))
                        throw new InvalidOperationException($"Input '{inputName}' contains unauthorized types");
                }
                else
                {
                    throw new InvalidOperationException($"Input '{inputName}' type must be string or string array");
                }

                // Validate enum values if present
                if (inputSpec.TryGetValue("enum", out object? enumValue) && enumValue is string[] enumValues)
                {
                    // For enum validation, we just ensure the enum values are strings
                    // The actual validation will happen at runtime when the tool is called
                    if (enumValues.Any(v => !(v is string)))
                    {
                        throw new InvalidOperationException($"Input '{inputName}' enum values must all be strings");
                    }
                }
            }
        }

        /// <summary>
        /// Override argument validation to handle enum values
        /// </summary>
        protected override bool ValidateArgumentType(object value, Dictionary<string, object> inputSpec)
        {
            if (!inputSpec.TryGetValue("type", out object? typeObj))
                return false;

            if (value == null)
            {
                return inputSpec.TryGetValue("nullable", out object? nullableValue) && 
                       nullableValue is bool nullable && nullable;
            }

            // Check if this input has enum validation
            if (inputSpec.TryGetValue("enum", out object? enumValue) && enumValue is string[] enumValues)
            {
                // For enum inputs, validate against the allowed values
                if (value is string stringValue)
                {
                    return enumValues.Contains(stringValue);
                }
                return false;
            }

            // Fall back to base validation for non-enum inputs
            string[] expectedTypes = typeObj switch
            {
                string singleType => new[] { singleType },
                string[] multipleTypes => multipleTypes,
                _ => Array.Empty<string>()
            };

            string actualType = GetJsonSchemaType(value);
            
            return expectedTypes.Contains("any") || 
                   expectedTypes.Contains(actualType) ||
                   (actualType == "integer" && expectedTypes.Contains("number"));
        }

        protected override object? Forward(object?[]? args, Dictionary<string, object>? kwargs)
        {
            // Extract parameters
            string solutionPath = kwargs?["solution_path"]?.ToString() ?? throw new ArgumentException("solution_path is required");
            bool includeSystemTypes = kwargs?.TryGetValue("include_system_types", out object? sysTypes) == true && sysTypes is bool b && b;
            string analysisType = kwargs?.TryGetValue("analysis_type", out object? analysis) == true ? analysis?.ToString() ?? "basic" : "basic";

            try
            {
                // Load the solution
                var solution = _solutionAnalyzer.LoadSolutionAsync(solutionPath).GetAwaiter().GetResult();

                // Perform the requested analysis
                return analysisType switch
                {
                    "basic" => PerformBasicAnalysis(solution),
                    "validation" => PerformValidationAnalysis(solution),
                    "dependency_graph" => PerformDependencyAnalysis(solution, includeSystemTypes),
                    "comprehensive" => PerformComprehensiveAnalysis(solution, includeSystemTypes),
                    _ => throw new ArgumentException($"Unknown analysis type: {analysisType}")
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to analyze solution: {ex.Message}", ex);
            }
        }

        private object PerformBasicAnalysis(Solution solution)
        {
            var analysis = _solutionAnalyzer.GetSolutionAnalysis(solution);
            return new Dictionary<string, object>
            {
                ["analysis_type"] = "basic",
                ["solution_info"] = analysis.ToString(),
                ["project_count"] = analysis.ProjectCount,
                ["csharp_project_count"] = analysis.CSharpProjectCount,
                ["total_documents"] = analysis.TotalDocuments,
                ["project_names"] = analysis.ProjectNames
            };
        }

        private object PerformValidationAnalysis(Solution solution)
        {
            var validation = _solutionAnalyzer.ValidateSolution(solution);
            return new Dictionary<string, object>
            {
                ["analysis_type"] = "validation",
                ["is_valid"] = validation.IsValid,
                ["errors"] = validation.Errors,
                ["warnings"] = validation.Warnings,
                ["validation_summary"] = validation.ToString()
            };
        }

        private async Task<object> PerformDependencyAnalysis(Solution solution, bool includeSystemTypes)
        {
            var graph = await _solutionAnalyzer.AnalyzeSolutionAsync(solution, includeSystemTypes);
            
            return new Dictionary<string, object>
            {
                ["analysis_type"] = "dependency_graph",
                ["node_count"] = graph.Nodes.Count,
                ["dependency_count"] = graph.DependencyEdgeCount,
                ["metrics"] = new Dictionary<string, object>
                {
                    ["total_types"] = graph.Nodes.Count,
                    ["total_dependencies"] = graph.DependencyEdgeCount,
                    ["projects_analyzed"] = graph.Projects.Count()
                },
                ["summary"] = $"Analyzed {graph.Nodes.Count} types with {graph.DependencyEdgeCount} dependencies across {graph.Projects.Count()} projects"
            };
        }

        private async Task<object> PerformComprehensiveAnalysis(Solution solution, bool includeSystemTypes)
        {
            var basicAnalysis = PerformBasicAnalysis(solution);
            var validationAnalysis = PerformValidationAnalysis(solution);
            var dependencyAnalysis = await PerformDependencyAnalysis(solution, includeSystemTypes);

            return new Dictionary<string, object>
            {
                ["analysis_type"] = "comprehensive",
                ["basic_analysis"] = basicAnalysis,
                ["validation_analysis"] = validationAnalysis,
                ["dependency_analysis"] = dependencyAnalysis,
                ["summary"] = "Complete solution analysis completed successfully"
            };
        }

        protected override void Setup()
        {
            base.Setup();
            // Any additional setup can go here
        }

        public  void Dispose()
        {
            _solutionAnalyzer?.Dispose();
        }
    }
}