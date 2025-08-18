using Microsoft.CodeAnalysis;

namespace MCPCSharpRelevancy.Models.Tests
{
    /// <summary>
    /// Demonstrates the bi-directional dependency analysis features
    /// </summary>
    public class BiDirectionalDependencyTest
    {
        public static void DemonstrateBiDirectionalFeatures()
        {
            Console.WriteLine("=== BI-DIRECTIONAL DEPENDENCY ANALYSIS DEMO ===");
            
            // Create a mock solution and graph
            var graph = CreateMockGraph();
            
            if (graph == null)
            {
                Console.WriteLine("Failed to create mock graph");
                return;
            }

            DemonstrateGraphLevelDependents(graph);
            DemonstrateQueries(graph);
            DemonstrateMetrics(graph);
            DemonstratePatterns(graph);
        }

        private static void DemonstrateGraphLevelDependents(SourceDependencyGraph graph)
        {
            Console.WriteLine("\n--- GRAPH-LEVEL DEPENDENTS ---");
            
            // Show the graph-level Dependents dictionary
            Console.WriteLine($"Graph has {graph.Dependents.Count} types with dependents:");
            foreach (var kvp in graph.Dependents)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value.Count} dependents");
            }

            // Demonstrate GetDependentsFromGraph method
            Console.WriteLine("\nUsing GetDependentsFromGraph():");
            foreach (var typeName in graph.GetTypesWithDependents())
            {
                var dependents = graph.GetDependentsFromGraph(typeName);
                Console.WriteLine($"  {typeName}: {dependents.Count()} dependents");
            }

            // Demonstrate GetDependentCount method
            Console.WriteLine("\nUsing GetDependentCount():");
            foreach (var typeName in graph.GetTypesWithDependents())
            {
                int count = graph.GetDependentCount(typeName);
                Console.WriteLine($"  {typeName}: {count} dependents");
            }

            // Demonstrate GetAllDependents method
            Console.WriteLine($"\nTotal dependents across graph: {graph.GetAllDependents().Count()}");
        }

        private static void DemonstrateQueries(SourceDependencyGraph? graph)
        {
            if (graph == null) return;

            Console.WriteLine("\n--- BI-DIRECTIONAL QUERIES ---");
            
            // Get dependents of a specific type
            var userServiceDependents = graph.GetDependentsOf("UserService");
            Console.WriteLine($"UserService has {userServiceDependents.Count()} dependents");
            
            // Get dependencies of a specific type
            var userControllerDependencies = graph.GetDependenciesOf("UserController");
            Console.WriteLine($"UserController has {userControllerDependencies.Count()} dependencies");
            
            // Fan-in and fan-out scores
            double userServiceFanIn = graph.GetFanInScore("UserService");
            double userServiceFanOut = graph.GetFanOutScore("UserService");
            Console.WriteLine($"UserService - Fan-In: {userServiceFanIn}, Fan-Out: {userServiceFanOut}");
            
            // High fan-in types
            var highFanInTypes = graph.GetHighFanInTypes(2);
            Console.WriteLine($"High Fan-In Types (≥2 dependents): {string.Join(", ", highFanInTypes.Select(t => t.Name))}");
            
            // High fan-out types
            var highFanOutTypes = graph.GetHighFanOutTypes(2);
            Console.WriteLine($"High Fan-Out Types (≥2 dependencies): {string.Join(", ", highFanOutTypes.Select(t => t.Name))}");
            
            // Stable and unstable types
            var stableTypes = graph.GetStableTypes();
            Console.WriteLine($"Stable Types: {string.Join(", ", stableTypes.Select(t => t.Name))}");
            
            var unstableTypes = graph.GetUnstableTypes();
            Console.WriteLine($"Unstable Types: {string.Join(", ", unstableTypes.Select(t => t.Name))}");
        }

        private static void DemonstrateMetrics(SourceDependencyGraph? graph)
        {
            if (graph == null) return;

            Console.WriteLine("\n--- BI-DIRECTIONAL METRICS ---");
            
            graph.CalculateMetrics();
            var metrics = graph.Metrics;
            
            Console.WriteLine($"Total Dependents: {metrics.TotalDependents}");
            Console.WriteLine($"Average Dependents per Type: {metrics.AverageDependentsPerType:F2}");
            Console.WriteLine($"Dependency to Dependent Ratio: {metrics.DependencyToDependentRatio:F2}");
            Console.WriteLine($"Max Fan-In: {metrics.MaxFanIn}");
            Console.WriteLine($"Max Fan-Out: {metrics.MaxFanOut}");
            Console.WriteLine($"High Fan-In Types: {metrics.HighFanInTypes}");
            Console.WriteLine($"High Fan-Out Types: {metrics.HighFanOutTypes}");
            Console.WriteLine($"Stable Types: {metrics.StableTypes}");
            Console.WriteLine($"Unstable Types: {metrics.UnstableTypes}");
        }

        private static void DemonstratePatterns(SourceDependencyGraph? graph)
        {
            if (graph == null) return;

            Console.WriteLine("\n--- ARCHITECTURAL PATTERNS ---");
            
            // Detect bi-directional patterns
            var patterns = graph.DetectBiDirectionalPatterns();
            foreach (var pattern in patterns)
            {
                Console.WriteLine($"Pattern: {pattern.Type} - {pattern.Description}");
            }
            
            // Identify anti-patterns
            var antiPatterns = graph.IdentifyAntiPatterns();
            foreach (var antiPattern in antiPatterns)
            {
                Console.WriteLine($"Anti-Pattern: {antiPattern.Type} ({antiPattern.Severity}) - {antiPattern.Description}");
            }
        }

        private static SourceDependencyGraph? CreateMockGraph()
        {
            try
            {
                // Create a simple mock graph for demonstration
                var workspace = new AdhocWorkspace();
                var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
                
                // Create a mock solution
                var solution = workspace.CurrentSolution;
                
                // Create the graph
                var graph = new SourceDependencyGraph(solution);
                
                // Add some mock nodes and dependencies
                AddMockData(graph);
                
                return graph;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating mock graph: {ex.Message}");
                return null;
            }
        }

        private static void AddMockData(SourceDependencyGraph graph)
        {
            // This is a simplified mock - in a real scenario, you'd parse actual code
            Console.WriteLine("Adding mock dependency data...");
            
            // Note: This is just for demonstration - in practice, you'd use actual Roslyn symbols
            // from parsed source code
        }
    }
}
