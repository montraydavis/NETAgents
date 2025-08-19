// See https://aka.ms/new-console-template for more information

using MCPCSharpRelevancy.Models;
using MCPCSharpRelevancy.Services.Analysis;
using SmolConv;
using SmolConv.Core;
using SmolConv.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using SmolConv.Inference;


Directory.CreateDirectory("tmp");

InitMSBuild.EnsureMSBuildLocated();

SolutionAnalyzer analyzer = new SolutionAnalyzer();
Solution solution = await analyzer.LoadSolutionAsync("/Users/montraydavis/NETAgents/NETAgents.sln");
SourceDependencyGraph analysis = await analyzer.AnalyzeSolutionAsync(solution, false);

// Process each node and generate markdown documentation
foreach (SourceTypeNode node in analysis.Nodes.Values)
{
    // Filter out self-references
    List<SourceTypeDependency> dependencies = analysis.GetDependenciesOf(node.FullName)
        .Where(dep => dep.SourceType.ToDisplayString() != node.FullName)
        .ToList();
    List<SourceTypeDependency> dependents = analysis.GetDependentsOf(node.FullName)
        .Where(dep => dep.TargetType.ToDisplayString() != node.FullName)
        .ToList();
    
    // Prepare data for template
    var templateData = new
    {
        NodeName = node.FullName,
        SourceCode = node.SourceCode,
        Dependencies = dependencies.Select(dep => new
        {
            TypeName = dep.SourceType.ToDisplayString(),
            SourceCode = analysis.GetNode(dep.SourceType.ToDisplayString())?.SourceCode ?? "Source code not available",
            Namespace = string.Join(".", (dep.FullName ?? "").Split(".").SkipLast(1)),
            DependencyType = dep.DependencyType,
            Member = dep.FullName ?? "Unknown"
        }).ToList(),
        Dependents = dependents.GroupBy(dep => dep.TargetType.ToDisplayString())
            .Select(group => new
            {
                TypeName = group.First().TargetType.ToDisplayString(),
                SourceCode = analysis.GetNode(group.First().TargetType.ToDisplayString())?.SourceCode ?? "Source code not available",
                Namespace = string.Join(".", (group.First().FullName ?? "").Split(".").SkipLast(1)),
                DependencyTypes = string.Join(", ", group.Select(d => d.DependencyType)),
                Members = string.Join(", ", group.Select(d => d.FullName ?? "Unknown"))
            }).ToList()
    };
    
    // Format C# code using Roslyn
    string FormatCSharpCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return code;
        
        try
        {
            // Parse the code into a syntax tree
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);
            SyntaxNode root = syntaxTree.GetRoot();
            
            // Format the code using Roslyn's formatter
            AdhocWorkspace workspace = new AdhocWorkspace();
            SyntaxNode formattedRoot = Formatter.Format(root, workspace);
            
            return formattedRoot.ToFullString();
        }
        catch (Exception)
        {
            // Fallback to original code if formatting fails
            return code;
        }
    }
    
    // Format markdown for consistent output
    string FormatMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return markdown;
        
        try
        {
            // Normalize line endings
            string normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Remove excessive blank lines (more than 2 consecutive)
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\n{3,}", "\n\n");
            
            // Trim and ensure proper ending
            return normalized.Trim() + "\n";
        }
        catch (Exception)
        {
            // Fallback to original markdown if formatting fails
            return markdown;
        }
    }
    
    string markdown = $"---\n" +
                      $"node:\n" +
                      $"  fullName: \"{node.FullName}\"\n" +
                      $"  filePath: \"{node.FilePath}\"\n" +
                      $"  kind: \"{node.Symbol.TypeKind}\"\n" +
                      $"  namespace: \"{node.Namespace}\"\n" +
                      $"  name: \"{node.Name}\"\n" +
                      $"  dependencyCount: {node.Dependencies.Count}\n" +
                      $"  dependentCount: {node.Dependents.Count}\n" +
                      $"---\n\n" +
                      $"<!-- Source Code Path: {node.FilePath} -->\n\n" +
                      $"# {templateData.NodeName}\n\n" +
                      "## Source Code\n\n" +
                      $"```csharp\n" +
                      $"namespace {string.Join(".", templateData.NodeName.Split(".").SkipLast(1))};\n\n" +
                      $"{FormatCSharpCode(templateData.SourceCode)}\n" +
                      "```\n\n" +
                      (templateData.Dependencies.Any() ? 
                          "## Dependencies (Dependent On)\n\n" +
                          string.Join("\n\n", templateData.Dependencies.Select(dep => 
                              $"### {dep.TypeName}\n\n" +
                              $"```csharp\n" +
                              $"namespace {dep.Namespace};\n\n" +
                              $"{FormatCSharpCode(dep.SourceCode)}\n" +
                              "```\n\n" +
                              $"**Dependency Type:** `{dep.DependencyType}`  \n" +
                              $"**Member:** `{dep.Member}`\n\n" +
                              "---\n"
                          )) + "\n" : "") +
                      (templateData.Dependents.Any() ? 
                          "## Dependents (Dependent By)\n\n" +
                          string.Join("\n\n", templateData.Dependents.Select(dep => 
                              $"### {dep.TypeName}\n\n" +
                              $"```csharp\n" +
                              $"namespace {dep.Namespace};\n\n" +
                              $"{FormatCSharpCode(dep.SourceCode)}\n" +
                              "```\n\n" +
                              $"**Dependency Types:** `{dep.DependencyTypes}`  \n" +
                              $"**Members:** `{dep.Members}`\n\n" +
                              "---\n"
                          )) + "\n" : "") +
                      "---\n";
    
    // Format the final markdown
    string formattedMarkdown = FormatMarkdown(markdown);
    
    // Write to file
    string fileName = $"/Users/montraydavis/NETAgents/smolconv/bin/Debug/net9.0/tmp/{node.FullName.Replace("<", "").Replace(">", "").Replace(" ", "_")}_dependencies.md";
    await File.WriteAllTextAsync(fileName, formattedMarkdown);
    
    Console.WriteLine($"Generated documentation for {node.FullName} -> {fileName}");
}

string endpoint = Environment.GetEnvironmentVariable("AOAI_ENDPOINT") ?? string.Empty;
string apiKey = Environment.GetEnvironmentVariable("AOAI_API_KEY") ?? string.Empty;
ToolCallingAgent agent = new ToolCallingAgent([], new AzureOpenAIModel("gpt-4.1", endpoint, apiKey));

Console.WriteLine("Starting agent execution...");
object result = await agent.RunAsync("What is the capital of paris ?");
Console.WriteLine("Agent execution completed.");

// Look for the final answer in the agent's memory steps
string finalAnswer = "No final answer found";
foreach (MemoryStep step in agent.Memory.Steps)
{
    if (step is ActionStep actionStep && actionStep.IsFinalAnswer && actionStep.ActionOutput != null)
    {
        finalAnswer = actionStep.ActionOutput.ToString() ?? "Empty final answer";
        break;
    }
}

Console.WriteLine("=== RESULTS ===");
Console.WriteLine($"Final Answer: {finalAnswer}");
Console.WriteLine($"Result Type: {result?.GetType().Name ?? "null"}");
Console.WriteLine($"Result: {result}");
Console.WriteLine("=== END RESULTS ===");

while(true){
    
}