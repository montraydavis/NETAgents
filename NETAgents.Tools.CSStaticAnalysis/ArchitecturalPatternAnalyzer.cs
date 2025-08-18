namespace MCPCSharpRelevancy.Services.Analysis
{
    using MCPCSharpRelevancy.Models;

    using Microsoft.CodeAnalysis;

    /// <summary>
    /// Analyzes and detects architectural patterns in code dependencies
    /// </summary>
    public class ArchitecturalPatternAnalyzer
    {
        private readonly Dictionary<string, ArchitecturalPattern> _patternCache = [];
        private readonly Dictionary<string, LayerType> _layerCache = [];

        /// <summary>
        /// Analyzes a dependency to determine architectural patterns
        /// </summary>
        public ArchitecturalPattern AnalyzePattern(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            INamedTypeSymbol targetType = dependency.TargetType;
            INamedTypeSymbol sourceType = dependency.SourceType;

            // Check cache first
            string cacheKey = $"{sourceType.ToDisplayString()}->{targetType.ToDisplayString()}";
            if (this._patternCache.TryGetValue(cacheKey, out ArchitecturalPattern value))
            {
                return value;
            }

            ArchitecturalPattern pattern = this.DetectPattern(dependency, context);
            this._patternCache[cacheKey] = pattern;

            // Also cache individual type patterns
            this.UpdateTypePattern(context, sourceType);
            this.UpdateTypePattern(context, targetType);

            return pattern;
        }

        private ArchitecturalPattern DetectPattern(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            INamedTypeSymbol targetType = dependency.TargetType;
            INamedTypeSymbol sourceType = dependency.SourceType;
            SourceDependencyType dependencyType = dependency.DependencyType;

            // Detect by naming conventions
            string targetName = targetType.Name.ToLowerInvariant();
            string sourceName = sourceType.Name.ToLowerInvariant();
            string targetNamespace = targetType.ContainingNamespace.ToDisplayString().ToLowerInvariant();
            string sourceNamespace = sourceType.ContainingNamespace.ToDisplayString().ToLowerInvariant();

            // Repository Pattern
            if (this.IsRepositoryPattern(targetName, targetNamespace, dependencyType))
            {
                return ArchitecturalPattern.Repository;
            }

            // Factory Pattern
            if (this.IsFactoryPattern(targetName, sourceName, dependencyType))
            {
                return ArchitecturalPattern.Factory;
            }

            // Strategy Pattern
            if (this.IsStrategyPattern(dependency, targetType))
            {
                return ArchitecturalPattern.Strategy;
            }

            // Observer Pattern
            if (this.IsObserverPattern(dependency))
            {
                return ArchitecturalPattern.Observer;
            }

            // Dependency Injection Pattern
            if (this.IsDependencyInjectionPattern(dependency))
            {
                return ArchitecturalPattern.DependencyInjection;
            }

            // Domain Core
            if (this.IsDomainCore(targetNamespace, targetName))
            {
                return ArchitecturalPattern.DomainCore;
            }

            // Application Service
            if (this.IsApplicationService(targetNamespace, targetName))
            {
                return ArchitecturalPattern.ApplicationService;
            }

            // Infrastructure
            if (this.IsInfrastructure(targetNamespace, targetName))
            {
                return ArchitecturalPattern.Infrastructure;
            }

            // Data Access
            if (this.IsDataAccess(targetNamespace, targetName))
            {
                return ArchitecturalPattern.DataAccess;
            }

            // Testing
            if (this.IsTesting(targetNamespace, targetName, sourceNamespace, sourceName))
            {
                return ArchitecturalPattern.Testing;
            }

            // Configuration
            if (this.IsConfiguration(targetNamespace, targetName))
            {
                return ArchitecturalPattern.Configuration;
            }

            // Microservices Boundary
            return this.IsMicroservicesBoundary(dependency, context) ? ArchitecturalPattern.MicroservicesBoundary : ArchitecturalPattern.Unknown;
        }

        private bool IsRepositoryPattern(string targetName, string targetNamespace, SourceDependencyType dependencyType)
        {
            return (targetName.Contains("repository") ||
                    targetNamespace.Contains("repository") ||
                    targetNamespace.Contains("data") ||
                    targetNamespace.Contains("persistence")) &&
                   (dependencyType == SourceDependencyType.Interface ||
                    dependencyType == SourceDependencyType.Constructor);
        }

        private bool IsFactoryPattern(string targetName, string sourceName, SourceDependencyType dependencyType)
        {
            return (targetName.Contains("factory") ||
                    sourceName.Contains("factory") ||
                    targetName.Contains("builder") ||
                    sourceName.Contains("builder")) &&
                   (dependencyType == SourceDependencyType.Method ||
                    dependencyType == SourceDependencyType.Interface);
        }

        private bool IsStrategyPattern(SourceTypeDependency dependency, INamedTypeSymbol targetType)
        {
            return dependency.DependencyType == SourceDependencyType.Interface &&
                   (targetType.Name.ToLowerInvariant().Contains("strategy") ||
                    targetType.Name.ToLowerInvariant().Contains("handler") ||
                    targetType.Name.ToLowerInvariant().Contains("processor"));
        }

        private bool IsObserverPattern(SourceTypeDependency dependency)
        {
            return dependency.DependencyType == SourceDependencyType.Event ||
                   dependency.DependencyType == SourceDependencyType.Interface &&
                    (dependency.TargetType.Name.ToLowerInvariant().Contains("observer") ||
                     dependency.TargetType.Name.ToLowerInvariant().Contains("listener") ||
                     dependency.TargetType.Name.ToLowerInvariant().Contains("handler"));
        }

        private bool IsDependencyInjectionPattern(SourceTypeDependency dependency)
        {
            return dependency.DependencyType == SourceDependencyType.Constructor &&
                   dependency.TargetType.TypeKind == TypeKind.Interface;
        }

        private bool IsDomainCore(string namespaceName, string typeName)
        {
            return namespaceName.Contains("domain") ||
                   namespaceName.Contains("core") ||
                   namespaceName.Contains("business") ||
                   namespaceName.Contains("model") && !namespaceName.Contains("view");
        }

        private bool IsApplicationService(string namespaceName, string typeName)
        {
            return namespaceName.Contains("service") && !namespaceName.Contains("infrastructure") ||
                   namespaceName.Contains("application") ||
                   typeName.Contains("service") && !typeName.Contains("test");
        }

        private bool IsInfrastructure(string namespaceName, string typeName)
        {
            return namespaceName.Contains("infrastructure") ||
                   namespaceName.Contains("external") ||
                   namespaceName.Contains("integration") ||
                   namespaceName.Contains("persistence");
        }

        private bool IsDataAccess(string namespaceName, string typeName)
        {
            return namespaceName.Contains("data") ||
                   namespaceName.Contains("repository") ||
                   namespaceName.Contains("dal") ||
                   namespaceName.Contains("persistence") ||
                   typeName.Contains("repository") ||
                   typeName.Contains("context");
        }

        private bool IsTesting(string targetNamespace, string targetName, string sourceNamespace, string sourceName)
        {
            return targetNamespace.Contains("test") ||
                   sourceNamespace.Contains("test") ||
                   targetName.Contains("test") ||
                   sourceName.Contains("test") ||
                   targetName.Contains("mock") ||
                   sourceName.Contains("mock");
        }

        private bool IsConfiguration(string namespaceName, string typeName)
        {
            return namespaceName.Contains("configuration") ||
                   namespaceName.Contains("config") ||
                   namespaceName.Contains("settings") ||
                   typeName.Contains("configuration") ||
                   typeName.Contains("config") ||
                   typeName.Contains("settings");
        }

        private bool IsMicroservicesBoundary(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            string sourceNamespace = dependency.SourceType.ContainingNamespace.ToDisplayString();
            string targetNamespace = dependency.TargetType.ContainingNamespace.ToDisplayString();

            // If namespaces are very different, might be crossing service boundaries
            string[] sourceSegments = sourceNamespace.Split('.');
            string[] targetSegments = targetNamespace.Split('.');

            if (sourceSegments.Length > 0 && targetSegments.Length > 0)
            {
                // Different root namespaces might indicate different services
                return !sourceSegments[0].Equals(targetSegments[0], StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void UpdateTypePattern(ArchitecturalContext context, INamedTypeSymbol type)
        {
            string typeName = type.ToDisplayString();
            if (context.TypePatterns.ContainsKey(typeName))
            {
                return;
            }

            ArchitecturalPattern pattern = this.DetectTypePattern(type);
            context.TypePatterns[typeName] = pattern;

            LayerType layer = this.DetectLayerType(type);
            context.TypeLayers[typeName] = layer;
        }

        private ArchitecturalPattern DetectTypePattern(INamedTypeSymbol type)
        {
            string name = type.Name.ToLowerInvariant();
            string namespaceName = type.ContainingNamespace.ToDisplayString().ToLowerInvariant();

            return name.Contains("repository") || namespaceName.Contains("repository")
                ? ArchitecturalPattern.Repository
                : name.Contains("factory") || name.Contains("builder")
                ? ArchitecturalPattern.Factory
                : name.Contains("service") && !namespaceName.Contains("infrastructure")
                ? ArchitecturalPattern.ApplicationService
                : namespaceName.Contains("domain") || namespaceName.Contains("core")
                ? ArchitecturalPattern.DomainCore
                : namespaceName.Contains("infrastructure")
                ? ArchitecturalPattern.Infrastructure
                : namespaceName.Contains("test")
                ? ArchitecturalPattern.Testing
                : namespaceName.Contains("config") ? ArchitecturalPattern.Configuration : ArchitecturalPattern.Unknown;
        }

        private LayerType DetectLayerType(INamedTypeSymbol type)
        {
            string namespaceName = type.ContainingNamespace.ToDisplayString().ToLowerInvariant();

            return namespaceName.Contains("presentation") || namespaceName.Contains("ui") || namespaceName.Contains("web")
                ? LayerType.Presentation
                : namespaceName.Contains("application") || namespaceName.Contains("service") && !namespaceName.Contains("infrastructure")
                ? LayerType.Application
                : namespaceName.Contains("domain") || namespaceName.Contains("core") || namespaceName.Contains("business")
                ? LayerType.Domain
                : namespaceName.Contains("infrastructure") || namespaceName.Contains("external")
                ? LayerType.Infrastructure
                : namespaceName.Contains("data") || namespaceName.Contains("persistence") || namespaceName.Contains("repository")
                ? LayerType.Data
                : namespaceName.Contains("test")
                ? LayerType.Test
                : namespaceName.Contains("config") ? LayerType.Configuration : LayerType.Unknown;
        }

        /// <summary>
        /// Builds architectural context from the dependency graph
        /// </summary>
        public ArchitecturalContext BuildArchitecturalContext(SourceDependencyGraph graph)
        {
            ArchitecturalContext context = new ArchitecturalContext();

            // Calculate coupling metrics
            this.CalculateCouplingMetrics(graph, context);

            // Detect cyclic dependencies
            this.DetectCyclicDependencies(graph, context);

            // Analyze change frequency (placeholder - would need historical data)
            this.CalculateChangeFrequency(graph, context);

            return context;
        }

        private void CalculateCouplingMetrics(SourceDependencyGraph graph, ArchitecturalContext context)
        {
            // Calculate afferent coupling (Ca) - number of types that depend on this type
            foreach (SourceTypeNode node in graph.Nodes.Values)
            {
                string typeName = node.FullName;
                context.AfferentCoupling[typeName] = node.Dependents.Count;
                context.EfferentCoupling[typeName] = node.Dependencies.Count;
            }
        }

        private void DetectCyclicDependencies(SourceDependencyGraph graph, ArchitecturalContext context)
        {
            List<List<SourceTypeNode>> cycles = graph.FindCircularDependencies();
            foreach (List<SourceTypeNode> cycle in cycles)
            {
                foreach (SourceTypeNode node in cycle)
                {
                    context.CyclicTypes.Add(node.FullName);
                }
            }
        }

        private void CalculateChangeFrequency(SourceDependencyGraph graph, ArchitecturalContext context)
        {
            // Placeholder implementation - in real scenario, this would analyze:
            // - Git commit history
            // - File modification timestamps
            // - Version control metrics

            foreach (SourceTypeNode node in graph.Nodes.Values)
            {
                // For now, assign random values based on type patterns
                ArchitecturalPattern pattern = this.DetectTypePattern(node.Symbol);
                double changeFreq = pattern switch
                {
                    ArchitecturalPattern.DomainCore => 0.1,     // Domain should be stable
                    ArchitecturalPattern.Infrastructure => 0.3, // Infrastructure changes more
                    ArchitecturalPattern.ApplicationService => 0.2, // Medium change rate
                    ArchitecturalPattern.Testing => 0.5,       // Tests change frequently
                    ArchitecturalPattern.Configuration => 0.4,  // Config changes often
                    _ => 0.2
                };

                context.ChangeFrequency[node.FullName] = changeFreq;
            }
        }
    }

    /// <summary>
    /// Specialized coupling analyzer
    /// </summary>
    public class CouplingAnalyzer
    {
        public double AnalyzeCouplingIntensity(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            int afferent = context.GetAfferentCoupling(dependency.TargetType);
            int efferent = context.GetEfferentCoupling(dependency.SourceType);

            return Math.Min(1.0, (afferent + efferent) / 20.0);
        }

        public bool IsBidirectional(SourceTypeDependency dependency, SourceDependencyGraph graph)
        {
            bool reverseExists = graph.AllDependencies.Any(d =>
                d.SourceType.ToDisplayString() == dependency.TargetType.ToDisplayString() &&
                d.TargetType.ToDisplayString() == dependency.SourceType.ToDisplayString());

            return reverseExists;
        }

        public double CalculateInstability(INamedTypeSymbol type, ArchitecturalContext context)
        {
            int afferent = context.GetAfferentCoupling(type);
            int efferent = context.GetEfferentCoupling(type);

            return afferent + efferent == 0 ? 0.0 : (double)efferent / (afferent + efferent);
        }
    }
}