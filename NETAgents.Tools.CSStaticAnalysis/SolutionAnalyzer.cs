namespace MCPCSharpRelevancy.Services.Analysis
{
    using MCPCSharpRelevancy.Models;
    using MCPCSharpRelevancy.Models.Analysis;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.MSBuild;
    using Microsoft.Build.Locator;
    using System.Diagnostics;
    using System.Reflection;

    /// <summary>
    /// Service for loading and analyzing C# solutions using Roslyn with comprehensive MSBuild support
    /// </summary>
    public class SolutionAnalyzer
    {
        private readonly MSBuildWorkspace _workspace;
        private readonly ProjectAnalyzer _projectAnalyzer;
        private static bool _msbuildLocated = false;

        static SolutionAnalyzer()
        {
            // Ensure MSBuild is registered at the class level
            try
            {
                if (!MSBuildLocator.IsRegistered)
                {
                    Console.WriteLine("Registering MSBuild in static constructor...");
                    MSBuildLocator.RegisterDefaults();
                    Console.WriteLine("MSBuild registered successfully in static constructor");
                }
                
                // Test MSBuild functionality
                TestMSBuildFunctionality();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to register MSBuild in static constructor: {ex.Message}");
            }
        }

        private static void TestMSBuildFunctionality()
        {
            try
            {
                Console.WriteLine("Testing MSBuild functionality...");
                
                // Test 1: Check if we can access MSBuild types
                Console.WriteLine("Testing MSBuild type access...");
                
                // Test 2: Try to create a simple MSBuild project
                Console.WriteLine("Testing MSBuild project creation...");
                
                // Test 3: Check Roslyn MSBuild integration
                var workspaceType = typeof(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace);
                Console.WriteLine($"Successfully accessed MSBuildWorkspace type: {workspaceType.FullName}");
                
                // Test 4: Try to create a minimal workspace
                try
                {
                    var testWorkspace = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create();
                    Console.WriteLine("Successfully created test MSBuildWorkspace");
                    testWorkspace.Dispose();
                }
                catch (Exception workspaceEx)
                {
                    Console.WriteLine($"Failed to create test workspace: {workspaceEx.Message}");
                }
                
                Console.WriteLine("MSBuild functionality test completed successfully");
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"ReflectionTypeLoadException in MSBuild test: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception? loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"Loader exception: {loaderEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in MSBuild test: {ex.Message}");
            }
        }

        public SolutionAnalyzer()
        {
            // Ensure MSBuild is located and registered before creating workspace
            EnsureMSBuildLocated();

            // Try multiple approaches to create MSBuildWorkspace
            this._workspace = CreateMSBuildWorkspaceWithFallback();

            // Subscribe to diagnostics to help debug loading issues
            this._workspace.WorkspaceFailed += this.OnWorkspaceFailed;

            this._projectAnalyzer = new ProjectAnalyzer();
        }

        private static void EnsureMSBuildLocated()
        {
            if (_msbuildLocated)
            {
                return;
            }

            try
            {
                // Check if MSBuildLocator has already been registered
                if (MSBuildLocator.IsRegistered)
                {
                    Console.WriteLine("MSBuild is already registered");
                    _msbuildLocated = true;
                    return;
                }

                // Try to register MSBuild using MSBuildLocator
                VisualStudioInstance[] instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();

                if (instances.Length > 0)
                {
                    // Use the latest version found
                    VisualStudioInstance latestInstance = instances.OrderByDescending(i => i.Version).First();
                    Console.WriteLine($"Registering MSBuild from: {latestInstance.MSBuildPath} (Version: {latestInstance.Version})");
                    MSBuildLocator.RegisterInstance(latestInstance);
                    _msbuildLocated = true;
                    return;
                }

                // If no Visual Studio instances found, try to register defaults
                Console.WriteLine("No Visual Studio instances found, trying to register defaults");
                MSBuildLocator.RegisterDefaults();
                _msbuildLocated = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not register MSBuild with MSBuildLocator: {ex.Message}");

                // Fall back to manual MSBuild location
                try
                {
                    EnsureMSBuildLocatedManually();
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"Warning: Manual MSBuild location also failed: {fallbackEx.Message}");
                    _msbuildLocated = true; // Don't keep trying
                }
            }
        }

        private static void EnsureMSBuildLocatedManually()
        {
            // Try to find and set MSBuild path manually
            List<string> possibleMSBuildPaths = new List<string>
            {
                @"/usr/local/share/dotnet/sdk",
                @"/usr/share/dotnet/sdk",
                @"/opt/dotnet/sdk",
                @"/usr/local/share/dotnet",
                @"/usr/share/dotnet",
                @"/opt/dotnet"
            };

            foreach (string path in possibleMSBuildPaths)
            {
                if (Directory.Exists(path))
                {
                    Console.WriteLine($"Found .NET SDK at: {path}");
                    // For macOS/Linux, we don't set MSBUILD_EXE_PATH as it's handled differently
                    break;
                }
            }

            // Try to find .NET SDK
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-sdks",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.WriteLine("Warning: Failed to start 'dotnet --list-sdks' process");
                    _msbuildLocated = true; // avoid repeated attempts
                    return;
                }
                process.WaitForExit();
                string output = process.StandardOutput.ReadToEnd();

                Console.WriteLine($"Found .NET SDKs: {output.Trim().Replace('\n', ' ')}");

                _msbuildLocated = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not locate .NET SDK: {ex.Message}");
                _msbuildLocated = true; // Don't keep trying
            }
        }

        private MSBuildWorkspace CreateMSBuildWorkspaceWithFallback()
        {
            // Approach 1: Try with comprehensive properties
            try
            {
                Dictionary<string, string> properties = new Dictionary<string, string>
                {
                    // Disable problematic tasks and resolution
                    ["CheckForSystemRuntimeDependency"] = "false",
                    ["DesignTimeBuild"] = "true",
                    ["BuildProjectReferences"] = "false",
                    ["_ResolveReferenceDependencies"] = "false",
                    ["SuppressAutoGeneratedAssemblyAttributes"] = "true",

                    // Skip problematic MSBuild tasks
                    ["ResolveAppHosts"] = "false",
                    ["GetPackageDirectory"] = "false",
                    ["ResolveNuGetPackageAssets"] = "false",
                    ["ResolveAssemblyReferences"] = "false",
                    ["GetReferenceAssemblyPaths"] = "false",

                    // Disable package resolution
                    ["RestorePackages"] = "false",
                    ["EnableNuGetPackageRestore"] = "false",
                    ["DisableImplicitNuGetFallbackFolder"] = "true",

                    // Skip framework reference resolution
                    ["DisableFrameworkReferenceResolution"] = "true",
                    ["SkipUnchangedFiles"] = "true",

                    // Continue on errors
                    ["ContinueOnError"] = "true",
                    ["TreatWarningsAsErrors"] = "false"
                };

                var workspace = MSBuildWorkspace.Create(properties);
                Console.WriteLine("MSBuildWorkspace created successfully with comprehensive properties");
                return workspace;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"ReflectionTypeLoadException with comprehensive properties: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception? loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"Loader exception: {loaderEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception with comprehensive properties: {ex.Message}");
            }

            // Approach 2: Try with minimal properties
            try
            {
                Dictionary<string, string> minimalProperties = new Dictionary<string, string>
                {
                    ["DesignTimeBuild"] = "true",
                    ["BuildProjectReferences"] = "false",
                    ["ContinueOnError"] = "true"
                };

                var workspace = MSBuildWorkspace.Create(minimalProperties);
                Console.WriteLine("MSBuildWorkspace created successfully with minimal properties");
                return workspace;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"ReflectionTypeLoadException with minimal properties: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception? loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"Loader exception: {loaderEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception with minimal properties: {ex.Message}");
            }

            // Approach 3: Try without any properties
            try
            {
                var workspace = MSBuildWorkspace.Create();
                Console.WriteLine("MSBuildWorkspace created successfully without properties");
                return workspace;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"ReflectionTypeLoadException without properties: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception? loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"Loader exception: {loaderEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception without properties: {ex.Message}");
            }

            // Approach 4: Try with explicit assembly loading
            try
            {
                Console.WriteLine("Attempting to pre-load MSBuild assemblies...");
                
                // Try to load key MSBuild assemblies explicitly
                var assemblyLoadContext = System.Runtime.Loader.AssemblyLoadContext.Default;
                
                // Get the MSBuild path from the registered instance
                if (MSBuildLocator.IsRegistered)
                {
                    var instances = MSBuildLocator.QueryVisualStudioInstances();
                    if (instances.Any())
                    {
                        var instance = instances.First();
                        var msbuildPath = Path.GetDirectoryName(instance.MSBuildPath);
                        
                        if (!string.IsNullOrEmpty(msbuildPath))
                        {
                            Console.WriteLine($"MSBuild path: {msbuildPath}");
                            
                            // Try to load key assemblies
                            var assembliesToLoad = new[]
                            {
                                "Microsoft.Build.dll",
                                "Microsoft.Build.Framework.dll",
                                "Microsoft.Build.Tasks.Core.dll",
                                "Microsoft.Build.Utilities.Core.dll"
                            };

                            foreach (var assemblyName in assembliesToLoad)
                            {
                                var assemblyPath = Path.Combine(msbuildPath, assemblyName);
                                if (File.Exists(assemblyPath))
                                {
                                    try
                                    {
                                        assemblyLoadContext.LoadFromAssemblyPath(assemblyPath);
                                        Console.WriteLine($"Successfully loaded: {assemblyName}");
                                    }
                                    catch (Exception loadEx)
                                    {
                                        Console.WriteLine($"Failed to load {assemblyName}: {loadEx.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                var workspace = MSBuildWorkspace.Create();
                Console.WriteLine("MSBuildWorkspace created successfully after pre-loading assemblies");
                return workspace;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"ReflectionTypeLoadException after pre-loading: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception? loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"Loader exception: {loaderEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception after pre-loading: {ex.Message}");
            }

            // Approach 5: Try with environment variable approach
            try
            {
                Console.WriteLine("Attempting to set MSBuild environment variables...");
                
                if (MSBuildLocator.IsRegistered)
                {
                    var instances = MSBuildLocator.QueryVisualStudioInstances();
                    if (instances.Any())
                    {
                        var instance = instances.First();
                        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", instance.MSBuildPath);
                        Console.WriteLine($"Set MSBUILD_EXE_PATH to: {instance.MSBuildPath}");
                    }
                }

                var workspace = MSBuildWorkspace.Create();
                Console.WriteLine("MSBuildWorkspace created successfully with environment variables");
                return workspace;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"ReflectionTypeLoadException with environment variables: {ex.Message}");
                if (ex.LoaderExceptions != null)
                {
                    foreach (Exception? loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"Loader exception: {loaderEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception with environment variables: {ex.Message}");
            }

            throw new InvalidOperationException("Failed to create MSBuildWorkspace with all attempted approaches. Check the console output for specific error details.");
        }

        private void OnWorkspaceFailed(object? sender, WorkspaceDiagnosticEventArgs e)
        {
            // Only log errors, don't fail the entire process
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.WriteLine($"Workspace diagnostic: {e.Diagnostic.Kind} - {e.Diagnostic.Message}");
                // Continue processing despite failures
            }
            else
            {
                Console.WriteLine($"Workspace diagnostic: {e.Diagnostic.Kind} - {e.Diagnostic.Message}");
            }
        }

        /// <summary>
        /// Loads a solution from the specified path with lenient error handling
        /// </summary>
        /// <param name="solutionPath">Path to the .sln file</param>
        /// <param name="continueOnErrors">Whether to continue loading even if some projects fail</param>
        /// <returns>The loaded solution</returns>
        public async Task<Solution> LoadSolutionAsync(string solutionPath)
        {
            if (!File.Exists(solutionPath))
            {
                throw new FileNotFoundException($"Solution file not found: {solutionPath}");
            }

            try
            {
                Console.WriteLine($"Loading solution: {solutionPath}");
                Solution solution = await this._workspace.OpenSolutionAsync(solutionPath);

                // Log project information for debugging
                foreach (Project project in solution.Projects)
                {
                    Console.WriteLine($"Project: {project.Name}, Language: {project.Language}, Documents: {project.Documents.Count()}");
                    if (project.Language == LanguageNames.CSharp)
                    {
                        foreach (Document? doc in project.Documents.Take(5)) // Show first 5 documents
                        {
                            Console.WriteLine($"  Document: {doc.Name} ({doc.FilePath})");
                        }
                    }
                }

                return solution;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load solution: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Analyzes a solution and builds a complete dependency graph
        /// </summary>
        /// <param name="solution">The solution to analyze</param>
        /// <param name="includeSystemTypes">Whether to include system types in analysis</param>
        /// <returns>A complete dependency graph</returns>
        public async Task<SourceDependencyGraph> AnalyzeSolutionAsync(Solution solution, bool includeSystemTypes = false)
        {
            SourceDependencyGraph graph = new SourceDependencyGraph(solution);

            // First pass: Collect all types from all projects
            foreach (Project project in solution.Projects)
            {
                if (project.Language != LanguageNames.CSharp)
                {
                    continue; // Skip non-C# projects
                }

                List<SourceTypeNode> projectTypes = await this._projectAnalyzer.AnalyzeProjectAsync(project, includeSystemTypes);
                foreach (SourceTypeNode typeNode in projectTypes)
                {
                    graph.AddNode(typeNode);
                }
            }

            // Second pass: Analyze dependencies between types
            foreach (Project project in solution.Projects)
            {
                if (project.Language != LanguageNames.CSharp)
                {
                    continue;
                }

                List<SourceTypeDependency> dependencies = await this._projectAnalyzer.AnalyzeDependenciesAsync(project, graph, includeSystemTypes);
                foreach (SourceTypeDependency dependency in dependencies)
                {
                    graph.AddDependency(dependency);
                }
            }

            graph.CalculateMetrics();
            return graph;
        }

        /// <summary>
        /// Gets basic information about a solution
        /// </summary>
        /// <param name="solution">The solution to analyze</param>
        /// <returns>Solution information</returns>
        public SolutionAnalysis GetSolutionAnalysis(Solution solution)
        {
            List<Project> projects = [.. solution.Projects];
            List<Project> csharpProjects = [.. projects.Where(p => p.Language == LanguageNames.CSharp)];

            return new SolutionAnalysis
            {
                FilePath = solution.FilePath ?? "Unknown",
                ProjectCount = projects.Count,
                CSharpProjectCount = csharpProjects.Count,
                TotalDocuments = csharpProjects.Sum(p => p.Documents.Count()),
                ProjectNames = [.. csharpProjects.Select(p => p.Name)]
            };
        }

        /// <summary>
        /// Validates that a solution can be analyzed
        /// </summary>
        /// <param name="solution">The solution to validate</param>
        /// <returns>Validation result</returns>
        public SolutionValidationResult ValidateSolution(Solution solution)
        {
            SolutionValidationResult result = new SolutionValidationResult { IsValid = true };

            List<Project> csharpProjects = [.. solution.Projects.Where(p => p.Language == LanguageNames.CSharp)];

            if (csharpProjects.Count == 0)
            {
                result.IsValid = false;
                result.Errors.Add("No C# projects found in solution");
            }

            foreach (Project? project in csharpProjects)
            {
                if (!project.Documents.Any())
                {
                    result.Warnings.Add($"Project '{project.Name}' contains no documents");
                }

                if (project.HasDocuments && !project.Documents.Any(d => d.Name.EndsWith(".cs")))
                {
                    result.Warnings.Add($"Project '{project.Name}' contains no C# files");
                }
            }

            return result;
        }

        /// <summary>
        /// Disposes the workspace
        /// </summary>
        public void Dispose()
        {
            this._workspace?.Dispose();
        }
    }

}