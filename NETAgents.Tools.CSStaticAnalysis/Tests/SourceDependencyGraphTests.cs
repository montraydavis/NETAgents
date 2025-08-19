using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MCPCSharpRelevancy.Models;
using Xunit;

namespace NETAgents.Tools.CSStaticAnalysis.Tests
{
    /// <summary>
    /// Comprehensive tests for SourceDependencyGraph bi-directional functionality
    /// </summary>
    public class SourceDependencyGraphTests
    {
        private readonly SourceDependencyGraph _graph;
        private readonly SourceTypeNode _userService;
        private readonly SourceTypeNode _userController;
        private readonly SourceTypeNode _userRepository;
        private readonly SourceTypeNode _userModel;

        public SourceDependencyGraphTests()
        {
            // Create a mock solution and graph
            AdhocWorkspace workspace = new AdhocWorkspace();
            Project? project = workspace.AddProject("TestProject", LanguageNames.CSharp);
            Solution solution = workspace.CurrentSolution;
            
            _graph = new SourceDependencyGraph(solution);
            
            // Create mock nodes
            _userService = CreateMockNode("UserService", project);
            _userController = CreateMockNode("UserController", project);
            _userRepository = CreateMockNode("UserRepository", project);
            _userModel = CreateMockNode("UserModel", project);
            
            // Add nodes to graph
            _graph.AddNode(_userService);
            _graph.AddNode(_userController);
            _graph.AddNode(_userRepository);
            _graph.AddNode(_userModel);
            
            // Create dependencies
            CreateMockDependencies();
        }

        private SourceTypeNode CreateMockNode(string name, Project project)
        {
            // Create a simple mock node for testing
            Document document = project.AddDocument(name + ".cs", SourceText.From($"public class {name} {{ }}"));
            SyntaxTree? syntaxTree = document.GetSyntaxTreeAsync().Result;
            SyntaxNode root = syntaxTree.GetRootAsync().Result;
            ClassDeclarationSyntax classDeclaration = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);
            
            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            INamedTypeSymbol? symbol = semanticModel.GetDeclaredSymbol(classDeclaration);
            
            return new SourceTypeNode(
                symbol!,
                classDeclaration,
                document,
                classDeclaration.ToString(),
                document.FilePath ?? string.Empty,
                classDeclaration.GetLocation().GetLineSpan().StartLinePosition.Line,
                classDeclaration.GetLocation().GetLineSpan().EndLinePosition.Line,
                project
            );
        }

        private void CreateMockDependencies()
        {
            // UserController depends on UserService
            SourceTypeDependency controllerToService = CreateMockDependency(_userController, _userService, SourceDependencyType.Constructor);
            _graph.AddDependency(controllerToService);
            
            // UserService depends on UserRepository
            SourceTypeDependency serviceToRepository = CreateMockDependency(_userService, _userRepository, SourceDependencyType.Constructor);
            _graph.AddDependency(serviceToRepository);
            
            // UserService depends on UserModel
            SourceTypeDependency serviceToModel = CreateMockDependency(_userService, _userModel, SourceDependencyType.Property);
            _graph.AddDependency(serviceToModel);
            
            // UserRepository depends on UserModel
            SourceTypeDependency repositoryToModel = CreateMockDependency(_userRepository, _userModel, SourceDependencyType.Field);
            _graph.AddDependency(repositoryToModel);
        }

        private SourceTypeDependency CreateMockDependency(SourceTypeNode source, SourceTypeNode target, SourceDependencyType type)
        {
            return new SourceTypeDependency(
                source.Symbol,
                target.Symbol,
                type,
                memberName: $"{source.Name}To{target.Name}",
                location: source.Location,
                document: source.Document
            );
        }

        [Fact]
        public void GraphConstruction_ShouldTrackDependenciesCorrectly()
        {
            // Assert
            Assert.Equal(4, _graph.Nodes.Count);
            Assert.Equal(4, _graph.DependencyEdgeCount);
            Assert.Equal(4, _graph.AllDependencies.Count());
        }

