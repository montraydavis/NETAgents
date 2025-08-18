namespace MCPCSharpRelevancy.Models
{
    using MCPCSharpRelevancy.Models.KnowledgeGraph;
    using MCPCSharpRelevancy.Services.Analysis;

    using Microsoft.CodeAnalysis;


    /// <summary>
    /// Information about an AI-generated summary
    /// </summary>
    public class AIGeneratedSummary
    {
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public NodeType NodeType { get; set; }
        public string Summary { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public TimeSpan GenerationTime { get; set; }
        public int TokensUsed { get; set; }
        public double Confidence { get; set; }
        public int SourceContextLength { get; set; }
    }

    /// <summary>
    /// Contains dependency information for graph traversal
    /// </summary>
    public class DependencyInfo
    {
        public Dictionary<string, List<string>> NodeDependencies { get; } = [];
        public Dictionary<string, List<string>> NodeDependents { get; } = [];
        public Dictionary<string, int> IncomingDependencyCount { get; } = [];
        public Dictionary<string, NodeProcessingState> ProcessingState { get; } = [];
    }

    /// <summary>
    /// Result of graph traversal operation
    /// </summary>
    public class GraphTraversalResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool IsSuccessful { get; set; }
        public string? ErrorMessage { get; set; }

        public int TotalNodes { get; set; }
        public int TotalEdges { get; set; }
        public int ProcessedNodeCount { get; set; }
        public int CycleGroupCount { get; set; }

        public List<ProcessingWave> ProcessingWaves { get; } = [];
        public List<string> ValidationWarnings { get; } = [];

        public TimeSpan Duration => (this.EndTime ?? DateTime.UtcNow) - this.StartTime;

        public override string ToString()
        {
            return $"Traversal: {(this.IsSuccessful ? "SUCCESS" : "FAILED")} | " +
                   $"Processed: {this.ProcessedNodeCount}/{this.TotalNodes} nodes | " +
                   $"Waves: {this.ProcessingWaves.Count} | " +
                   $"Duration: {this.Duration.TotalMilliseconds:F0}ms | " +
                   $"Cycles: {this.CycleGroupCount}";
        }
    }

    /// <summary>
    /// Represents a wave of processing during traversal
    /// </summary>
    public class ProcessingWave
    {
        public int WaveNumber { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int NodesProcessed { get; set; }
        public List<NodeProcessingInfo> ProcessedNodes { get; } = [];

        public TimeSpan Duration => (this.EndTime ?? DateTime.UtcNow) - this.StartTime;
    }

    /// <summary>
    /// Information about processing a specific node
    /// </summary>
    public class NodeProcessingInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string NodeName { get; set; } = string.Empty;
        public NodeType NodeType { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int DependencyCount { get; set; }
        public int DependentCount { get; set; }
        public bool IsCyclic { get; set; }
        public int UnprocessedDependencyCount { get; set; }
    }

    /// <summary>
    /// Represents a group of nodes in a dependency cycle
    /// </summary>
    public class CycleGroup
    {
        public int GroupId { get; set; }
        public List<string> Nodes { get; set; } = [];
    }

    /// <summary>
    /// Multi-dimensional relationship strength analysis
    /// </summary>
    public class AdvancedRelationshipStrength
    {
        public double StructuralStrength { get; set; }    // How foundational is this relationship?
        public double SemanticStrength { get; set; }      // How meaningful/purposeful?
        public double CouplingStrength { get; set; }      // How tightly coupled?
        public double StabilityStrength { get; set; }     // How stable/permanent?
        public double CriticalityStrength { get; set; }   // How critical to system function?
        public double TestabilityStrength { get; set; }   // How much it affects testability?

        public double CompositeStrength => this.CalculateComposite();
        public StrengthProfile Profile => this.DetermineProfile();
        public ArchitecturalPattern DetectedPattern { get; set; } = ArchitecturalPattern.Unknown;
        public WeightProfile Weights { get; set; } = WeightProfile.Default;

        private double CalculateComposite()
        {
            return (this.StructuralStrength * this.Weights.Structural +
                    this.SemanticStrength * this.Weights.Semantic +
                    this.CouplingStrength * this.Weights.Coupling +
                    this.StabilityStrength * this.Weights.Stability +
                    this.CriticalityStrength * this.Weights.Criticality +
                    this.TestabilityStrength * this.Weights.Testability);
        }

        private StrengthProfile DetermineProfile()
        {
            return this.StructuralStrength > 0.8 && this.SemanticStrength > 0.8
                ? StrengthProfile.HighQualityCore
                : this.CouplingStrength > 0.8 && this.StabilityStrength < 0.4
                ? StrengthProfile.HighRiskCoupling
                : this.TestabilityStrength < 0.3
                ? StrengthProfile.TestingChallenge
                : this.CriticalityStrength > 0.8
                ? StrengthProfile.BusinessCritical
                : this.StructuralStrength < 0.3 && this.SemanticStrength < 0.3 ? StrengthProfile.WeakConnection : StrengthProfile.Balanced;
        }

        public override string ToString()
        {
            return $"Composite: {this.CompositeStrength:F2} | Structural: {this.StructuralStrength:F2} | " +
                   $"Semantic: {this.SemanticStrength:F2} | Coupling: {this.CouplingStrength:F2} | " +
                   $"Stability: {this.StabilityStrength:F2} | Criticality: {this.CriticalityStrength:F2} | " +
                   $"Testability: {this.TestabilityStrength:F2} | Profile: {this.Profile}";
        }
    }

    /// <summary>
    /// Strength calculation profiles for different architectural contexts
    /// </summary>
    public enum StrengthProfile
    {
        HighQualityCore,      // Strong structural and semantic strength
        HighRiskCoupling,     // High coupling, low stability
        TestingChallenge,     // Low testability strength
        BusinessCritical,     // High criticality strength
        WeakConnection,       // Low structural and semantic
        Balanced             // Even distribution across dimensions
    }

    /// <summary>
    /// Detected architectural patterns affecting strength calculation
    /// </summary>
    public enum ArchitecturalPattern
    {
        Unknown,
        MicroservicesBoundary,
        DomainCore,
        Infrastructure,
        ApplicationService,
        DataAccess,
        Presentation,
        Testing,
        Configuration,
        Factory,
        Repository,
        Strategy,
        Observer,
        Singleton,
        DependencyInjection
    }

    /// <summary>
    /// Weight profiles for different architectural contexts
    /// </summary>
    public class WeightProfile(double structural, double semantic, double coupling,
                       double stability, double criticality, double testability)
    {
        public double Structural { get; set; } = structural;
        public double Semantic { get; set; } = semantic;
        public double Coupling { get; set; } = coupling;
        public double Stability { get; set; } = stability;
        public double Criticality { get; set; } = criticality;
        public double Testability { get; set; } = testability;

        public static WeightProfile Default => new(0.25, 0.2, 0.2, 0.15, 0.1, 0.1);
        public static WeightProfile MicroservicesBoundary => new(0.3, 0.2, 0.3, 0.1, 0.05, 0.05);
        public static WeightProfile DomainCore => new(0.2, 0.4, 0.1, 0.2, 0.05, 0.05);
        public static WeightProfile Infrastructure => new(0.4, 0.1, 0.2, 0.3, 0.0, 0.0);
        public static WeightProfile ApplicationService => new(0.2, 0.3, 0.2, 0.1, 0.1, 0.1);
        public static WeightProfile Testing => new(0.1, 0.1, 0.1, 0.1, 0.1, 0.5);
    }

    /// <summary>
    /// Context information for architectural analysis
    /// </summary>
    public class ArchitecturalContext
    {
        public Dictionary<string, ArchitecturalPattern> TypePatterns { get; } = [];
        public Dictionary<string, LayerType> TypeLayers { get; } = [];
        public Dictionary<string, int> AfferentCoupling { get; } = [];
        public Dictionary<string, int> EfferentCoupling { get; } = [];
        public HashSet<string> CyclicTypes { get; } = [];
        public Dictionary<string, double> ChangeFrequency { get; } = [];

        public ArchitecturalPattern GetPattern(INamedTypeSymbol type)
        {
            return this.TypePatterns.GetValueOrDefault(type.ToDisplayString(), ArchitecturalPattern.Unknown);
        }

        public LayerType GetLayer(INamedTypeSymbol type)
        {
            return this.TypeLayers.GetValueOrDefault(type.ToDisplayString(), LayerType.Unknown);
        }

        public int GetAfferentCoupling(INamedTypeSymbol type)
        {
            return this.AfferentCoupling.GetValueOrDefault(type.ToDisplayString(), 0);
        }

        public int GetEfferentCoupling(INamedTypeSymbol type)
        {
            return this.EfferentCoupling.GetValueOrDefault(type.ToDisplayString(), 0);
        }

        public bool IsInCycle(INamedTypeSymbol type)
        {
            return this.CyclicTypes.Contains(type.ToDisplayString());
        }

        public double GetChangeFrequency(INamedTypeSymbol type)
        {
            return this.ChangeFrequency.GetValueOrDefault(type.ToDisplayString(), 0.0);
        }
    }

    /// <summary>
    /// Architectural layer types
    /// </summary>
    public enum LayerType
    {
        Unknown,
        Presentation,
        Application,
        Domain,
        Infrastructure,
        Data,
        Test,
        Configuration
    }

    /// <summary>
    /// Advanced strength calculator implementing multi-dimensional analysis
    /// </summary>
    public class AdvancedStrengthCalculator
    {
        private readonly ArchitecturalPatternAnalyzer _patternAnalyzer;
        private readonly SemanticAnalyzer _semanticAnalyzer;
        private readonly CouplingAnalyzer _couplingAnalyzer;

        public AdvancedStrengthCalculator()
        {
            this._patternAnalyzer = new ArchitecturalPatternAnalyzer();
            this._semanticAnalyzer = new SemanticAnalyzer();
            this._couplingAnalyzer = new CouplingAnalyzer();
        }

        public AdvancedRelationshipStrength CalculateAdvancedStrength(
            SourceTypeDependency dependency,
            ArchitecturalContext context)
        {
            AdvancedRelationshipStrength strength = new AdvancedRelationshipStrength
            {
                // Phase 1: Enhanced base calculation
                StructuralStrength = this.CalculateStructuralStrength(dependency, context),
                SemanticStrength = this.CalculateSemanticStrength(dependency, context),

                // Phase 2: Multi-dimensional scoring
                CouplingStrength = this.CalculateCouplingStrength(dependency, context),
                StabilityStrength = this.CalculateStabilityStrength(dependency, context),
                CriticalityStrength = this.CalculateCriticalityStrength(dependency, context),
                TestabilityStrength = this.CalculateTestabilityStrength(dependency, context),

                // Detect architectural patterns
                DetectedPattern = this._patternAnalyzer.AnalyzePattern(dependency, context)
            };

            // Apply contextual weights
            strength.Weights = this.DetermineContextualWeights(strength.DetectedPattern, context);

            return strength;
        }

        private double CalculateStructuralStrength(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            int baseWeight = this.GetEnhancedDependencyWeight(dependency.DependencyType);

            // Contextual multipliers
            double roleMultiplier = this.GetArchitecturalRoleMultiplier(dependency, context);
            double patternMultiplier = this.GetDesignPatternMultiplier(dependency, context);
            double layerMultiplier = this.GetArchitecturalLayerMultiplier(dependency, context);
            double directionMultiplier = this.GetDependencyDirectionMultiplier(dependency, context);

            double structuralScore = (baseWeight / 10.0) * roleMultiplier * patternMultiplier * layerMultiplier * directionMultiplier;

            // Apply bonuses for good architectural practices
            if (dependency.DependencyType == SourceDependencyType.Interface)
            {
                structuralScore += 0.2;
            }

            if (dependency.DependencyType == SourceDependencyType.Constructor)
            {
                structuralScore += 0.15;
            }

            return Math.Min(1.0, structuralScore);
        }

        private double CalculateSemanticStrength(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            double purposeScore = this.AnalyzeDependencyPurpose(dependency);
            double cohesionScore = this.AnalyzeCohesionContribution(dependency, context);
            double abstractionScore = this.AnalyzeAbstractionLevel(dependency);
            double intentScore = this.AnalyzeCodeIntent(dependency, context);

            return (purposeScore + cohesionScore + abstractionScore + intentScore) / 4.0;
        }

        private double CalculateCouplingStrength(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            int afferentCoupling = context.GetAfferentCoupling(dependency.TargetType);
            int efferentCoupling = context.GetEfferentCoupling(dependency.SourceType);

            if (afferentCoupling + efferentCoupling == 0)
            {
                return 0.1;
            }

            double instability = (double)efferentCoupling / (afferentCoupling + efferentCoupling);
            double couplingIntensity = Math.Min(1.0, (afferentCoupling + efferentCoupling) / 20.0);

            // Bidirectional dependencies are stronger but more concerning
            double bidirectionalBonus = this.IsBidirectional(dependency, context) ? 0.2 : 0.0;
            double cyclicMultiplier = context.IsInCycle(dependency.SourceType) || context.IsInCycle(dependency.TargetType) ? 1.3 : 1.0;

            return Math.Min(1.0, (couplingIntensity + bidirectionalBonus) * cyclicMultiplier);
        }

        private double CalculateStabilityStrength(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            double sourceChangeFreq = context.GetChangeFrequency(dependency.SourceType);
            double targetChangeFreq = context.GetChangeFrequency(dependency.TargetType);
            double avgChangeFreq = (sourceChangeFreq + targetChangeFreq) / 2.0;

            // Interface dependencies are more stable
            double interfaceStability = dependency.DependencyType == SourceDependencyType.Interface ? 0.9 : 0.5;
            double abstractionStability = this.AnalyzeAbstractionStability(dependency);

            double stabilityScore = (interfaceStability + abstractionStability) / 2.0;
            double volatilityPenalty = Math.Min(0.5, avgChangeFreq * 0.2);

            return Math.Max(0.1, stabilityScore - volatilityPenalty);
        }

        private double CalculateCriticalityStrength(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            double executionPathCriticality = this.AnalyzeExecutionPathImportance(dependency, context);
            double businessLogicImportance = this.AnalyzeBusinessLogicRole(dependency, context);
            double failureImpact = this.AnalyzeFailureImpact(dependency, context);
            double performanceImpact = this.AnalyzePerformanceImpact(dependency, context);

            return (executionPathCriticality + businessLogicImportance + failureImpact + performanceImpact) / 4.0;
        }

        private double CalculateTestabilityStrength(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            double mockability = this.AnalyzeMockability(dependency);
            double testComplexity = this.AnalyzeTestComplexity(dependency, context);
            double isolationDifficulty = this.AnalyzeIsolationDifficulty(dependency);

            return (mockability + (1.0 - testComplexity) + (1.0 - isolationDifficulty)) / 3.0;
        }

        private WeightProfile DetermineContextualWeights(ArchitecturalPattern pattern, ArchitecturalContext context)
        {
            return pattern switch
            {
                ArchitecturalPattern.MicroservicesBoundary => WeightProfile.MicroservicesBoundary,
                ArchitecturalPattern.DomainCore => WeightProfile.DomainCore,
                ArchitecturalPattern.Infrastructure => WeightProfile.Infrastructure,
                ArchitecturalPattern.ApplicationService => WeightProfile.ApplicationService,
                ArchitecturalPattern.Testing => WeightProfile.Testing,
                _ => WeightProfile.Default
            };
        }

        // Enhanced helper methods
        private int GetEnhancedDependencyWeight(SourceDependencyType dependencyType)
        {
            return dependencyType switch
            {
                SourceDependencyType.Inheritance => 10,
                SourceDependencyType.Interface => 9,
                SourceDependencyType.Constructor => 8,
                SourceDependencyType.Field => 7,
                SourceDependencyType.Property => 6,
                SourceDependencyType.ReturnType => 5,
                SourceDependencyType.Method => 4,
                SourceDependencyType.GenericArgument => 4,
                SourceDependencyType.Parameter => 3,
                SourceDependencyType.Event => 3,
                SourceDependencyType.LocalVariable => 2,
                SourceDependencyType.Attribute => 1,
                SourceDependencyType.CastOperation => 1,
                _ => 1
            };
        }

        private double GetArchitecturalRoleMultiplier(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            ArchitecturalPattern sourcePattern = context.GetPattern(dependency.SourceType);
            ArchitecturalPattern targetPattern = context.GetPattern(dependency.TargetType);

            // Core domain types should have higher multipliers
            return targetPattern == ArchitecturalPattern.DomainCore
                ? 1.3
                : sourcePattern == ArchitecturalPattern.DomainCore ? 1.2 : targetPattern == ArchitecturalPattern.Infrastructure ? 0.8 : 1.0;
        }

        private double GetDesignPatternMultiplier(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            ArchitecturalPattern pattern = context.GetPattern(dependency.TargetType);

            return pattern switch
            {
                ArchitecturalPattern.Factory => 1.2,
                ArchitecturalPattern.Repository => 1.3,
                ArchitecturalPattern.Strategy => 1.1,
                ArchitecturalPattern.Singleton => 0.7, // Singleton is generally not preferred
                _ => 1.0
            };
        }

        private double GetArchitecturalLayerMultiplier(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            LayerType sourceLayer = context.GetLayer(dependency.SourceType);
            LayerType targetLayer = context.GetLayer(dependency.TargetType);

            // Dependencies should generally flow inward (toward domain)
            return this.IsValidLayerDependency(sourceLayer, targetLayer)
                ? 1.2
                : this.IsInvalidLayerDependency(sourceLayer, targetLayer) ? 0.6 : 1.0;
        }

        private double GetDependencyDirectionMultiplier(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            LayerType sourceLayer = context.GetLayer(dependency.SourceType);
            LayerType targetLayer = context.GetLayer(dependency.TargetType);

            // Penalize upward dependencies (presentation -> domain is ok, domain -> presentation is not)
            return sourceLayer == LayerType.Domain && targetLayer == LayerType.Presentation
                ? 0.5
                : sourceLayer == LayerType.Domain && targetLayer == LayerType.Application ? 0.7 : 1.0;
        }

        private bool IsValidLayerDependency(LayerType source, LayerType target)
        {
            // Valid: outer layers depending on inner layers
            return (source, target) switch
            {
                (LayerType.Presentation, LayerType.Application) => true,
                (LayerType.Application, LayerType.Domain) => true,
                (LayerType.Infrastructure, LayerType.Domain) => true,
                _ => false
            };
        }

        private bool IsInvalidLayerDependency(LayerType source, LayerType target)
        {
            // Invalid: inner layers depending on outer layers
            return (source, target) switch
            {
                (LayerType.Domain, LayerType.Application) => true,
                (LayerType.Domain, LayerType.Presentation) => true,
                (LayerType.Application, LayerType.Presentation) => true,
                _ => false
            };
        }

        // Semantic analysis helper methods
        private double AnalyzeDependencyPurpose(SourceTypeDependency dependency)
        {
            return dependency.DependencyType switch
            {
                SourceDependencyType.Interface => 0.9,
                SourceDependencyType.Constructor => 0.8,
                SourceDependencyType.Inheritance => 0.85,
                SourceDependencyType.Field => 0.6,
                SourceDependencyType.Property => 0.7,
                SourceDependencyType.Method => this.AnalyzeMethodPurpose(dependency),
                SourceDependencyType.LocalVariable => 0.2,
                _ => 0.4
            };
        }

        private double AnalyzeMethodPurpose(SourceTypeDependency dependency)
        {
            if (string.IsNullOrEmpty(dependency.MemberName))
            {
                return 0.4;
            }

            string memberName = dependency.MemberName.ToLowerInvariant();

            // Business logic methods have higher purpose
            if (memberName.Contains("execute") || memberName.Contains("process") || memberName.Contains("handle"))
            {
                return 0.8;
            }

            // CRUD operations have medium purpose
            if (memberName.Contains("get") || memberName.Contains("set") || memberName.Contains("save") || memberName.Contains("delete"))
            {
                return 0.6;
            }

            // Utility methods have lower purpose
            return memberName.Contains("helper") || memberName.Contains("util") || memberName.Contains("convert") ? 0.3 : 0.4;
        }

        private double AnalyzeCohesionContribution(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            // High cohesion when types are in the same namespace/module and serve similar purposes
            string sourceNamespace = dependency.SourceType.ContainingNamespace.ToDisplayString();
            string targetNamespace = dependency.TargetType.ContainingNamespace.ToDisplayString();

            return sourceNamespace == targetNamespace
                ? 0.8
                : sourceNamespace.StartsWith(targetNamespace) || targetNamespace.StartsWith(sourceNamespace) ? 0.6 : 0.4;
        }

        private double AnalyzeAbstractionLevel(SourceTypeDependency dependency)
        {
            INamedTypeSymbol targetType = dependency.TargetType;

            return targetType.TypeKind == TypeKind.Interface ? 0.9 : targetType.IsAbstract ? 0.8 : targetType.IsSealed ? 0.4 : 0.6;
        }

        private double AnalyzeCodeIntent(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            // Analyze if the dependency makes sense from a domain perspective
            ArchitecturalPattern sourcePattern = context.GetPattern(dependency.SourceType);
            ArchitecturalPattern targetPattern = context.GetPattern(dependency.TargetType);

            // Repository depending on domain entity is high intent
            if (sourcePattern == ArchitecturalPattern.Repository && targetPattern == ArchitecturalPattern.DomainCore)
            {
                return 0.9;
            }

            // Service depending on repository is high intent
            return sourcePattern == ArchitecturalPattern.ApplicationService && targetPattern == ArchitecturalPattern.Repository ? 0.8 : 0.5;
        }

        private bool IsBidirectional(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            // This would require analyzing the full dependency graph
            // For now, return false as a placeholder
            return false;
        }

        private double AnalyzeAbstractionStability(SourceTypeDependency dependency)
        {
            INamedTypeSymbol targetType = dependency.TargetType;

            // Interfaces and abstract classes are more stable
            return targetType.TypeKind == TypeKind.Interface ? 0.9 : targetType.IsAbstract ? 0.8 : targetType.IsSealed ? 0.6 : 0.5;
        }

        private double AnalyzeExecutionPathImportance(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            // Analyze if this dependency is in critical execution paths
            ArchitecturalPattern targetPattern = context.GetPattern(dependency.TargetType);

            return targetPattern == ArchitecturalPattern.DomainCore
                ? 0.9
                : targetPattern == ArchitecturalPattern.ApplicationService
                ? 0.7
                : targetPattern == ArchitecturalPattern.Infrastructure ? 0.5 : 0.3;
        }

        private double AnalyzeBusinessLogicRole(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            ArchitecturalPattern targetPattern = context.GetPattern(dependency.TargetType);
            ArchitecturalPattern sourcePattern = context.GetPattern(dependency.SourceType);

            // Domain core is business critical
            return targetPattern == ArchitecturalPattern.DomainCore ? 0.9 : sourcePattern == ArchitecturalPattern.DomainCore ? 0.8 : 0.4;
        }

        private double AnalyzeFailureImpact(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            // Constructor dependencies have high failure impact
            return dependency.DependencyType == SourceDependencyType.Constructor
                ? 0.8
                : dependency.DependencyType == SourceDependencyType.Interface ? 0.7 : 0.4;
        }

        private double AnalyzePerformanceImpact(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            // Method calls have performance implications
            return dependency.DependencyType == SourceDependencyType.Method
                ? 0.6
                : dependency.DependencyType == SourceDependencyType.Property ? 0.4 : 0.2;
        }

        private double AnalyzeMockability(SourceTypeDependency dependency)
        {
            INamedTypeSymbol targetType = dependency.TargetType;

            // Interfaces are easily mockable
            return targetType.TypeKind == TypeKind.Interface ? 0.9 : targetType.IsAbstract ? 0.8 : targetType.IsSealed ? 0.2 : 0.5;
        }

        private double AnalyzeTestComplexity(SourceTypeDependency dependency, ArchitecturalContext context)
        {
            int couplingCount = context.GetAfferentCoupling(dependency.TargetType) +
                               context.GetEfferentCoupling(dependency.TargetType);

            // Higher coupling = higher test complexity
            return Math.Min(1.0, couplingCount / 10.0);
        }

        private double AnalyzeIsolationDifficulty(SourceTypeDependency dependency)
        {
            // Static dependencies are harder to isolate
            return dependency.TargetType.IsStatic ? 0.8 : dependency.DependencyType == SourceDependencyType.StaticReference ? 0.9 : 0.3;
        }
    }
}