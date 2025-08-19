using MCPCSharpRelevancy.Extensions;
using MCPCSharpRelevancy.Models;
using MCPCSharpRelevancy.Models.KnowledgeGraph;
using NodeProcessingInfo = MCPCSharpRelevancy.Extensions.NodeProcessingInfo;

namespace MCPCSharpRelevancy.Extensions
{
    /// <summary>
    /// Extension methods for sorting dependencies in topological order
    /// </summary>
    public static class DependencySortingExtensions
    {
        /// <summary>
        /// Sorts nodes in topological order (least to most dependent).
        /// Nodes with no dependencies come first, followed by nodes that depend only on already-processed nodes.
        /// </summary>
        /// <param name="nodes">Collection of nodes to sort</param>
        /// <param name="getDependencies">Function to get dependencies for a node</param>
        /// <param name="handleCycles">How to handle circular dependencies</param>
        /// <returns>Topologically sorted list of nodes</returns>
        public static List<T> ToTopologicalOrder<T>(
            this IEnumerable<T> nodes,
            Func<T, IEnumerable<T>> getDependencies,
            CycleHandling handleCycles = CycleHandling.BreakCycles)
            where T : class
        {
            List<T> nodeList = nodes.ToList();
            List<T> result = new List<T>();
            HashSet<T> visited = new HashSet<T>();
            HashSet<T> visiting = new HashSet<T>();
            List<List<T>> cycles = new List<List<T>>();

            foreach (T node in nodeList)
            {
                if (!visited.Contains(node))
                {
                    List<T> cycle = TopologicalSortDFS(node, getDependencies, visited, visiting, result);
                    if (cycle.Any())
                    {
                        cycles.Add(cycle);
                    }
                }
            }

            // Handle cycles based on strategy
            if (cycles.Any())
            {
                switch (handleCycles)
                {
                    case CycleHandling.ThrowException:
                        throw new InvalidOperationException($"Circular dependencies detected: {string.Join(", ", cycles.SelectMany(c => c.Select(n => n.ToString())))}");
                    
                    case CycleHandling.BreakCycles:
                        result = BreakCyclesAndResort(nodeList, getDependencies, cycles);
                        break;
                    
                    case CycleHandling.IgnoreCycles:
                        // Keep the result as-is, cycles will be processed in arbitrary order
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Sorts SourceTypeNodes by dependency order (least to most dependent)
        /// </summary>
        /// <param name="nodes">Collection of SourceTypeNodes to sort</param>
        /// <param name="graph">The dependency graph containing the relationships</param>
        /// <param name="handleCycles">How to handle circular dependencies</param>
        /// <returns>Topologically sorted list of nodes</returns>
        public static List<SourceTypeNode> ToTopologicalOrder(
            this IEnumerable<SourceTypeNode> nodes,
            SourceDependencyGraph graph,
            CycleHandling handleCycles = CycleHandling.BreakCycles)
        {
            return nodes.ToTopologicalOrder(
                node => GetDependentNodes(node, graph),
                handleCycles);
        }

        /// <summary>
        /// Sorts dependencies by processing order, ensuring dependencies are processed before dependents
        /// </summary>
        /// <param name="dependencies">Collection of dependencies to sort</param>
        /// <param name="graph">The dependency graph</param>
        /// <param name="handleCycles">How to handle circular dependencies</param>
        /// <returns>Dependencies sorted by processing order</returns>
        public static List<SourceTypeDependency> ToProcessingOrder(
            this IEnumerable<SourceTypeDependency> dependencies,
            SourceDependencyGraph graph,
            CycleHandling handleCycles = CycleHandling.BreakCycles)
        {
            List<SourceTypeDependency> dependencyList = dependencies.ToList();
            
            // Get all unique nodes involved in these dependencies
            List<SourceTypeNode?> involvedNodes = dependencyList
                .SelectMany(d => new[] { 
                    graph.GetNode(d.SourceType.ToDisplayString()), 
                    graph.GetNode(d.TargetType.ToDisplayString()) 
                })
                .Where(n => n != null)
                .Distinct()
                .ToList();

            // Sort nodes topologically
            List<SourceTypeNode> sortedNodes = involvedNodes.ToTopologicalOrder(graph, handleCycles);

            // Create a lookup for node processing order
            Dictionary<string, int> nodeOrder = sortedNodes
                .Select((node, index) => new { Node = node, Order = index })
                .ToDictionary(x => x.Node.FullName, x => x.Order);

            // Sort dependencies based on the order of their target nodes (what they depend on)
            return dependencyList
                .OrderBy(d => nodeOrder.GetValueOrDefault(d.TargetType.ToDisplayString(), int.MaxValue))
                .ThenBy(d => nodeOrder.GetValueOrDefault(d.SourceType.ToDisplayString(), int.MaxValue))
                .ThenBy(d => (int)d.DependencyType) // Secondary sort by dependency type
                .ToList();
        }

        /// <summary>
        /// Groups nodes into dependency levels (0 = no dependencies, 1 = depends only on level 0, etc.)
        /// </summary>
        /// <param name="nodes">Collection of nodes to group</param>
        /// <param name="graph">The dependency graph</param>
        /// <returns>Dictionary mapping dependency level to nodes at that level</returns>
        public static Dictionary<int, List<SourceTypeNode>> GroupByDependencyLevel(
            this IEnumerable<SourceTypeNode> nodes,
            SourceDependencyGraph graph)
        {
            List<SourceTypeNode> nodeList = nodes.ToList();
            Dictionary<int, List<SourceTypeNode>> levels = new Dictionary<int, List<SourceTypeNode>>();
            Dictionary<string, int> nodeToLevel = new Dictionary<string, int>();
            
            // Continue until all nodes are assigned a level
            HashSet<SourceTypeNode> unprocessed = new HashSet<SourceTypeNode>(nodeList);
            int currentLevel = 0;

            while (unprocessed.Any())
            {
                List<SourceTypeNode> currentLevelNodes = new List<SourceTypeNode>();

                foreach (SourceTypeNode node in unprocessed.ToList())
                {
                    List<SourceTypeNode> dependencies = GetDependentNodes(node, graph).ToList();
                    
                    // Check if all dependencies are already processed (at lower levels)
                    bool canProcess = dependencies.All(dep => 
                        nodeToLevel.ContainsKey(dep.FullName) || 
                        !nodeList.Contains(dep)); // External dependencies don't count

                    if (canProcess)
                    {
                        currentLevelNodes.Add(node);
                        nodeToLevel[node.FullName] = currentLevel;
                        unprocessed.Remove(node);
                    }
                }

                if (currentLevelNodes.Any())
                {
                    levels[currentLevel] = currentLevelNodes;
                    currentLevel++;
                }
                else if (unprocessed.Any())
                {
                    // Handle circular dependencies by breaking the cycle
                    SourceTypeNode nodeToBreak = unprocessed.First();
                    currentLevelNodes.Add(nodeToBreak);
                    nodeToLevel[nodeToBreak.FullName] = currentLevel;
                    unprocessed.Remove(nodeToBreak);
                    levels[currentLevel] = currentLevelNodes;
                    currentLevel++;
                }
            }

            return levels;
        }

        /// <summary>
        /// Gets the optimal processing order with detailed information about each node
        /// </summary>
        /// <param name="nodes">Collection of nodes to analyze</param>
        /// <param name="graph">The dependency graph</param>
        /// <returns>Processing information for each node</returns>
        public static List<NodeProcessingInfo> GetProcessingPlan(
            this IEnumerable<SourceTypeNode> nodes,
            SourceDependencyGraph graph)
        {
            List<SourceTypeNode> nodeList = nodes.ToList();
            Dictionary<int, List<SourceTypeNode>> dependencyLevels = nodeList.GroupByDependencyLevel(graph);
            List<NodeProcessingInfo> processingPlan = new List<NodeProcessingInfo>();

            foreach (KeyValuePair<int, List<SourceTypeNode>> level in dependencyLevels.OrderBy(kvp => kvp.Key))
            {
                foreach (SourceTypeNode node in level.Value)
                {
                    List<SourceTypeNode> dependencies = GetDependentNodes(node, graph).ToList();
                    List<SourceTypeDependency> dependents = graph.GetDependentsOf(node.FullName).ToList();
                    
                    processingPlan.Add(new NodeProcessingInfo
                    {
                        NodeId = node.FullName,
                        NodeName = node.Name,
                        NodeType = NodeType.Type,
                        DependencyLevel = level.Key,
                        DependencyCount = dependencies.Count,
                        DependentCount = dependents.Count,
                        ProcessingOrder = processingPlan.Count,
                        IsCyclic = IsInCycle(node, graph),
                        CanProcessInParallel = CanProcessInParallel(node, level.Value, graph)
                    });
                }
            }

            return processingPlan;
        }

        // Private helper methods

        private static List<T> TopologicalSortDFS<T>(
            T node,
            Func<T, IEnumerable<T>> getDependencies,
            HashSet<T> visited,
            HashSet<T> visiting,
            List<T> result) where T : class
        {
            if (visiting.Contains(node))
            {
                // Cycle detected - return the cycle path
                return new List<T> { node };
            }

            if (visited.Contains(node))
            {
                return new List<T>();
            }

            visiting.Add(node);

            foreach (T dependency in getDependencies(node))
            {
                List<T> cycle = TopologicalSortDFS(dependency, getDependencies, visited, visiting, result);
                if (cycle.Any())
                {
                    cycle.Insert(0, node);
                    return cycle;
                }
            }

            visiting.Remove(node);
            visited.Add(node);
            result.Insert(0, node); // Insert at beginning for reverse topological order

            return new List<T>();
        }

        private static List<T> BreakCyclesAndResort<T>(
            List<T> nodes,
            Func<T, IEnumerable<T>> getDependencies,
            List<List<T>> cycles) where T : class
        {
            // Create a modified dependency function that breaks cycles
            HashSet<T> cyclicNodes = cycles.SelectMany(c => c).ToHashSet();
            HashSet<(T, T)> brokenEdges = new HashSet<(T, T)>();

            // Break cycles by removing one edge from each cycle
            foreach (List<T> cycle in cycles)
            {
                if (cycle.Count >= 2)
                {
                    // Remove the edge from the last node to the first node in the cycle
                    brokenEdges.Add((cycle.Last(), cycle.First()));
                }
            }

            Func<T, IEnumerable<T>> modifiedGetDependencies = node =>
            {
                return getDependencies(node).Where(dep => !brokenEdges.Contains((node, dep)));
            };

            // Resort with broken cycles
            List<T> result = new List<T>();
            HashSet<T> visited = new HashSet<T>();
            HashSet<T> visiting = new HashSet<T>();

            foreach (T node in nodes)
            {
                if (!visited.Contains(node))
                {
                    TopologicalSortDFS(node, modifiedGetDependencies, visited, visiting, result);
                }
            }

            return result;
        }

        private static IEnumerable<SourceTypeNode> GetDependentNodes(SourceTypeNode node, SourceDependencyGraph graph)
        {
            return node.Dependencies
                .Select(d => graph.GetNode(d.TargetType.ToDisplayString()))
                .Where(n => n != null)
                .Cast<SourceTypeNode>();
        }

        private static bool IsInCycle(SourceTypeNode node, SourceDependencyGraph graph)
        {
            List<List<SourceTypeNode>> cycles = graph.FindCircularDependencies();
            return cycles.Any(cycle => cycle.Contains(node));
        }

        private static bool CanProcessInParallel(SourceTypeNode node, List<SourceTypeNode> levelNodes, SourceDependencyGraph graph)
        {
            // Nodes can be processed in parallel if they don't depend on each other within the same level
            HashSet<SourceTypeNode> dependencies = GetDependentNodes(node, graph).ToHashSet();
            return !levelNodes.Any(otherNode => 
                otherNode != node && dependencies.Contains(otherNode));
        }
    }

    /// <summary>
    /// Strategy for handling circular dependencies during sorting
    /// </summary>
    public enum CycleHandling
    {
        /// <summary>
        /// Throw an exception when cycles are detected
        /// </summary>
        ThrowException,
        
        /// <summary>
        /// Break cycles by removing edges and continue sorting
        /// </summary>
        BreakCycles,
        
        /// <summary>
        /// Ignore cycles and process in arbitrary order
        /// </summary>
        IgnoreCycles
    }

    /// <summary>
    /// Detailed information about node processing order and characteristics
    /// </summary>
    public class NodeProcessingInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public NodeType NodeType { get; set; }
        public int DependencyLevel { get; set; }
        public int ProcessingOrder { get; set; }
        public int DependencyCount { get; set; }
        public int DependentCount { get; set; }
        public bool IsCyclic { get; set; }
        public bool CanProcessInParallel { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int UnprocessedDependencyCount { get; set; }

        public override string ToString()
        {
            return $"Order: {ProcessingOrder}, Level: {DependencyLevel}, " +
                   $"Node: {NodeName}, Dependencies: {DependencyCount}, " +
                   $"Dependents: {DependentCount}, Cyclic: {IsCyclic}";
        }
    }
}

// Usage Examples
namespace MCPCSharpRelevancy.Examples
{
    public static class DependencySortingExamples
    {
        public static void DemonstrateTopologicalSorting(SourceDependencyGraph graph)
        {
            // Example 1: Sort all nodes by dependency order
            Dictionary<string, SourceTypeNode>.ValueCollection allNodes = graph.Nodes.Values;
            List<SourceTypeNode> sortedNodes = allNodes.ToTopologicalOrder(graph);
            
            Console.WriteLine("=== Topological Sort (Least to Most Dependent) ===");
            foreach (SourceTypeNode node in sortedNodes)
            {
                int depCount = node.Dependencies.Count;
                Console.WriteLine($"{node.Name} (Dependencies: {depCount})");
            }

            // Example 2: Sort dependencies for processing
            IEnumerable<SourceTypeDependency> allDependencies = graph.AllDependencies;
            List<SourceTypeDependency> sortedDependencies = allDependencies.ToProcessingOrder(graph);
            
            Console.WriteLine("\n=== Dependency Processing Order ===");
            foreach (SourceTypeDependency dep in sortedDependencies.Take(10))
            {
                Console.WriteLine($"{dep.SourceType.Name} -> {dep.TargetType.Name} ({dep.DependencyType})");
            }

            // Example 3: Group by dependency levels
            Dictionary<int, List<SourceTypeNode>> dependencyLevels = allNodes.GroupByDependencyLevel(graph);
            
            Console.WriteLine("\n=== Dependency Levels ===");
            foreach (KeyValuePair<int, List<SourceTypeNode>> level in dependencyLevels.OrderBy(kvp => kvp.Key))
            {
                Console.WriteLine($"Level {level.Key}: {string.Join(", ", level.Value.Select(n => n.Name))}");
            }

            // Example 4: Get detailed processing plan
            List<NodeProcessingInfo> processingPlan = allNodes.GetProcessingPlan(graph);
            
            Console.WriteLine("\n=== Processing Plan ===");
            foreach (NodeProcessingInfo info in processingPlan.Take(10))
            {
                Console.WriteLine(info);
            }

            // Example 5: Process nodes in dependency order
            Console.WriteLine("\n=== Sequential Processing ===");
            foreach (SourceTypeNode node in sortedNodes)
            {
                ProcessNode(node);
            }

            // Example 6: Process by levels (can be parallelized within levels)
            Console.WriteLine("\n=== Level-by-Level Processing ===");
            foreach (KeyValuePair<int, List<SourceTypeNode>> level in dependencyLevels.OrderBy(kvp => kvp.Key))
            {
                Console.WriteLine($"Processing Level {level.Key}...");
                
                // Nodes within the same level can potentially be processed in parallel
                Parallel.ForEach(level.Value, ProcessNode);
            }
        }

        private static void ProcessNode(SourceTypeNode node)
        {
            // Your processing logic here
            Console.WriteLine($"Processing: {node.Name}");
            
            // Simulate processing time
            Thread.Sleep(10);
        }

        public static void HandleSpecificUseCase(SourceDependencyGraph graph)
        {
            // Use case: Process only types from a specific namespace in dependency order
            List<SourceTypeNode> domainTypes = graph.Nodes.Values
                .Where(n => n.Namespace.Contains("Domain"))
                .ToList();

            List<SourceTypeNode> sortedDomainTypes = domainTypes.ToTopologicalOrder(graph, CycleHandling.BreakCycles);

            Console.WriteLine("=== Domain Types Processing Order ===");
            foreach (SourceTypeNode type in sortedDomainTypes)
            {
                Console.WriteLine($"{type.FullName} -> Dependencies: {type.Dependencies.Count}");
            }

            // Use case: Get types that can be processed in parallel
            List<NodeProcessingInfo> processingPlan = domainTypes.GetProcessingPlan(graph);
            List<IGrouping<int, NodeProcessingInfo>> parallelGroups = processingPlan
                .Where(p => p.CanProcessInParallel)
                .GroupBy(p => p.DependencyLevel)
                .ToList();

            Console.WriteLine("\n=== Parallel Processing Opportunities ===");
            foreach (IGrouping<int, NodeProcessingInfo> group in parallelGroups)
            {
                Console.WriteLine($"Level {group.Key}: {group.Count()} types can be processed in parallel");
            }
        }
    }
}