        [Fact]
        public void BiDirectionalTracking_ShouldMaintainConsistency()
        {
            // Test that dependencies and dependents are tracked correctly
            
            // UserService should have 2 dependencies (UserRepository, UserModel)
            IEnumerable<SourceTypeDependency> userServiceDependencies = _graph.GetDependenciesOf("UserService");
            Assert.Equal(2, userServiceDependencies.Count());
            
            // UserService should have 1 dependent (UserController)
            IEnumerable<SourceTypeDependency> userServiceDependents = _graph.GetDependentsOf("UserService");
            SourceTypeDependency singleDependent = Assert.Single(userServiceDependents);
            Assert.Equal("UserController", singleDependent.SourceType.Name);
        }

        [Fact]
        public void GraphLevelDependents_ShouldMatchNodeLevelDependents()
        {
            // Test that graph-level Dependents dictionary matches node-level dependents
            
            foreach (SourceTypeNode node in _graph.Nodes.Values)
            {
                List<SourceTypeDependency> nodeDependents = node.Dependents;
                List<SourceTypeDependency> graphDependents = _graph.GetDependentsFromGraph(node.FullName).ToList();

                Assert.Equal(nodeDependents.Count, graphDependents.Count);

                // Check that the same dependencies are present
                HashSet<string> nodeDependentTypes = nodeDependents.Select(d => d.SourceType.ToDisplayString()).ToHashSet();
                HashSet<string> graphDependentTypes = graphDependents.Select(d => d.SourceType.ToDisplayString()).ToHashSet();

                Assert.Equal(nodeDependentTypes, graphDependentTypes);
            }
        }

        [Fact]
        public void GetDependentsFromGraph_ShouldReturnCorrectDependents()
        {
            // Test the new graph-level dependents method
            
            IEnumerable<SourceTypeDependency> userServiceDependents = _graph.GetDependentsFromGraph("UserService");
            SourceTypeDependency singleDependent = Assert.Single(userServiceDependents);
            Assert.Equal("UserController", singleDependent.SourceType.Name);
            
            IEnumerable<SourceTypeDependency> userModelDependents = _graph.GetDependentsFromGraph("UserModel");
            Assert.Equal(2, userModelDependents.Count());
            HashSet<string> dependentTypes = userModelDependents.Select(d => d.SourceType.Name).ToHashSet();
            Assert.Contains("UserService", dependentTypes);
            Assert.Contains("UserRepository", dependentTypes);
        }

        [Fact]
        public void GetTypesWithDependents_ShouldReturnCorrectTypes()
        {
            List<string> typesWithDependents = _graph.GetTypesWithDependents().ToList();
            
            Assert.Equal(3, typesWithDependents.Count); // UserService, UserModel, UserRepository have dependents
            Assert.Contains("UserService", typesWithDependents);
            Assert.Contains("UserModel", typesWithDependents);
            Assert.Contains("UserRepository", typesWithDependents);
            Assert.DoesNotContain("UserController", typesWithDependents); // No dependents
        }

        [Fact]
        public void GetDependentCount_ShouldReturnCorrectCounts()
        {
            Assert.Equal(1, _graph.GetDependentCount("UserService"));
            Assert.Equal(2, _graph.GetDependentCount("UserModel"));
            Assert.Equal(1, _graph.GetDependentCount("UserRepository"));
            Assert.Equal(0, _graph.GetDependentCount("UserController"));
        }

        [Fact]
        public void GetAllDependents_ShouldReturnAllDependents()
        {
            List<SourceTypeDependency> allDependents = _graph.GetAllDependents().ToList();
            Assert.Equal(4, allDependents.Count); // Total of all dependent relationships
        }

        [Fact]
        public void FanInFanOutCalculations_ShouldBeCorrect()
        {
            // UserService: Fan-In = 1 (UserController depends on it), Fan-Out = 2 (depends on UserRepository and UserModel)
            Assert.Equal(1, _graph.GetFanInScore("UserService"));
            Assert.Equal(2, _graph.GetFanOutScore("UserService"));
            
            // UserModel: Fan-In = 2 (UserService and UserRepository depend on it), Fan-Out = 0
            Assert.Equal(2, _graph.GetFanInScore("UserModel"));
            Assert.Equal(0, _graph.GetFanOutScore("UserModel"));
            
            // UserController: Fan-In = 0, Fan-Out = 1 (depends on UserService)
            Assert.Equal(0, _graph.GetFanInScore("UserController"));
            Assert.Equal(1, _graph.GetFanOutScore("UserController"));
        }

