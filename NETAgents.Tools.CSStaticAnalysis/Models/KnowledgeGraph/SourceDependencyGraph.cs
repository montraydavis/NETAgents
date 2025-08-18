namespace MCPCSharpRelevancy.Models
{
    using MCPCSharpRelevancy.Services.Analysis;

    using Microsoft.CodeAnalysis;

    /// <summary>
    /// Enhanced DependencyGraph that works with source-based type analysis
    /// </summary>
    public class SourceDependencyGraph(Solution solution)
    {
        /// <summary>
        /// The solution being analyzed
        /// </summary>
        public Solution Solution { get; } = solution;

        /// <summary>
        /// All source type nodes in the graph
        /// </summary>
        public Dictionary<string, SourceTypeNode> Nodes { get; } = [];

        /// <summary>
        /// All dependencies in the graph, keyed by source type full name
        /// </summary>
        public Dictionary<string, List<SourceTypeDependency>> Dependencies { get; } = [];

        /// <summary>
        /// Flattened view of all dependency edges in the graph
        /// </summary>
        public IEnumerable<SourceTypeDependency> AllDependencies => this.Dependencies.Values.SelectMany(d => d);

        /// <summary>
        /// Total number of dependency edges across the graph
        /// </summary>
        public int DependencyEdgeCount => this.Dependencies.Values.Sum(list => list.Count);

        /// <summary>
        /// All dependents in the graph (consolidated view)
        /// </summary>
        public Dictionary<string, List<SourceTypeDependency>> Dependents { get; } = [];

        /// <summary>
        /// Cached bi-directional indexes for performance
        /// </summary>
        private Dictionary<string, List<SourceTypeDependency>>? _dependentsIndex;
        private Dictionary<string, List<SourceTypeDependency>>? _dependenciesIndex;
        private bool _indexesBuilt = false;

        /// <summary>
        /// Projects in the solution
        /// </summary>
        public IEnumerable<Project> Projects => this.Solution.Projects;

        /// <summary>
        /// Metrics about the graph
        /// </summary>
        public SourceGraphMetrics Metrics { get; private set; } = new SourceGraphMetrics();

        /// <summary>
        /// Adds a source type node to the graph
        /// </summary>
        public void AddNode(SourceTypeNode node)
        {
            string key = node.FullName;
            if (!this.Nodes.ContainsKey(key))
            {
                this.Nodes[key] = node;
            }
        }

        /// <summary>
        /// Gets a node by its full name
        /// </summary>
        public SourceTypeNode? GetNode(string fullName)
        {
            return this.Nodes.TryGetValue(fullName, out SourceTypeNode? node) ? node : null;
        }

        /// <summary>
        /// Gets a node by its symbol
        /// </summary>
        public SourceTypeNode? GetNode(INamedTypeSymbol symbol)
        {
            return this.GetNode(symbol.ToDisplayString());
        }

        /// <summary>
        /// Adds a dependency to the graph
        /// </summary>
        public void AddDependency(SourceTypeDependency dependency)
        {
            // Update graph-level dependencies dictionary (by source)
            string sourceTypeName = dependency.SourceType.ToDisplayString();
            if (!this.Dependencies.ContainsKey(sourceTypeName))
            {
                this.Dependencies[sourceTypeName] = [];
            }
            this.Dependencies[sourceTypeName].Add(dependency);

            // Update the nodes
            SourceTypeNode? sourceNode = this.GetNode(dependency.SourceType.ToDisplayString());
            SourceTypeNode? targetNode = this.GetNode(dependency.TargetType.ToDisplayString());

            sourceNode?.Dependencies.Add(dependency);

            targetNode?.Dependents.Add(dependency);

            // Update graph-level dependents dictionary
            string targetTypeName = dependency.TargetType.ToDisplayString();
            if (!this.Dependents.ContainsKey(targetTypeName))
            {
                this.Dependents[targetTypeName] = [];
            }
            this.Dependents[targetTypeName].Add(dependency);

            // Invalidate cached indexes
            this._indexesBuilt = false;
        }

        /// <summary>
        /// Gets all nodes with the most dependencies
        /// </summary>
        public List<SourceTypeNode> GetNodesWithMostDependencies(int count)
        {
            return [.. this.Nodes.Values
                .OrderByDescending(n => n.Dependencies.Count)
                .Take(count)];
        }

        /// <summary>
        /// Gets all nodes with the most dependents
        /// </summary>
        public List<SourceTypeNode> GetNodesWithMostDependents(int count)
        {
            return [.. this.Nodes.Values
                .OrderByDescending(n => n.Dependents.Count)
                .Take(count)];
        }

        /// <summary>
        /// Gets nodes by project
        /// </summary>
        public IEnumerable<SourceTypeNode> GetNodesByProject(Project project)
        {
            return this.Nodes.Values.Where(n => n.Project.Id == project.Id);
        }

        /// <summary>
        /// Gets cross-project dependencies
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetCrossProjectDependencies()
        {
            return this.AllDependencies.Where(d =>
                this.GetNode(d.SourceType.ToDisplayString())?.Project.Id !=
                this.GetNode(d.TargetType.ToDisplayString())?.Project.Id);
        }

        /// <summary>
        /// Finds circular dependencies
        /// </summary>
        public List<List<SourceTypeNode>> FindCircularDependencies()
        {
            List<List<SourceTypeNode>> cycles = [];
            HashSet<string> visited = [];
            HashSet<string> recursionStack = [];

            foreach (SourceTypeNode node in this.Nodes.Values)
            {
                if (!visited.Contains(node.FullName))
                {
                    List<SourceTypeNode> cycle = this.FindCycleDFS(node, visited, recursionStack, []);
                    if (cycle.Count != 0)
                    {
                        cycles.Add(cycle);
                    }
                }
            }

            return cycles;
        }

        private List<SourceTypeNode> FindCycleDFS(
            SourceTypeNode node,
            HashSet<string> visited,
            HashSet<string> recursionStack,
            List<SourceTypeNode> currentPath)
        {
            visited.Add(node.FullName);
            recursionStack.Add(node.FullName);
            currentPath.Add(node);

            foreach (SourceTypeDependency dependency in node.Dependencies)
            {
                SourceTypeNode? targetNode = this.GetNode(dependency.TargetType.ToDisplayString());
                if (targetNode == null)
                {
                    continue;
                }

                if (!visited.Contains(targetNode.FullName))
                {
                    List<SourceTypeNode> cycle = this.FindCycleDFS(targetNode, visited, recursionStack, [.. currentPath]);
                    if (cycle.Count != 0)
                    {
                        return cycle;
                    }
                }
                else if (recursionStack.Contains(targetNode.FullName))
                {
                    // Found a cycle
                    int cycleStart = currentPath.FindIndex(n => n.FullName == targetNode.FullName);
                    if (cycleStart >= 0)
                    {
                        return [.. currentPath.Skip(cycleStart)];
                    }
                }
            }

            recursionStack.Remove(node.FullName);
            return [];
        }

        /// <summary>
        /// Calculates graph metrics with advanced strength analysis
        /// </summary>
        public void CalculateMetrics()
        {
            // Build architectural context for advanced analysis
            ArchitecturalPatternAnalyzer patternAnalyzer = new ArchitecturalPatternAnalyzer();
            ArchitecturalContext architecturalContext = patternAnalyzer.BuildArchitecturalContext(this);

            // Calculate advanced strengths for all dependencies
            this.CalculateAdvancedStrengths(architecturalContext);

            // Calculate aggregate relationship strengths
            this.CalculateAggregateStrengths();

            // Calculate bi-directional metrics
            int totalDependents = this.Nodes.Values.Sum(n => n.Dependents.Count);
            double averageDependentsPerType = this.Nodes.Count > 0 ? (double)totalDependents / this.Nodes.Count : 0;
            double dependencyToDependentRatio = totalDependents > 0 ? (double)this.DependencyEdgeCount / totalDependents : 0;
            
            int maxFanIn = this.Nodes.Values.Count != 0 ? this.Nodes.Values.Max(n => n.Dependents.Count) : 0;
            int maxFanOut = this.Nodes.Values.Count != 0 ? this.Nodes.Values.Max(n => n.Dependencies.Count) : 0;
            
            double averageFanInScore = this.Nodes.Values.Count != 0 ? this.Nodes.Values.Average(n => n.Dependents.Count) : 0;
            double averageFanOutScore = this.Nodes.Values.Count != 0 ? this.Nodes.Values.Average(n => n.Dependencies.Count) : 0;

            // Identify high fan-in/out types and stable/unstable types
            int highFanInThreshold = Math.Max(5, (int)(averageFanInScore * 2));
            int highFanOutThreshold = Math.Max(5, (int)(averageFanOutScore * 2));
            
            int highFanInTypes = this.Nodes.Values.Count(n => n.Dependents.Count >= highFanInThreshold);
            int highFanOutTypes = this.Nodes.Values.Count(n => n.Dependencies.Count >= highFanOutThreshold);
            
            int stableTypes = this.Nodes.Values.Count(n => n.Dependents.Count > averageFanInScore && n.Dependencies.Count < averageFanOutScore);
            int unstableTypes = this.Nodes.Values.Count(n => n.Dependents.Count < averageFanInScore && n.Dependencies.Count > averageFanOutScore);

            this.Metrics = new SourceGraphMetrics
            {
                TotalTypes = this.Nodes.Count,
                TotalDependencies = this.DependencyEdgeCount,
                TotalDependents = totalDependents,
                TotalProjects = this.Projects.Count(),
                CrossProjectDependencies = this.GetCrossProjectDependencies().Count(),
                CircularDependencies = this.FindCircularDependencies().Count,
                AverageDependenciesPerType = this.Nodes.Count > 0 ? (double)this.DependencyEdgeCount / this.Nodes.Count : 0,
                AverageDependentsPerType = averageDependentsPerType,
                DependencyToDependentRatio = dependencyToDependentRatio,
                MaxDependencies = maxFanOut,
                MaxDependents = maxFanIn,
                MaxFanIn = maxFanIn,
                MaxFanOut = maxFanOut,
                AverageFanInScore = averageFanInScore,
                AverageFanOutScore = averageFanOutScore,
                HighFanInTypes = highFanInTypes,
                HighFanOutTypes = highFanOutTypes,
                StableTypes = stableTypes,
                UnstableTypes = unstableTypes,
                AverageStrength = this.DependencyEdgeCount != 0 ? this.AllDependencies.Average(d => d.EffectiveStrength) : 0,
                StrongestDependency = this.DependencyEdgeCount != 0 ? this.AllDependencies.Max(d => d.EffectiveStrength) : 0,
                WeakestDependency = this.DependencyEdgeCount != 0 ? this.AllDependencies.Min(d => d.EffectiveStrength) : 0,

                // Advanced metrics
                AverageStructuralStrength = this.CalculateAverageStructuralStrength(),
                AverageSemanticStrength = this.CalculateAverageSemanticStrength(),
                AverageCouplingStrength = this.CalculateAverageCouplingStrength(),
                AverageStabilityStrength = this.CalculateAverageStabilityStrength(),
                HighQualityCoreCount = this.CountByProfile(StrengthProfile.HighQualityCore),
                HighRiskCouplingCount = this.CountByProfile(StrengthProfile.HighRiskCoupling),
                TestingChallengeCount = this.CountByProfile(StrengthProfile.TestingChallenge),
                BusinessCriticalCount = this.CountByProfile(StrengthProfile.BusinessCritical)
            };
        }

        /// <summary>
        /// Calculates advanced strengths for all dependencies
        /// </summary>
        private void CalculateAdvancedStrengths(ArchitecturalContext context)
        {
            foreach (SourceTypeDependency dependency in this.AllDependencies)
            {
                dependency.CalculateAdvancedStrength(context);
            }
        }

        /// <summary>
        /// Gets dependencies by strength profile
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetDependenciesByProfile(StrengthProfile profile)
        {
            return this.AllDependencies.Where(d => d.AdvancedStrength?.Profile == profile);
        }

        /// <summary>
        /// Gets the strongest relationships using advanced strength metrics
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetStrongestAdvancedRelationships(int count = 20)
        {
            return this.AllDependencies
                .Where(d => d.AdvancedStrength != null)
                .OrderByDescending(d => d.AdvancedStrength!.CompositeStrength)
                .Take(count);
        }

        /// <summary>
        /// Gets relationships by architectural pattern
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetRelationshipsByPattern(ArchitecturalPattern pattern)
        {
            return this.AllDependencies.Where(d => d.AdvancedStrength?.DetectedPattern == pattern);
        }

        /// <summary>
        /// Gets types with highest structural strength
        /// </summary>
        public IEnumerable<(SourceTypeNode Node, double AvgStructuralStrength)> GetTypesWithHighestStructuralStrength(int count = 10)
        {
            return this.Nodes.Values
                .Where(n => n.Dependencies.Any(d => d.AdvancedStrength != null))
                .Select(n => (
                    Node: n,
                    AvgStructuralStrength: n.Dependencies
                        .Where(d => d.AdvancedStrength != null)
                        .Average(d => d.AdvancedStrength!.StructuralStrength)
                ))
                .OrderByDescending(t => t.AvgStructuralStrength)
                .Take(count);
        }

        /// <summary>
        /// Gets types with highest semantic strength
        /// </summary>
        public IEnumerable<(SourceTypeNode Node, double AvgSemanticStrength)> GetTypesWithHighestSemanticStrength(int count = 10)
        {
            return this.Nodes.Values
                .Where(n => n.Dependencies.Any(d => d.AdvancedStrength != null))
                .Select(n => (
                    Node: n,
                    AvgSemanticStrength: n.Dependencies
                        .Where(d => d.AdvancedStrength != null)
                        .Average(d => d.AdvancedStrength!.SemanticStrength)
                ))
                .OrderByDescending(t => t.AvgSemanticStrength)
                .Take(count);
        }

        /// <summary>
        /// Identifies architectural anti-patterns
        /// </summary>
        public IEnumerable<ArchitecturalAntiPattern> IdentifyAntiPatterns()
        {
            List<ArchitecturalAntiPattern> antiPatterns = [];

            // High coupling, low stability
            IEnumerable<SourceTypeDependency> highRiskDependencies = this.GetDependenciesByProfile(StrengthProfile.HighRiskCoupling);
            if (highRiskDependencies.Any())
            {
                antiPatterns.Add(new ArchitecturalAntiPattern
                {
                    Type = AntiPatternType.HighRiskCoupling,
                    Description = $"Found {highRiskDependencies.Count()} high-risk coupling relationships",
                    Severity = Severity.High,
                    Dependencies = [.. highRiskDependencies]
                });
            }

            // Low testability
            IEnumerable<SourceTypeDependency> testingChallenges = this.GetDependenciesByProfile(StrengthProfile.TestingChallenge);
            if (testingChallenges.Any())
            {
                antiPatterns.Add(new ArchitecturalAntiPattern
                {
                    Type = AntiPatternType.TestingChallenge,
                    Description = $"Found {testingChallenges.Count()} dependencies with low testability",
                    Severity = Severity.Medium,
                    Dependencies = [.. testingChallenges]
                });
            }

            // Weak connections (low structural and semantic strength)
            IEnumerable<SourceTypeDependency> weakConnections = this.GetDependenciesByProfile(StrengthProfile.WeakConnection);
            if (weakConnections.Any())
            {
                antiPatterns.Add(new ArchitecturalAntiPattern
                {
                    Type = AntiPatternType.WeakConnection,
                    Description = $"Found {weakConnections.Count()} weak connections that may indicate design issues",
                    Severity = Severity.Low,
                    Dependencies = [.. weakConnections]
                });
            }

            // Bi-directional anti-patterns
            this.IdentifyBiDirectionalAntiPatterns(antiPatterns);

            return antiPatterns;
        }

        /// <summary>
        /// Identifies bi-directional architectural anti-patterns
        /// </summary>
        private void IdentifyBiDirectionalAntiPatterns(List<ArchitecturalAntiPattern> antiPatterns)
        {
            // High fan-out types (unstable)
            IEnumerable<SourceTypeNode> highFanOutTypes = this.GetHighFanOutTypes(15);
            if (highFanOutTypes.Any())
            {
                antiPatterns.Add(new ArchitecturalAntiPattern
                {
                    Type = AntiPatternType.GodClass,
                    Description = $"Found {highFanOutTypes.Count()} types with high fan-out (unstable)",
                    Severity = Severity.High,
                    Dependencies = highFanOutTypes.SelectMany(n => n.Dependencies).ToList()
                });
            }

            // Types with no dependents (dead code)
            IEnumerable<SourceTypeNode> orphanedTypes = this.Nodes.Values.Where(n => n.Dependents.Count == 0);
            if (orphanedTypes.Any())
            {
                antiPatterns.Add(new ArchitecturalAntiPattern
                {
                    Type = AntiPatternType.DeadCode,
                    Description = $"Found {orphanedTypes.Count()} types with no dependents (potential dead code)",
                    Severity = Severity.Medium,
                    Dependencies = []
                });
            }

            // Types with circular dependencies
            List<List<SourceTypeNode>> circularDependencies = this.FindCircularDependencies();
            if (circularDependencies.Any())
            {
                antiPatterns.Add(new ArchitecturalAntiPattern
                {
                    Type = AntiPatternType.CircularDependency,
                    Description = $"Found {circularDependencies.Count} circular dependency chains",
                    Severity = Severity.High,
                    Dependencies = circularDependencies.SelectMany(cycle => 
                        cycle.SelectMany(n => n.Dependencies)).ToList()
                });
            }
        }

        /// <summary>
        /// Detects bi-directional architectural patterns
        /// </summary>
        public IEnumerable<BiDirectionalPattern> DetectBiDirectionalPatterns()
        {
            List<BiDirectionalPattern> patterns = [];

            // Stable types (high fan-in, low fan-out)
            IEnumerable<SourceTypeNode> stableTypes = this.GetStableTypes();
            if (stableTypes.Any())
            {
                patterns.Add(new BiDirectionalPattern
                {
                    Type = BiDirectionalPatternType.StableCore,
                    Description = $"Found {stableTypes.Count()} stable types (high fan-in, low fan-out)",
                    Types = [.. stableTypes],
                    Severity = Severity.Low // This is generally good
                });
            }

            // Unstable types (low fan-in, high fan-out)
            IEnumerable<SourceTypeNode> unstableTypes = this.GetUnstableTypes();
            if (unstableTypes.Any())
            {
                patterns.Add(new BiDirectionalPattern
                {
                    Type = BiDirectionalPatternType.UnstableLeaf,
                    Description = $"Found {unstableTypes.Count()} unstable types (low fan-in, high fan-out)",
                    Types = [.. unstableTypes],
                    Severity = Severity.Medium
                });
            }

            // High fan-in types (many dependents)
            IEnumerable<SourceTypeNode> highFanInTypes = this.GetHighFanInTypes(10);
            if (highFanInTypes.Any())
            {
                patterns.Add(new BiDirectionalPattern
                {
                    Type = BiDirectionalPatternType.HighFanIn,
                    Description = $"Found {highFanInTypes.Count()} types with high fan-in (many dependents)",
                    Types = [.. highFanInTypes],
                    Severity = Severity.Low
                });
            }

            return patterns;
        }

        /// <summary>
        /// Checks if a type is stable (high fan-in, low fan-out)
        /// </summary>
        public bool IsStableType(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            if (node == null)
                return false;

            double avgFanIn = this.Nodes.Values.Average(n => n.Dependents.Count);
            double avgFanOut = this.Nodes.Values.Average(n => n.Dependencies.Count);

            return node.Dependents.Count > avgFanIn && node.Dependencies.Count < avgFanOut;
        }

        /// <summary>
        /// Checks if a type is unstable (low fan-in, high fan-out)
        /// </summary>
        public bool IsUnstableType(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            if (node == null)
                return false;

            double avgFanIn = this.Nodes.Values.Average(n => n.Dependents.Count);
            double avgFanOut = this.Nodes.Values.Average(n => n.Dependencies.Count);

            return node.Dependents.Count < avgFanIn && node.Dependencies.Count > avgFanOut;
        }

        // Helper methods for advanced metrics
        private double CalculateAverageStructuralStrength()
        {
            IEnumerable<SourceTypeDependency> advancedDeps = this.AllDependencies.Where(d => d.AdvancedStrength != null);
            return advancedDeps.Any() ? advancedDeps.Average(d => d.AdvancedStrength!.StructuralStrength) : 0.0;
        }

        private double CalculateAverageSemanticStrength()
        {
            IEnumerable<SourceTypeDependency> advancedDeps = this.AllDependencies.Where(d => d.AdvancedStrength != null);
            return advancedDeps.Any() ? advancedDeps.Average(d => d.AdvancedStrength!.SemanticStrength) : 0.0;
        }

        private double CalculateAverageCouplingStrength()
        {
            IEnumerable<SourceTypeDependency> advancedDeps = this.AllDependencies.Where(d => d.AdvancedStrength != null);
            return advancedDeps.Any() ? advancedDeps.Average(d => d.AdvancedStrength!.CouplingStrength) : 0.0;
        }

        private double CalculateAverageStabilityStrength()
        {
            IEnumerable<SourceTypeDependency> advancedDeps = this.AllDependencies.Where(d => d.AdvancedStrength != null);
            return advancedDeps.Any() ? advancedDeps.Average(d => d.AdvancedStrength!.StabilityStrength) : 0.0;
        }

        private int CountByProfile(StrengthProfile profile)
        {
            return this.AllDependencies.Count(d => d.AdvancedStrength?.Profile == profile);
        }

        /// <summary>
        /// Calculates aggregate relationship strengths between types
        /// </summary>
        private void CalculateAggregateStrengths()
        {
            // Group dependencies by type pairs to calculate aggregate strengths
            var typePairs = this.AllDependencies.GroupBy(d => new
            {
                Source = d.SourceType.ToDisplayString(),
                Target = d.TargetType.ToDisplayString()
            });

            foreach (var pair in typePairs)
            {
                List<SourceTypeDependency> dependencies = [.. pair];
                if (dependencies.Count > 1)
                {
                    // Calculate aggregate strength for multiple dependencies between same types
                    double aggregateStrength = this.CalculateAggregateStrength(dependencies);

                    // Apply the aggregate strength to all dependencies in the pair
                    foreach (SourceTypeDependency? dep in dependencies)
                    {
                        dep.Strength = Math.Max(dep.Strength, aggregateStrength);
                    }
                }
            }
        }

        /// <summary>
        /// Calculates aggregate strength for multiple dependencies between the same types
        /// </summary>
        private double CalculateAggregateStrength(List<SourceTypeDependency> dependencies)
        {
            // Frequency bonus - more dependencies = stronger relationship
            double frequencyBonus = Math.Min(0.3, dependencies.Count * 0.05);

            // Diversity bonus - different types of dependencies = stronger relationship
            double diversityBonus = Math.Min(0.2, dependencies.GroupBy(d => d.DependencyType).Count() * 0.03);

            // Weight-based strength calculation
            double totalWeight = dependencies.Sum(d => d.Weight);
            double maxWeight = dependencies.Max(d => d.Weight);
            double weightFactor = Math.Min(1.0, (totalWeight / maxWeight) / 10.0);

            return Math.Min(1.0, weightFactor + frequencyBonus + diversityBonus);
        }

        /// <summary>
        /// Gets the strongest relationships in the graph
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetStrongestRelationships(int count = 20)
        {
            return this.AllDependencies.OrderByDescending(d => d.Strength).Take(count);
        }

        /// <summary>
        /// Gets all dependents of a specific type
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetDependentsOf(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            return node?.Dependents ?? [];
        }

        /// <summary>
        /// Gets all dependents of a specific type using the graph-level dictionary
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetDependentsFromGraph(string typeName)
        {
            return this.Dependents.GetValueOrDefault(typeName, []);
        }

        /// <summary>
        /// Gets all types that have dependents
        /// </summary>
        public IEnumerable<string> GetTypesWithDependents()
        {
            return this.Dependents.Keys;
        }

        /// <summary>
        /// Gets the count of dependents for a specific type
        /// </summary>
        public int GetDependentCount(string typeName)
        {
            return this.Dependents.GetValueOrDefault(typeName, []).Count;
        }

        /// <summary>
        /// Gets all dependents across the entire graph
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetAllDependents()
        {
            return this.Dependents.Values.SelectMany(dependents => dependents);
        }

        /// <summary>
        /// Gets all dependencies of a specific type
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetDependenciesOf(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            return node?.Dependencies ?? [];
        }

        /// <summary>
        /// Gets the fan-in score (number of dependents) for a type
        /// </summary>
        public double GetFanInScore(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            return node?.Dependents.Count ?? 0;
        }

        /// <summary>
        /// Gets the fan-out score (number of dependencies) for a type
        /// </summary>
        public double GetFanOutScore(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            return node?.Dependencies.Count ?? 0;
        }

        /// <summary>
        /// Gets types with high fan-in (many dependents)
        /// </summary>
        public IEnumerable<SourceTypeNode> GetHighFanInTypes(int threshold = 10)
        {
            return this.Nodes.Values.Where(n => n.Dependents.Count >= threshold);
        }

        /// <summary>
        /// Gets types with high fan-out (many dependencies)
        /// </summary>
        public IEnumerable<SourceTypeNode> GetHighFanOutTypes(int threshold = 10)
        {
            return this.Nodes.Values.Where(n => n.Dependencies.Count >= threshold);
        }

        /// <summary>
        /// Gets stable types (high fan-in, low fan-out)
        /// </summary>
        public IEnumerable<SourceTypeNode> GetStableTypes()
        {
            double avgFanIn = this.Nodes.Values.Average(n => n.Dependents.Count);
            double avgFanOut = this.Nodes.Values.Average(n => n.Dependencies.Count);
            
            return this.Nodes.Values.Where(n => 
                n.Dependents.Count > avgFanIn && n.Dependencies.Count < avgFanOut);
        }

        /// <summary>
        /// Gets unstable types (low fan-in, high fan-out)
        /// </summary>
        public IEnumerable<SourceTypeNode> GetUnstableTypes()
        {
            double avgFanIn = this.Nodes.Values.Average(n => n.Dependents.Count);
            double avgFanOut = this.Nodes.Values.Average(n => n.Dependencies.Count);
            
            return this.Nodes.Values.Where(n => 
                n.Dependents.Count < avgFanIn && n.Dependencies.Count > avgFanOut);
        }

        /// <summary>
        /// Gets the impact scope of a type (what would break if this type changes)
        /// </summary>
        public List<SourceTypeNode> GetImpactScope(string typeName)
        {
            List<SourceTypeNode> impactScope = [];
            HashSet<string> visited = [];
            
            this.GetImpactScopeRecursive(typeName, impactScope, visited);
            return impactScope;
        }

        /// <summary>
        /// Recursively builds the impact scope
        /// </summary>
        private void GetImpactScopeRecursive(string typeName, List<SourceTypeNode> impactScope, HashSet<string> visited)
        {
            if (visited.Contains(typeName))
                return;

            visited.Add(typeName);
            SourceTypeNode? node = this.GetNode(typeName);
            
            if (node == null)
                return;

            impactScope.Add(node);

            // Add all direct dependents
            foreach (SourceTypeDependency dependent in node.Dependents)
            {
                string dependentTypeName = dependent.SourceType.ToDisplayString();
                if (!visited.Contains(dependentTypeName))
                {
                    this.GetImpactScopeRecursive(dependentTypeName, impactScope, visited);
                }
            }
        }

        /// <summary>
        /// Gets the dependency scope of a type (what this type depends on)
        /// </summary>
        public List<SourceTypeNode> GetDependencyScope(string typeName)
        {
            List<SourceTypeNode> dependencyScope = [];
            HashSet<string> visited = [];
            
            this.GetDependencyScopeRecursive(typeName, dependencyScope, visited);
            return dependencyScope;
        }

        /// <summary>
        /// Recursively builds the dependency scope
        /// </summary>
        private void GetDependencyScopeRecursive(string typeName, List<SourceTypeNode> dependencyScope, HashSet<string> visited)
        {
            if (visited.Contains(typeName))
                return;

            visited.Add(typeName);
            SourceTypeNode? node = this.GetNode(typeName);
            
            if (node == null)
                return;

            dependencyScope.Add(node);

            // Add all direct dependencies
            foreach (SourceTypeDependency dependency in node.Dependencies)
            {
                string dependencyTypeName = dependency.TargetType.ToDisplayString();
                if (!visited.Contains(dependencyTypeName))
                {
                    this.GetDependencyScopeRecursive(dependencyTypeName, dependencyScope, visited);
                }
            }
        }

        /// <summary>
        /// Gets the impact score (how many types would be affected if this type changes)
        /// </summary>
        public int GetImpactScore(string typeName)
        {
            return this.GetImpactScope(typeName).Count;
        }

        /// <summary>
        /// Gets the dependency score (how many types this type depends on)
        /// </summary>
        public int GetDependencyScore(string typeName)
        {
            return this.GetDependencyScope(typeName).Count;
        }

        /// <summary>
        /// Finds dependency chain from source to target
        /// </summary>
        public List<SourceTypeNode> FindDependencyChain(string sourceType, string targetType)
        {
            return this.FindPath(sourceType, targetType, true);
        }

        /// <summary>
        /// Finds dependent chain from source to target
        /// </summary>
        public List<SourceTypeNode> FindDependentChain(string sourceType, string targetType)
        {
            return this.FindPath(sourceType, targetType, false);
        }

        /// <summary>
        /// Finds all paths between two types
        /// </summary>
        public List<List<SourceTypeNode>> FindAllPaths(string sourceType, string targetType)
        {
            List<List<SourceTypeNode>> allPaths = [];
            HashSet<string> visited = [];
            
            this.FindAllPathsRecursive(sourceType, targetType, [], allPaths, visited);
            return allPaths;
        }

        /// <summary>
        /// Recursively finds all paths between two types
        /// </summary>
        private void FindAllPathsRecursive(
            string currentType, 
            string targetType, 
            List<SourceTypeNode> currentPath, 
            List<List<SourceTypeNode>> allPaths, 
            HashSet<string> visited)
        {
            if (currentPath.Any(n => n.FullName == currentType))
                return; // Avoid cycles

            SourceTypeNode? currentNode = this.GetNode(currentType);
            if (currentNode == null)
                return;

            currentPath.Add(currentNode);

            if (currentType == targetType)
            {
                allPaths.Add([.. currentPath]);
            }
            else
            {
                // Try both dependencies and dependents
                foreach (SourceTypeDependency dependency in currentNode.Dependencies)
                {
                    string nextType = dependency.TargetType.ToDisplayString();
                    this.FindAllPathsRecursive(nextType, targetType, currentPath, allPaths, visited);
                }

                foreach (SourceTypeDependency dependent in currentNode.Dependents)
                {
                    string nextType = dependent.SourceType.ToDisplayString();
                    this.FindAllPathsRecursive(nextType, targetType, currentPath, allPaths, visited);
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
        }

        /// <summary>
        /// Gets the dependency distance between two types
        /// </summary>
        public int GetDependencyDistance(string sourceType, string targetType)
        {
            List<SourceTypeNode> path = this.FindDependencyChain(sourceType, targetType);
            return path.Count > 0 ? path.Count - 1 : -1; // -1 means no path found
        }

        /// <summary>
        /// Generic path finding method
        /// </summary>
        private List<SourceTypeNode> FindPath(string sourceType, string targetType, bool useDependencies)
        {
            SourceTypeNode? sourceNode = this.GetNode(sourceType);
            SourceTypeNode? targetNode = this.GetNode(targetType);

            if (sourceNode == null || targetNode == null)
                return [];

            // Use BFS to find the shortest path
            Queue<List<SourceTypeNode>> queue = new();
            HashSet<string> visited = [];

            queue.Enqueue([sourceNode]);
            visited.Add(sourceType);

            while (queue.Count > 0)
            {
                List<SourceTypeNode> currentPath = queue.Dequeue();
                SourceTypeNode currentNode = currentPath[^1];

                if (currentNode.FullName == targetType)
                {
                    return currentPath;
                }

                // Get next nodes based on direction
                IEnumerable<SourceTypeDependency> nextEdges = useDependencies ? 
                    currentNode.Dependencies : currentNode.Dependents;

                foreach (SourceTypeDependency edge in nextEdges)
                {
                    string nextTypeName = useDependencies ? 
                        edge.TargetType.ToDisplayString() : edge.SourceType.ToDisplayString();

                    if (!visited.Contains(nextTypeName))
                    {
                        SourceTypeNode? nextNode = this.GetNode(nextTypeName);
                        if (nextNode != null)
                        {
                            visited.Add(nextTypeName);
                            List<SourceTypeNode> newPath = [.. currentPath, nextNode];
                            queue.Enqueue(newPath);
                        }
                    }
                }
            }

            return []; // No path found
        }

        /// <summary>
        /// Gets the weakest relationships in the graph
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetWeakestRelationships(int count = 20)
        {
            return this.AllDependencies.OrderBy(d => d.Strength).Take(count);
        }

        /// <summary>
        /// Gets types with the strongest overall dependency relationships
        /// </summary>
        public IEnumerable<(SourceTypeNode Node, double AverageStrength)> GetTypesWithStrongestRelationships(int count = 10)
        {
            return this.Nodes.Values
                .Where(n => n.Dependencies.Count != 0)
                .Select(n => (Node: n, AverageStrength: n.Dependencies.Average(d => d.Strength)))
                .OrderByDescending(t => t.AverageStrength)
                .Take(count);
        }

        /// <summary>
        /// Gets relationship strength between two specific types
        /// </summary>
        public double GetRelationshipStrength(string sourceType, string targetType)
        {
            SourceTypeNode? sourceNode = this.GetNode(sourceType);
            return sourceNode == null ? 0.0 : sourceNode.CalculateRelationshipStrength(targetType);
        }

        /// <summary>
        /// Generates a comprehensive bi-directional analysis report
        /// </summary>
        public string GenerateBiDirectionalReport()
        {
            this.CalculateMetrics(); // Ensure metrics are up to date

            return $"""
                ========================================
                BI-DIRECTIONAL DEPENDENCY ANALYSIS REPORT
                ========================================
                
                {this.Metrics}
                
                ARCHITECTURAL PATTERNS:
                {string.Join("\n", this.DetectBiDirectionalPatterns().Select(p => $"  - {p}"))}
                
                ANTI-PATTERNS:
                {string.Join("\n", this.IdentifyAntiPatterns().Select(a => $"  - {a}"))}
                
                TOP 10 HIGH FAN-IN TYPES:
                {string.Join("\n", this.GetHighFanInTypes(10).Take(10).Select(n => $"  - {n.Name} ({n.Dependents.Count} dependents)"))}
                
                TOP 10 HIGH FAN-OUT TYPES:
                {string.Join("\n", this.GetHighFanOutTypes(10).Take(10).Select(n => $"  - {n.Name} ({n.Dependencies.Count} dependencies)"))}
                
                STABLE TYPES (High Fan-In, Low Fan-Out):
                {string.Join("\n", this.GetStableTypes().Take(10).Select(n => $"  - {n.Name} (Fan-In: {n.Dependents.Count}, Fan-Out: {n.Dependencies.Count})"))}
                
                UNSTABLE TYPES (Low Fan-In, High Fan-Out):
                {string.Join("\n", this.GetUnstableTypes().Take(10).Select(n => $"  - {n.Name} (Fan-In: {n.Dependents.Count}, Fan-Out: {n.Dependencies.Count})"))}
                
                ========================================
                """;
        }

        /// <summary>
        /// Gets detailed bi-directional metrics for a specific type
        /// </summary>
        public Dictionary<string, object> GetTypeBiDirectionalMetrics(string typeName)
        {
            SourceTypeNode? node = this.GetNode(typeName);
            if (node == null)
                return [];

            int fanIn = node.Dependents.Count;
            int fanOut = node.Dependencies.Count;
            double avgFanIn = this.Nodes.Values.Average(n => n.Dependents.Count);
            double avgFanOut = this.Nodes.Values.Average(n => n.Dependencies.Count);

            return new Dictionary<string, object>
            {
                ["TypeName"] = typeName,
                ["FanIn"] = fanIn,
                ["FanOut"] = fanOut,
                ["AverageFanIn"] = avgFanIn,
                ["AverageFanOut"] = avgFanOut,
                ["FanInRatio"] = avgFanIn > 0 ? fanIn / avgFanIn : 0,
                ["FanOutRatio"] = avgFanOut > 0 ? fanOut / avgFanOut : 0,
                ["IsStable"] = this.IsStableType(typeName),
                ["IsUnstable"] = this.IsUnstableType(typeName),
                ["ImpactScore"] = this.GetImpactScore(typeName),
                ["DependencyScore"] = this.GetDependencyScore(typeName),
                ["TotalDependencies"] = this.GetDependenciesOf(typeName).Count(),
                ["TotalDependents"] = this.GetDependentsOf(typeName).Count(),
                ["StrongestDependency"] = node.Dependencies.Count > 0 ? node.Dependencies.Max(d => d.EffectiveStrength) : 0,
                ["WeakestDependency"] = node.Dependencies.Count > 0 ? node.Dependencies.Min(d => d.EffectiveStrength) : 0,
                ["AverageDependencyStrength"] = node.Dependencies.Count > 0 ? node.Dependencies.Average(d => d.EffectiveStrength) : 0
            };
        }

        /// <summary>
        /// Gets fan-in/fan-out analysis for all types
        /// </summary>
        public IEnumerable<(string TypeName, int FanIn, int FanOut, double FanInRatio, double FanOutRatio, bool IsStable, bool IsUnstable)> GetFanInOutAnalysis()
        {
            double avgFanIn = this.Nodes.Values.Average(n => n.Dependents.Count);
            double avgFanOut = this.Nodes.Values.Average(n => n.Dependencies.Count);

            return this.Nodes.Values.Select(n => (
                TypeName: n.FullName,
                FanIn: n.Dependents.Count,
                FanOut: n.Dependencies.Count,
                FanInRatio: avgFanIn > 0 ? n.Dependents.Count / avgFanIn : 0,
                FanOutRatio: avgFanOut > 0 ? n.Dependencies.Count / avgFanOut : 0,
                IsStable: this.IsStableType(n.FullName),
                IsUnstable: this.IsUnstableType(n.FullName)
            )).OrderByDescending(t => t.FanIn + t.FanOut);
        }

        /// <summary>
        /// Builds bi-directional indexes for performance optimization
        /// </summary>
        public void BuildBiDirectionalIndexes()
        {
            this._dependentsIndex = [];
            this._dependenciesIndex = [];

            foreach (SourceTypeDependency dependency in this.AllDependencies)
            {
                string sourceType = dependency.SourceType.ToDisplayString();
                string targetType = dependency.TargetType.ToDisplayString();

                // Build dependents index (who depends on this type)
                if (!this._dependentsIndex.ContainsKey(targetType))
                {
                    this._dependentsIndex[targetType] = [];
                }
                this._dependentsIndex[targetType].Add(dependency);

                // Build dependencies index (what this type depends on)
                if (!this._dependenciesIndex.ContainsKey(sourceType))
                {
                    this._dependenciesIndex[sourceType] = [];
                }
                this._dependenciesIndex[sourceType].Add(dependency);
            }

            this._indexesBuilt = true;
        }

        /// <summary>
        /// Gets dependents using cached index for better performance
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetDependentsOfOptimized(string typeName)
        {
            if (!this._indexesBuilt)
            {
                this.BuildBiDirectionalIndexes();
            }

            return this._dependentsIndex?.GetValueOrDefault(typeName, []) ?? [];
        }

        /// <summary>
        /// Gets dependencies using cached index for better performance
        /// </summary>
        public IEnumerable<SourceTypeDependency> GetDependenciesOfOptimized(string typeName)
        {
            if (!this._indexesBuilt)
            {
                this.BuildBiDirectionalIndexes();
            }

            return this._dependenciesIndex?.GetValueOrDefault(typeName, []) ?? [];
        }

        /// <summary>
        /// Clears cached indexes (call when graph structure changes)
        /// </summary>
        public void ClearCachedIndexes()
        {
            this._dependentsIndex = null;
            this._dependenciesIndex = null;
            this._indexesBuilt = false;
        }

        /// <summary>
        /// Finds the shortest path between two types based on relationship strength
        /// </summary>
        public List<SourceTypeNode> FindStrongestPath(string sourceType, string targetType)
        {
            SourceTypeNode? sourceNode = this.GetNode(sourceType);
            SourceTypeNode? targetNode = this.GetNode(targetType);

            if (sourceNode == null || targetNode == null)
            {
                return [];
            }

            // Use Dijkstra's algorithm with inverse strength as distance (stronger = shorter distance)
            Dictionary<string, double> distances = [];
            Dictionary<string, SourceTypeNode> previous = [];
            HashSet<string> unvisited = [];

            foreach (SourceTypeNode node in this.Nodes.Values)
            {
                distances[node.FullName] = double.MaxValue;
                unvisited.Add(node.FullName);
            }

            distances[sourceNode.FullName] = 0;

            while (unvisited.Count > 0)
            {
                string? current = unvisited.OrderBy(u => distances[u]).FirstOrDefault();
                if (current == null || distances[current] == double.MaxValue)
                {
                    break;
                }

                unvisited.Remove(current);

                if (current == targetNode.FullName)
                {
                    // Reconstruct path
                    List<SourceTypeNode> path = [];
                    SourceTypeNode step = targetNode;

                    while (step != null)
                    {
                        path.Insert(0, step);
                        if (previous.TryGetValue(step.FullName, out SourceTypeNode? value))
                        {
                            step = value;
                        }
                        else
                        {
                            break;
                        }
                    }

                    return path;
                }

                SourceTypeNode? currentNode = this.GetNode(current);
                if (currentNode != null)
                {
                    foreach (SourceTypeDependency dependency in currentNode.Dependencies)
                    {
                        string neighbor = dependency.TargetType.ToDisplayString();
                        if (!unvisited.Contains(neighbor))
                        {
                            continue;
                        }

                        // Distance is inverse of strength (stronger relationships = shorter distance)
                        double distance = distances[current] + (1.0 - dependency.Strength);

                        if (distance < distances[neighbor])
                        {
                            distances[neighbor] = distance;
                            previous[neighbor] = currentNode;
                        }
                    }
                }
            }

            return [];
        }
    }

    /// <summary>
    /// Represents a dependency between two source types with advanced relationship strength
    /// </summary>
    public class SourceTypeDependency
    {
        public INamedTypeSymbol SourceType { get; }
        public INamedTypeSymbol TargetType { get; }
        public SourceDependencyType DependencyType { get; }
        public string? MemberName { get; }
        public Location? Location { get; }
        public Document? Document { get; }
        public int Weight { get; }
        public double Strength { get; set; }
        public AdvancedRelationshipStrength? AdvancedStrength { get; set; }
        public List<string> Usages { get; set; } = [];
        public string? FullName
        {
            get
            {
                bool depType = this.DependencyType == SourceDependencyType.Parameter;

                return depType == true ? this.SourceType.ToString() : this.SourceType.ToString();
            }
        }

        public SourceTypeDependency(
            INamedTypeSymbol sourceType,
            INamedTypeSymbol targetType,
            SourceDependencyType dependencyType,
            string? memberName = null,
            Location? location = null,
            Document? document = null,
            List<string>? usages = null)
        {
            this.SourceType = sourceType;
            this.TargetType = targetType;
            this.DependencyType = dependencyType;
            this.MemberName = memberName;
            this.Location = location;
            this.Document = document;
            this.Weight = GetDependencyWeight(dependencyType);
            this.Strength = this.CalculateBaseStrength();
            this.Usages = usages ?? [];
        }

        /// <summary>
        /// Calculates advanced strength using the multi-dimensional analysis
        /// </summary>
        public void CalculateAdvancedStrength(ArchitecturalContext context)
        {
            AdvancedStrengthCalculator calculator = new AdvancedStrengthCalculator();
            this.AdvancedStrength = calculator.CalculateAdvancedStrength(this, context);

            // Update the basic strength with the composite strength
            this.Strength = this.AdvancedStrength.CompositeStrength;
        }

        /// <summary>
        /// Gets the effective strength - uses advanced strength if available, otherwise basic strength
        /// </summary>
        public double EffectiveStrength => this.AdvancedStrength?.CompositeStrength ?? this.Strength;

        /// <summary>
        /// Gets detailed strength information
        /// </summary>
        public string GetStrengthDetails()
        {
            return this.AdvancedStrength != null
                ? this.AdvancedStrength.ToString()
                : $"Basic Strength: {this.Strength:F2} (Weight: {this.Weight})";
        }

        /// <summary>
        /// Gets the base weight for different dependency types
        /// </summary>
        private static int GetDependencyWeight(SourceDependencyType dependencyType)
        {
            return dependencyType switch
            {
                SourceDependencyType.Inheritance => 10,
                SourceDependencyType.Interface => 9,
                SourceDependencyType.Constructor => 8,
                SourceDependencyType.Field => 7,
                SourceDependencyType.Property => 6,
                SourceDependencyType.Method => 4,
                SourceDependencyType.ReturnType => 5,
                SourceDependencyType.Parameter => 3,
                SourceDependencyType.LocalVariable => 2,
                SourceDependencyType.NewExpression => 5,
                SourceDependencyType.GenericArgument => 4,
                SourceDependencyType.Attribute => 1,
                SourceDependencyType.Event => 3,
                SourceDependencyType.CastOperation => 1,
                SourceDependencyType.Delegate => 4,
                SourceDependencyType.UsingDirective => 1,
                SourceDependencyType.StaticReference => 3,
                // Added for method body analysis
                SourceDependencyType.MethodCall => 4,
                SourceDependencyType.FieldAccess => 3,
                SourceDependencyType.PropertyAccess => 3,
                SourceDependencyType.LocalVariableType => 2,
                _ => 1
            };
        }

        /// <summary>
        /// Calculates the base strength for this dependency
        /// </summary>
        private double CalculateBaseStrength()
        {
            // Normalize weight to 0-1 scale
            double normalizedWeight = this.Weight / 10.0;

            // Add bonus for certain patterns
            double bonus = 0.0;

            // Constructor injection indicates strong dependency
            if (this.DependencyType == SourceDependencyType.Constructor)
            {
                bonus += 0.2;
            }

            // Property dependencies often indicate core relationships
            if (this.DependencyType == SourceDependencyType.Property || this.DependencyType == SourceDependencyType.Field)
            {
                bonus += 0.1;
            }

            // Inheritance is the strongest relationship
            if (this.DependencyType == SourceDependencyType.Inheritance || this.DependencyType == SourceDependencyType.Interface)
            {
                bonus += 0.3;
            }

            return Math.Min(1.0, normalizedWeight + bonus);
        }

        public override string ToString()
        {
            string member = string.IsNullOrEmpty(this.MemberName) ? "" : $".{this.MemberName}";
            string location = this.Location != null ? $" at {this.Location.GetLineSpan().StartLinePosition}" : "";

            return this.AdvancedStrength != null
                ? $"{this.SourceType.Name}{member} -> {this.TargetType.Name} ({this.DependencyType}) " +
                       $"[Composite: {this.AdvancedStrength.CompositeStrength:F2}, Profile: {this.AdvancedStrength.Profile}]{location}"
                : $"{this.SourceType.Name}{member} -> {this.TargetType.Name} ({this.DependencyType}) [Strength: {this.Strength:F2}]{location}";
        }
    }

    /// <summary>
    /// Types of dependencies in source code analysis
    /// </summary>
    public enum SourceDependencyType
    {
        Inheritance,
        Interface,
        Field,
        Property,
        Method,
        Constructor,
        Parameter,
        ReturnType,
        LocalVariable,
        GenericArgument,
        Attribute,
        UsingDirective,
        NamespaceReference,
        StaticReference,
        CastOperation,
        TypeOfExpression,
        IsExpression,
        AsExpression,
        NewExpression,
        ArrayCreation,
        Delegate,
        Event,
        IndexerAccess,
        ExtensionMethod,
        AnonymousType,
        Lambda,
        LinqExpression,
        // Added for method body analysis
        MethodCall,
        FieldAccess,
        PropertyAccess,
        LocalVariableType
    }

    /// <summary>
    /// Metrics about the source dependency graph
    /// </summary>
    public class SourceGraphMetrics
    {
        public int TotalTypes { get; set; }
        public int TotalDependencies { get; set; }
        public int TotalProjects { get; set; }
        public int CrossProjectDependencies { get; set; }
        public int CircularDependencies { get; set; }
        public double AverageDependenciesPerType { get; set; }
        public int MaxDependencies { get; set; }
        public int MaxDependents { get; set; }
        public double AverageStrength { get; set; }
        public double StrongestDependency { get; set; }
        public double WeakestDependency { get; set; }
        public double AverageStructuralStrength { get; set; }
        public double AverageSemanticStrength { get; set; }
        public double AverageCouplingStrength { get; set; }
        public double AverageStabilityStrength { get; set; }
        public int HighQualityCoreCount { get; set; }
        public int HighRiskCouplingCount { get; set; }
        public int TestingChallengeCount { get; set; }
        public int BusinessCriticalCount { get; set; }

        public int TotalDependents { get; set; }
        public double AverageDependentsPerType { get; set; }
        public double DependencyToDependentRatio { get; set; }
        public int HighFanInTypes { get; set; }  // Types with many dependents
        public int HighFanOutTypes { get; set; } // Types with many dependencies
        public int StableTypes { get; set; }     // High fan-in, low fan-out
        public int UnstableTypes { get; set; }   // Low fan-in, high fan-out
        public double AverageFanInScore { get; set; }
        public double AverageFanOutScore { get; set; }
        public int MaxFanIn { get; set; }
        public int MaxFanOut { get; set; }

        public override string ToString()
        {
            return $"""
                Total Types: {this.TotalTypes}
                Total Dependencies: {this.TotalDependencies}
                Total Dependents: {this.TotalDependents}
                Total Projects: {this.TotalProjects}
                Cross-Project Dependencies: {this.CrossProjectDependencies}
                Circular Dependencies: {this.CircularDependencies}
                Average Dependencies per Type: {this.AverageDependenciesPerType:F2}
                Average Dependents per Type: {this.AverageDependentsPerType:F2}
                Dependency to Dependent Ratio: {this.DependencyToDependentRatio:F2}
                Max Dependencies: {this.MaxDependencies}
                Max Dependents: {this.MaxDependents}
                Max Fan-In: {this.MaxFanIn}
                Max Fan-Out: {this.MaxFanOut}
                Average Relationship Strength: {this.AverageStrength:F2}
                Strongest Relationship: {this.StrongestDependency:F2}
                Weakest Relationship: {this.WeakestDependency:F2}
                Average Structural Strength: {this.AverageStructuralStrength:F2}
                Average Semantic Strength: {this.AverageSemanticStrength:F2}
                Average Coupling Strength: {this.AverageCouplingStrength:F2}
                Average Stability Strength: {this.AverageStabilityStrength:F2}
                High Quality Core Count: {this.HighQualityCoreCount}
                High Risk Coupling Count: {this.HighRiskCouplingCount}
                Testing Challenge Count: {this.TestingChallengeCount}
                Business Critical Count: {this.BusinessCriticalCount}
                High Fan-In Types: {this.HighFanInTypes}
                High Fan-Out Types: {this.HighFanOutTypes}
                Stable Types: {this.StableTypes}
                Unstable Types: {this.UnstableTypes}
                Average Fan-In Score: {this.AverageFanInScore:F2}
                Average Fan-Out Score: {this.AverageFanOutScore:F2}
                """;
        }
    }

    /// <summary>
    /// Represents an architectural anti-pattern detected in the codebase
    /// </summary>
    public class ArchitecturalAntiPattern
    {
        public AntiPatternType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public Severity Severity { get; set; }
        public List<SourceTypeDependency> Dependencies { get; set; } = [];
        public string Recommendation { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{this.Type} ({this.Severity}): {this.Description} - {this.Dependencies.Count} dependencies affected";
        }
    }

    /// <summary>
    /// Types of architectural anti-patterns
    /// </summary>
    public enum AntiPatternType
    {
        HighRiskCoupling,
        TestingChallenge,
        WeakConnection,
        CircularDependency,
        LayerViolation,
        SingletonOveruse,
        GodClass,
        FeatureEnvy,
        DeadCode
    }

    /// <summary>
    /// Represents a bi-directional architectural pattern detected in the codebase
    /// </summary>
    public class BiDirectionalPattern
    {
        public BiDirectionalPatternType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public Severity Severity { get; set; }
        public List<SourceTypeNode> Types { get; set; } = [];
        public string Recommendation { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{this.Type} ({this.Severity}): {this.Description} - {this.Types.Count} types affected";
        }
    }

    /// <summary>
    /// Types of bi-directional architectural patterns
    /// </summary>
    public enum BiDirectionalPatternType
    {
        StableCore,      // High fan-in, low fan-out (good)
        UnstableLeaf,    // Low fan-in, high fan-out (concerning)
        HighFanIn,       // Many dependents
        HighFanOut,      // Many dependencies
        IsolatedNode,    // No dependencies or dependents
        BridgeNode       // Connects different parts of the system
    }

    /// <summary>
    /// Severity levels for anti-patterns
    /// </summary>
    public enum Severity
    {
        Low,
        Medium,
        High,
        Critical
    }
}