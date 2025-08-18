namespace MCPCSharpRelevancy.Models.KnowledgeGraph
{
    using System.Collections.Generic;

    using Microsoft.CodeAnalysis;


    /// <summary>
    /// Processing state of a node during traversal
    /// </summary>
    public enum NodeProcessingState
    {
        Unprocessed,
        Processing,
        Processed
    }

    /// <summary>
    /// Base interface for all knowledge graph nodes
    /// </summary>
    public interface IKnowledgeGraphNode
    {
        /// <summary>
        /// Unique identifier for the node
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Display name of the node
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Fully qualified name of the parent node (null for root nodes)
        /// </summary>
        string? ParentQualifiedName { get; }

        /// <summary>
        /// Type of node in the hierarchy
        /// </summary>
        NodeType NodeType { get; }

        /// <summary>
        /// Additional properties and metadata
        /// </summary>
        Dictionary<string, object> Properties { get; }

        /// <summary>
        /// When this node was created/last updated
        /// </summary>
        DateTime LastUpdated { get; }
    }

    /// <summary>
    /// Base interface for all knowledge graph edges
    /// </summary>
    public interface IKnowledgeGraphEdge
    {
        /// <summary>
        /// Unique identifier for the edge
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Source node identifier
        /// </summary>
        string SourceNodeId { get; }

        /// <summary>
        /// Target node identifier
        /// </summary>
        string TargetNodeId { get; }

        /// <summary>
        /// Type of relationship
        /// </summary>
        EdgeType EdgeType { get; }

        /// <summary>
        /// Natural language summary of the relationship's purpose
        /// </summary>
        string Summary { get; set; }

        /// <summary>
        /// Confidence score for AI-generated content (0.0 to 1.0)
        /// </summary>
        double ConfidenceScore { get; set; }

        /// <summary>
        /// Additional edge properties and metadata
        /// </summary>
        Dictionary<string, object> Properties { get; }

        /// <summary>
        /// When this edge was created/last updated
        /// </summary>
        DateTime LastUpdated { get; }
    }

    /// <summary>
    /// Abstract base class for knowledge graph nodes
    /// </summary>
    public abstract class KnowledgeGraphNodeBase(string id, string name, string? parentQualifiedName, NodeType nodeType) : IKnowledgeGraphNode
    {

        /// <inheritdoc />
        public string Id { get; } = id;

        /// <inheritdoc />
        public string Name { get; } = name;

        /// <inheritdoc />
        public string? ParentQualifiedName { get; } = parentQualifiedName;

        /// <inheritdoc />
        public NodeType NodeType { get; } = nodeType;

        /// <inheritdoc />
        public Dictionary<string, object> Properties { get; } = [];

        /// <inheritdoc />
        public DateTime LastUpdated { get; protected set; } = DateTime.UtcNow;

        /// <summary>
        /// Updates the last updated timestamp
        /// </summary>
        public void Touch()
        {
            this.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Abstract base class for knowledge graph edges
    /// </summary>
    public abstract class KnowledgeGraphEdgeBase(
        string sourceNodeId,
        string targetNodeId,
        EdgeType edgeType,
        string summary = "") : IKnowledgeGraphEdge
    {

        /// <inheritdoc />
        public string Id { get; } = GenerateEdgeId(sourceNodeId, targetNodeId, edgeType);

        /// <inheritdoc />
        public string SourceNodeId { get; } = sourceNodeId;

        /// <inheritdoc />
        public string TargetNodeId { get; } = targetNodeId;

        /// <inheritdoc />
        public EdgeType EdgeType { get; } = edgeType;

        /// <inheritdoc />
        public string Summary { get; set; } = summary;

        /// <inheritdoc />
        public double ConfidenceScore { get; set; } = 1.0; // Default to high confidence for non-AI generated content

        /// <inheritdoc />
        public Dictionary<string, object> Properties { get; } = [];

        /// <inheritdoc />
        public DateTime LastUpdated { get; protected set; } = DateTime.UtcNow;

        /// <summary>
        /// Updates the summary and marks as updated
        /// </summary>
        public void UpdateSummary(string summary, double confidenceScore = 1.0)
        {
            this.Summary = summary;
            this.ConfidenceScore = confidenceScore;
            this.LastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// Generates a deterministic edge ID
        /// </summary>
        private static string GenerateEdgeId(string sourceId, string targetId, EdgeType edgeType)
        {
            return $"{sourceId}-{edgeType}-{targetId}";
        }
    }

    /// <summary>
    /// Project node representing a C# project in the knowledge graph
    /// </summary>
    public class ProjectNode : KnowledgeGraphNodeBase
    {
        public ProjectNode(string id, string name, string filePath, string? targetFramework = null)
            : base(id, name, null, NodeType.Project)
        {
            this.FilePath = filePath;
            this.TargetFramework = targetFramework;

            this.Properties["FilePath"] = filePath;
            if (targetFramework != null)
            {
                this.Properties["TargetFramework"] = targetFramework;
            }
        }

        /// <summary>
        /// Path to the project file
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Target framework (e.g., net9.0)
        /// </summary>
        public string? TargetFramework { get; }

        /// <summary>
        /// Creates a project node from a Roslyn Project
        /// </summary>
        public static ProjectNode FromRoslynProject(Project project)
        {
            string id = $"project:{project.Name}";
            string? targetFramework = project.ParseOptions?.DocumentationMode.ToString();

            return new ProjectNode(id, project.Name, project.FilePath ?? project.Name, targetFramework);
        }
    }

    /// <summary>
    /// Namespace node representing a namespace in the knowledge graph
    /// </summary>
    public class NamespaceNode : KnowledgeGraphNodeBase
    {
        public NamespaceNode(string id, string name, string shortName, string parentQualifiedName)
            : base(id, name, parentQualifiedName, NodeType.Namespace)
        {
            this.ShortName = shortName;
            this.Properties["ShortName"] = shortName;
        }

        /// <summary>
        /// Short name (last segment of namespace)
        /// </summary>
        public string ShortName { get; }

        /// <summary>
        /// Creates a namespace node
        /// </summary>
        public static NamespaceNode Create(string namespaceName, string projectName)
        {
            string id = $"namespace:{namespaceName}";
            string shortName = namespaceName.Split('.').LastOrDefault() ?? namespaceName;

            return new NamespaceNode(id, namespaceName, shortName, projectName);
        }
    }

    /// <summary>
    /// Type node representing a C# type in the knowledge graph
    /// </summary>
    public class TypeNode : KnowledgeGraphNodeBase
    {
        public TypeNode(
            string id,
            string name,
            string fullyQualifiedName,
            string parentQualifiedName,
            TypeKind kind,
            Accessibility accessModifier,
            SourceLocation? sourceLocation = null)
            : base(id, name, parentQualifiedName, NodeType.Type)
        {
            this.FullyQualifiedName = fullyQualifiedName;
            this.Kind = kind;
            this.AccessModifier = accessModifier;
            this.SourceLocation = sourceLocation;

            this.Properties["FullyQualifiedName"] = fullyQualifiedName;
            this.Properties["Kind"] = kind.ToString();
            this.Properties["AccessModifier"] = accessModifier.ToString();

            if (sourceLocation != null)
            {
                this.Properties["SourceLocation"] = sourceLocation;
            }
        }

        /// <summary>
        /// Fully qualified type name
        /// </summary>
        public string FullyQualifiedName { get; }

        /// <summary>
        /// Type kind (Class, Interface, Struct, etc.)
        /// </summary>
        public TypeKind Kind { get; }

        /// <summary>
        /// Access modifier
        /// </summary>
        public Accessibility AccessModifier { get; }

        /// <summary>
        /// Source code location
        /// </summary>
        public SourceLocation? SourceLocation { get; }

        /// <summary>
        /// Whether this type is abstract
        /// </summary>
        public bool IsAbstract
        {
            get => this.Properties.TryGetValue("IsAbstract", out object? value) && (bool)value;
            set => this.Properties["IsAbstract"] = value;
        }

        /// <summary>
        /// Whether this type is sealed
        /// </summary>
        public bool IsSealed
        {
            get => this.Properties.TryGetValue("IsSealed", out object? value) && (bool)value;
            set => this.Properties["IsSealed"] = value;
        }

        /// <summary>
        /// Whether this type is static
        /// </summary>
        public bool IsStatic
        {
            get => this.Properties.TryGetValue("IsStatic", out object? value) && (bool)value;
            set => this.Properties["IsStatic"] = value;
        }

        /// <summary>
        /// Creates a type node from a Roslyn symbol and source type node
        /// </summary>
        public static TypeNode FromSourceTypeNode(SourceTypeNode sourceNode)
        {
            string id = $"type:{sourceNode.FullName}";
            SourceLocation sourceLocation = new SourceLocation(
                sourceNode.FilePath,
                sourceNode.StartLine,
                sourceNode.EndLine);

            TypeNode typeNode = new TypeNode(
                id,
                sourceNode.Name,
                sourceNode.FullName,
                sourceNode.Namespace,
                sourceNode.Symbol.TypeKind,
                sourceNode.Symbol.DeclaredAccessibility,
                sourceLocation)
            {
                IsAbstract = sourceNode.IsAbstract,
                IsSealed = sourceNode.IsSealed,
                IsStatic = sourceNode.IsStatic
            };

            // Copy additional properties
            if (!string.IsNullOrEmpty(sourceNode.Documentation))
            {
                typeNode.Properties["Documentation"] = sourceNode.Documentation;
            }

            if (!string.IsNullOrEmpty(sourceNode.Summary))
            {
                typeNode.Properties["AISummary"] = sourceNode.Summary;
            }

            return typeNode;
        }
    }

    /// <summary>
    /// Member node representing a type member in the knowledge graph
    /// </summary>
    public class MemberNode : KnowledgeGraphNodeBase
    {
        public MemberNode(
            string id,
            string name,
            string fullyQualifiedName,
            string parentQualifiedName,
            MemberType memberType,
            Accessibility accessModifier,
            string? signature = null,
            SourceLocation? sourceLocation = null)
            : base(id, name, parentQualifiedName, NodeType.Member)
        {
            this.FullyQualifiedName = fullyQualifiedName;
            this.MemberType = memberType;
            this.AccessModifier = accessModifier;
            this.Signature = signature;
            this.SourceLocation = sourceLocation;

            this.Properties["FullyQualifiedName"] = fullyQualifiedName;
            this.Properties["MemberType"] = memberType.ToString();
            this.Properties["AccessModifier"] = accessModifier.ToString();

            if (signature != null)
            {
                this.Properties["Signature"] = signature;
            }

            if (sourceLocation != null)
            {
                this.Properties["SourceLocation"] = sourceLocation;
            }
        }

        /// <summary>
        /// Fully qualified member name (Type.Member)
        /// </summary>
        public string FullyQualifiedName { get; }

        /// <summary>
        /// Type of member
        /// </summary>
        public MemberType MemberType { get; }

        /// <summary>
        /// Access modifier
        /// </summary>
        public Accessibility AccessModifier { get; }

        /// <summary>
        /// Member signature (method signature, property type, etc.)
        /// </summary>
        public string? Signature { get; }

        /// <summary>
        /// Source code location
        /// </summary>
        public SourceLocation? SourceLocation { get; }

        /// <summary>
        /// Whether this member is static
        /// </summary>
        public bool IsStatic
        {
            get => this.Properties.TryGetValue("IsStatic", out object? value) && (bool)value;
            set => this.Properties["IsStatic"] = value;
        }

        /// <summary>
        /// Creates a member node from a Roslyn symbol
        /// </summary>
        public static MemberNode FromSymbol(ISymbol symbol, string parentTypeQualifiedName)
        {
            string memberName = symbol.Name;
            string fullyQualifiedName = $"{parentTypeQualifiedName}.{memberName}";
            string id = $"member:{fullyQualifiedName}";

            MemberType memberType = DetermineMemberType(symbol);
            string? signature = GenerateSignature(symbol);
            SourceLocation? sourceLocation = GetSourceLocation(symbol);

            MemberNode memberNode = new MemberNode(
                id,
                memberName,
                fullyQualifiedName,
                parentTypeQualifiedName,
                memberType,
                symbol.DeclaredAccessibility,
                signature,
                sourceLocation)
            {
                IsStatic = symbol.IsStatic
            };

            return memberNode;
        }

        private static MemberType DetermineMemberType(ISymbol symbol)
        {
            return symbol.Kind switch
            {
                SymbolKind.Method => symbol.Name == ".ctor" ? MemberType.Constructor : MemberType.Method,
                SymbolKind.Property => MemberType.Property,
                SymbolKind.Field => MemberType.Field,
                SymbolKind.Event => MemberType.Event,
                _ => MemberType.Other
            };
        }

        private static string? GenerateSignature(ISymbol symbol)
        {
            return symbol switch
            {
                IMethodSymbol method => GenerateMethodSignature(method),
                IPropertySymbol property => property.Type.ToDisplayString(),
                IFieldSymbol field => field.Type.ToDisplayString(),
                IEventSymbol @event => @event.Type.ToDisplayString(),
                _ => null
            };
        }

        private static string GenerateMethodSignature(IMethodSymbol method)
        {
            string parameters = string.Join(", ", method.Parameters.Select(p =>
                $"{p.Type.ToDisplayString()} {p.Name}"));
            return $"{method.ReturnType.ToDisplayString()} {method.Name}({parameters})";
        }

        private static SourceLocation? GetSourceLocation(ISymbol symbol)
        {
            Location? location = symbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource)
            {
                return null;
            }

            FileLinePositionSpan span = location.GetLineSpan();
            return new SourceLocation(
                location.SourceTree?.FilePath ?? "Unknown",
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1);
        }
    }

    /// <summary>
    /// Containment edge representing parent-child relationships
    /// </summary>
    public class ContainmentEdge(string sourceNodeId, string targetNodeId, string summary = "") : KnowledgeGraphEdgeBase(sourceNodeId, targetNodeId, EdgeType.Contains, summary)
    {

        /// <summary>
        /// Creates a containment edge with auto-generated summary
        /// </summary>
        public static ContainmentEdge Create(IKnowledgeGraphNode parent, IKnowledgeGraphNode child)
        {
            string summary = GenerateContainmentSummary(parent.NodeType, child.NodeType, child.Name);
            return new ContainmentEdge(parent.Id, child.Id, summary);
        }

        private static string GenerateContainmentSummary(NodeType parentType, NodeType childType, string childName)
        {
            return $"Contains {childType.ToString().ToLower()} '{childName}'";
        }
    }

    /// <summary>
    /// Sibling edge representing relationships between nodes at the same hierarchy level
    /// </summary>
    public class SiblingEdge : KnowledgeGraphEdgeBase
    {
        public SiblingEdge(
            string sourceNodeId,
            string targetNodeId,
            SiblingType siblingType,
            string summary = "")
            : base(sourceNodeId, targetNodeId, EdgeType.Sibling, summary)
        {
            this.SiblingType = siblingType;
            this.Properties["SiblingType"] = siblingType.ToString();
        }

        /// <summary>
        /// Type of sibling relationship
        /// </summary>
        public SiblingType SiblingType { get; }

        /// <summary>
        /// Creates a sibling edge
        /// </summary>
        public static SiblingEdge Create(
            IKnowledgeGraphNode node1,
            IKnowledgeGraphNode node2,
            SiblingType siblingType,
            string? customSummary = null)
        {
            string summary = customSummary ?? GenerateSiblingSummary(siblingType, node1.Name, node2.Name);
            return new SiblingEdge(node1.Id, node2.Id, siblingType, summary);
        }

        private static string GenerateSiblingSummary(SiblingType siblingType, string name1, string name2)
        {
            return siblingType switch
            {
                SiblingType.NamespaceSibling => $"Related type in same namespace as '{name2}'",
                SiblingType.NestedType => $"Nested within same parent type as '{name2}'",
                SiblingType.PartialClass => $"Partial class component with '{name2}'",
                _ => $"Related to '{name2}'"
            };
        }
    }

    /// <summary>
    /// Enhanced dependency edge with AI-generated summaries
    /// </summary>
    public class DependencyEdge : KnowledgeGraphEdgeBase
    {
        public DependencyEdge(
            string sourceNodeId,
            string targetNodeId,
            SourceDependencyType dependencyType,
            double strength,
            string? memberName = null,
            string summary = "")
            : base(sourceNodeId, targetNodeId, EdgeType.DependsOn, summary)
        {
            this.DependencyType = dependencyType;
            this.Strength = strength;
            this.MemberName = memberName;

            this.Properties["DependencyType"] = dependencyType.ToString();
            this.Properties["Strength"] = strength;

            if (memberName != null)
            {
                this.Properties["MemberName"] = memberName;
            }
        }

        /// <summary>
        /// Type of dependency from existing analysis
        /// </summary>
        public SourceDependencyType DependencyType { get; }

        /// <summary>
        /// Dependency strength from existing analysis
        /// </summary>
        public double Strength { get; }

        /// <summary>
        /// Member name if dependency is member-specific
        /// </summary>
        public string? MemberName { get; }

        /// <summary>
        /// Creates a dependency edge from existing source dependency
        /// </summary>
        public static DependencyEdge FromSourceDependency(
            SourceTypeDependency sourceDependency,
            string sourceNodeId,
            string targetNodeId)
        {
            string summary = GenerateDependencySummary(sourceDependency.DependencyType, sourceDependency.MemberName);

            return new DependencyEdge(
                sourceNodeId,
                targetNodeId,
                sourceDependency.DependencyType,
                sourceDependency.EffectiveStrength,
                sourceDependency.MemberName,
                summary);
        }

        private static string GenerateDependencySummary(SourceDependencyType dependencyType, string? memberName)
        {
            string baseSummary = dependencyType switch
            {
                SourceDependencyType.Inheritance => "Inherits from",
                SourceDependencyType.Interface => "Implements interface",
                SourceDependencyType.Constructor => "Uses in constructor",
                SourceDependencyType.Field => "Has field of type",
                SourceDependencyType.Property => "Has property of type",
                SourceDependencyType.Method => "Calls methods of",
                SourceDependencyType.Parameter => "Takes parameter of type",
                SourceDependencyType.ReturnType => "Returns type",
                _ => "Depends on"
            };

            return memberName != null ? $"{baseSummary} (via {memberName})" : baseSummary;
        }
    }

    /// <summary>
    /// Source location information
    /// </summary>
    public class SourceLocation(string filePath, int startLine, int endLine)
    {

        /// <summary>
        /// Path to source file
        /// </summary>
        public string FilePath { get; } = filePath;

        /// <summary>
        /// Starting line number
        /// </summary>
        public int StartLine { get; } = startLine;

        /// <summary>
        /// Ending line number
        /// </summary>
        public int EndLine { get; } = endLine;

        public override string ToString()
        {
            return $"{Path.GetFileName(this.FilePath)}:{this.StartLine}-{this.EndLine}";
        }
    }

    /// <summary>
    /// Types of nodes in the knowledge graph
    /// </summary>
    public enum NodeType
    {
        Project,
        Namespace,
        Type,
        Member
    }

    /// <summary>
    /// Types of edges in the knowledge graph
    /// </summary>
    public enum EdgeType
    {
        Contains,
        Sibling,
        DependsOn
    }

    /// <summary>
    /// Types of sibling relationships
    /// </summary>
    public enum SiblingType
    {
        NamespaceSibling,
        NestedType,
        PartialClass
    }

    /// <summary>
    /// Types of members
    /// </summary>
    public enum MemberType
    {
        Method,
        Property,
        Field,
        Event,
        Constructor,
        Other
    }

    /// <summary>
    /// Container for the hierarchical knowledge graph
    /// </summary>
    public class HierarchicalKnowledgeGraph
    {
        private readonly Dictionary<string, IKnowledgeGraphNode> _nodes = [];
        private readonly Dictionary<string, IKnowledgeGraphEdge> _edges = [];
        private readonly Dictionary<string, List<string>> _nodesByType = [];
        private readonly Dictionary<string, List<string>> _outgoingEdges = [];
        private readonly Dictionary<string, List<string>> _incomingEdges = [];

        /// <summary>
        /// Gets all nodes in the graph
        /// </summary>
        public IReadOnlyDictionary<string, IKnowledgeGraphNode> Nodes => this._nodes;

        /// <summary>
        /// Gets all edges in the graph
        /// </summary>
        public IReadOnlyDictionary<string, IKnowledgeGraphEdge> Edges => this._edges;

        /// <summary>
        /// Gets the original source dependency graph this was built from
        /// </summary>
        public SourceDependencyGraph? SourceGraph { get; private set; }

        /// <summary>
        /// Adds a node to the graph
        /// </summary>
        public void AddNode(IKnowledgeGraphNode node)
        {
            this._nodes[node.Id] = node;

            string nodeTypeKey = node.NodeType.ToString();
            if (!this._nodesByType.TryGetValue(nodeTypeKey, out List<string>? value))
            {
                value = [];
                this._nodesByType[nodeTypeKey] = value;
            }

            value.Add(node.Id);
        }

        /// <summary>
        /// Adds an edge to the graph
        /// </summary>
        public void AddEdge(IKnowledgeGraphEdge edge)
        {
            this._edges[edge.Id] = edge;

            // Update outgoing edges index
            if (!this._outgoingEdges.TryGetValue(edge.SourceNodeId, out List<string>? value))
            {
                value = [];
                this._outgoingEdges[edge.SourceNodeId] = value;
            }

            value.Add(edge.Id);

            // Update incoming edges index
            if (!this._incomingEdges.TryGetValue(edge.TargetNodeId, out List<string>? liValue))
            {
                this._incomingEdges[edge.TargetNodeId] = liValue ?? [];
            }

            value.Add(edge.Id);
        }

        /// <summary>
        /// Gets nodes by type
        /// </summary>
        public IEnumerable<T> GetNodesByType<T>() where T : class, IKnowledgeGraphNode
        {
            string nodeType = typeof(T).Name.Replace("Node", "");
            return this._nodesByType.TryGetValue(nodeType, out List<string>? nodeIds)
                ? nodeIds.Select(id => this._nodes[id]).OfType<T>()
                : [];
        }

        /// <summary>
        /// Gets outgoing edges for a node
        /// </summary>
        public IEnumerable<IKnowledgeGraphEdge> GetOutgoingEdges(string nodeId)
        {
            return this._outgoingEdges.TryGetValue(nodeId, out List<string>? edgeIds)
                ? edgeIds.Select(id => this._edges[id])
                : [];
        }

        /// <summary>
        /// Gets incoming edges for a node
        /// </summary>
        public IEnumerable<IKnowledgeGraphEdge> GetIncomingEdges(string nodeId)
        {
            return this._incomingEdges.TryGetValue(nodeId, out List<string>? edgeIds)
                ? edgeIds.Select(id => this._edges[id])
                : [];
        }

        /// <summary>
        /// Gets children of a node (via containment edges)
        /// </summary>
        public IEnumerable<IKnowledgeGraphNode> GetChildren(string nodeId)
        {
            return this.GetOutgoingEdges(nodeId)
                .Where(e => e.EdgeType == EdgeType.Contains)
                .Select(e => this._nodes[e.TargetNodeId]);
        }

        /// <summary>
        /// Gets parent of a node (via containment edges)
        /// </summary>
        public IKnowledgeGraphNode? GetParent(string nodeId)
        {
            IKnowledgeGraphEdge? parentEdge = this.GetIncomingEdges(nodeId)
                .FirstOrDefault(e => e.EdgeType == EdgeType.Contains);

            return parentEdge != null ? this._nodes[parentEdge.SourceNodeId] : null;
        }

        /// <summary>
        /// Gets siblings of a node
        /// </summary>
        public IEnumerable<IKnowledgeGraphNode> GetSiblings(string nodeId)
        {
            IEnumerable<IKnowledgeGraphEdge> siblingEdges = this.GetOutgoingEdges(nodeId)
                .Concat(this.GetIncomingEdges(nodeId))
                .Where(e => e.EdgeType == EdgeType.Sibling);

            foreach (IKnowledgeGraphEdge? edge in siblingEdges)
            {
                string siblingId = edge.SourceNodeId == nodeId ? edge.TargetNodeId : edge.SourceNodeId;
                yield return this._nodes[siblingId];
            }
        }

        /// <summary>
        /// Sets the source dependency graph this was built from
        /// </summary>
        public void SetSourceGraph(SourceDependencyGraph sourceGraph)
        {
            this.SourceGraph = sourceGraph;
        }

        /// <summary>
        /// Gets basic statistics about the graph
        /// </summary>
        public HierarchicalGraphMetrics GetMetrics()
        {
            return new HierarchicalGraphMetrics
            {
                TotalNodes = this._nodes.Count,
                TotalEdges = this._edges.Count,
                ProjectCount = this.GetNodesByType<ProjectNode>().Count(),
                NamespaceCount = this.GetNodesByType<NamespaceNode>().Count(),
                TypeCount = this.GetNodesByType<TypeNode>().Count(),
                MemberCount = this.GetNodesByType<MemberNode>().Count(),
                ContainmentEdgeCount = this._edges.Values.Count(e => e.EdgeType == EdgeType.Contains),
                SiblingEdgeCount = this._edges.Values.Count(e => e.EdgeType == EdgeType.Sibling),
                DependencyEdgeCount = this._edges.Values.Count(e => e.EdgeType == EdgeType.DependsOn)
            };
        }
    }

    /// <summary>
    /// Metrics about the hierarchical knowledge graph
    /// </summary>
    public class HierarchicalGraphMetrics
    {
        public int TotalNodes { get; set; }
        public int TotalEdges { get; set; }
        public int ProjectCount { get; set; }
        public int NamespaceCount { get; set; }
        public int TypeCount { get; set; }
        public int MemberCount { get; set; }
        public int ContainmentEdgeCount { get; set; }
        public int SiblingEdgeCount { get; set; }
        public int DependencyEdgeCount { get; set; }

        public override string ToString()
        {
            return $"""
                Total Nodes: {this.TotalNodes}
                Total Edges: {this.TotalEdges}
                Projects: {this.ProjectCount}
                Namespaces: {this.NamespaceCount}
                Types: {this.TypeCount}
                Members: {this.MemberCount}
                Containment Edges: {this.ContainmentEdgeCount}
                Sibling Edges: {this.SiblingEdgeCount}
                Dependency Edges: {this.DependencyEdgeCount}
                """;
        }
    }
}