        [Fact]
        public void HighFanInTypes_ShouldBeIdentifiedCorrectly()
        {
            List<SourceTypeNode> highFanInTypes = _graph.GetHighFanInTypes(1).ToList();
            Assert.Equal(3, highFanInTypes.Count); // UserService, UserModel, and UserRepository have >= 1 dependents
            Assert.Contains(_userService, highFanInTypes);
            Assert.Contains(_userModel, highFanInTypes);
            Assert.Contains(_userRepository, highFanInTypes);
        }

        [Fact]
        public void HighFanOutTypes_ShouldBeIdentifiedCorrectly()
        {
            List<SourceTypeNode> highFanOutTypes = _graph.GetHighFanOutTypes(1).ToList();
            Assert.Equal(3, highFanOutTypes.Count); // UserService, UserController, and UserRepository have >= 1 dependencies
            Assert.Contains(_userService, highFanOutTypes);
            Assert.Contains(_userController, highFanOutTypes);
            Assert.Contains(_userRepository, highFanOutTypes);
        }

        [Fact]
        public void StableTypes_ShouldBeIdentifiedCorrectly()
        {
            List<SourceTypeNode> stableTypes = _graph.GetStableTypes().ToList();
            Assert.Single(stableTypes, _userModel); // UserModel is stable (high fan-in, low fan-out)
        }

        [Fact]
        public void UnstableTypes_ShouldBeIdentifiedCorrectly()
        {
            List<SourceTypeNode> unstableTypes = _graph.GetUnstableTypes().ToList();
            Assert.Empty(unstableTypes); // With avg fan-in/out == 1, no type has fan-out > 1 and fan-in < 1
        }

        [Fact]
        public void ImpactScope_ShouldIncludeAllDependents()
        {
            List<SourceTypeNode> userModelImpact = _graph.GetImpactScope("UserModel");
            Assert.Equal(4, userModelImpact.Count); // UserModel, UserService, UserController, UserRepository
            Assert.Contains(_userModel, userModelImpact);
            Assert.Contains(_userService, userModelImpact);
            Assert.Contains(_userController, userModelImpact);
            Assert.Contains(_userRepository, userModelImpact);
        }

        [Fact]
        public void DependencyScope_ShouldIncludeAllDependencies()
        {
            List<SourceTypeNode> userControllerScope = _graph.GetDependencyScope("UserController");
            Assert.Equal(4, userControllerScope.Count); // UserController, UserService, UserRepository, UserModel
            Assert.Contains(_userController, userControllerScope);
            Assert.Contains(_userService, userControllerScope);
            Assert.Contains(_userRepository, userControllerScope);
            Assert.Contains(_userModel, userControllerScope);
        }

        [Fact]
        public void ImpactScore_ShouldBeCorrect()
        {
            Assert.Equal(4, _graph.GetImpactScore("UserModel")); // Affects 4 types
            Assert.Equal(2, _graph.GetImpactScore("UserService")); // Affects 2 types
            Assert.Equal(1, _graph.GetImpactScore("UserController")); // Affects 1 type
        }

        [Fact]
        public void DependencyScore_ShouldBeCorrect()
        {
            Assert.Equal(4, _graph.GetDependencyScore("UserController")); // Depends on 4 types (transitive)
            Assert.Equal(3, _graph.GetDependencyScore("UserService")); // Depends on 3 types (transitive)
            Assert.Equal(2, _graph.GetDependencyScore("UserRepository")); // Depends on 2 types (transitive)
        }

        [Fact]
        public void MetricsCalculation_ShouldBeAccurate()
        {
            _graph.CalculateMetrics();
            SourceGraphMetrics metrics = _graph.Metrics;
            
            Assert.Equal(4, metrics.TotalTypes);
            Assert.Equal(4, metrics.TotalDependencies);
            Assert.Equal(4, metrics.TotalDependents);
            Assert.Equal(2, metrics.MaxFanIn);
            Assert.Equal(2, metrics.MaxFanOut);
            Assert.Equal(1, metrics.StableTypes);
            Assert.Equal(0, metrics.UnstableTypes);
        }

        [Fact]
        public void CachedIndexes_ShouldImprovePerformance()
        {
            // Test that cached indexes work correctly
            _graph.BuildBiDirectionalIndexes();
            
            IEnumerable<SourceTypeDependency> dependentsOptimized = _graph.GetDependentsOfOptimized("UserService");
            IEnumerable<SourceTypeDependency> dependentsRegular = _graph.GetDependentsOf("UserService");
            
            Assert.Equal(dependentsRegular.Count(), dependentsOptimized.Count());
            
            IEnumerable<SourceTypeDependency> dependenciesOptimized = _graph.GetDependenciesOfOptimized("UserService");
            IEnumerable<SourceTypeDependency> dependenciesRegular = _graph.GetDependenciesOf("UserService");
            
            Assert.Equal(dependenciesRegular.Count(), dependenciesOptimized.Count());
        }

        [Fact]
        public void PathFinding_ShouldFindCorrectPaths()
        {
            List<SourceTypeNode> path = _graph.FindDependencyChain("UserController", "UserModel");
            Assert.Equal(3, path.Count); // UserController -> UserService -> UserModel
            Assert.Equal("UserController", path[0].Name);
            Assert.Equal("UserService", path[1].Name);
            Assert.Equal("UserModel", path[2].Name);
        }

        [Fact]
        public void DependencyDistance_ShouldBeCorrect()
        {
            Assert.Equal(2, _graph.GetDependencyDistance("UserController", "UserModel")); // 2 hops
            Assert.Equal(1, _graph.GetDependencyDistance("UserController", "UserService")); // 1 hop
            Assert.Equal(0, _graph.GetDependencyDistance("UserController", "UserController")); // Same node
        }

        [Fact]
        public void BiDirectionalReport_ShouldGenerateCorrectly()
        {
            string report = _graph.GenerateBiDirectionalReport();
            Assert.NotNull(report);
            Assert.Contains("BI-DIRECTIONAL DEPENDENCY ANALYSIS REPORT", report);
            Assert.Contains("Total Types: 4", report);
            Assert.Contains("Total Dependencies: 4", report);
        }

        [Fact]
        public void TypeMetrics_ShouldBeAccurate()
        {
            Dictionary<string, object> metrics = _graph.GetTypeBiDirectionalMetrics("UserService");
            
            Assert.Equal("UserService", metrics["TypeName"]);
            Assert.Equal(1, metrics["FanIn"]);
            Assert.Equal(2, metrics["FanOut"]);
            Assert.False((bool)metrics["IsUnstable"]);
            Assert.False((bool)metrics["IsStable"]);
            Assert.Equal(2, metrics["ImpactScore"]);
            Assert.Equal(3, metrics["DependencyScore"]);
        }

        [Fact]
        public void FanInOutAnalysis_ShouldReturnCorrectData()
        {
            List<(string TypeName, int FanIn, int FanOut, double FanInRatio, double FanOutRatio, bool IsStable, bool IsUnstable)> analysis = _graph.GetFanInOutAnalysis().ToList();
            Assert.Equal(4, analysis.Count); // One entry per type
            
            (string TypeName, int FanIn, int FanOut, double FanInRatio, double FanOutRatio, bool IsStable, bool IsUnstable) userServiceAnalysis = analysis.First(a => a.TypeName == "UserService");
            Assert.Equal(1, userServiceAnalysis.FanIn);
            Assert.Equal(2, userServiceAnalysis.FanOut);
            Assert.False(userServiceAnalysis.IsStable);
            Assert.False(userServiceAnalysis.IsUnstable);
        }

        [Fact]
        public void DictionaryConsistency_ShouldBeMaintained()
        {
            // Test that the Dependencies dictionary structure is consistent
            Assert.Equal(3, _graph.Dependencies.Count); // 3 types have dependencies
            
            // UserController has 1 dependency
            Assert.True(_graph.Dependencies.ContainsKey("UserController"));
            Assert.Single(_graph.Dependencies["UserController"]);
            
            // UserService has 2 dependencies
            Assert.True(_graph.Dependencies.ContainsKey("UserService"));
            Assert.Equal(2, _graph.Dependencies["UserService"].Count);
            
            // UserRepository has 1 dependency
            Assert.True(_graph.Dependencies.ContainsKey("UserRepository"));
            Assert.Single(_graph.Dependencies["UserRepository"]);
            
            // UserModel has no dependencies
            Assert.False(_graph.Dependencies.ContainsKey("UserModel"));
        }
    }
}
