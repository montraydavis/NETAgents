using NETAgents;
using NETAgents.Core;
using NETAgents.Models;
using NETAgents.Inference;

InitMSBuild.EnsureMSBuildLocated();

Directory.CreateDirectory("./.tmp");

string endpoint = Environment.GetEnvironmentVariable("AOAI_ENDPOINT") ?? string.Empty;
string apiKey = Environment.GetEnvironmentVariable("AOAI_API_KEY") ?? string.Empty;

var solutionAnalysisTool = new SolutionAnalysisTool();

ToolCallingAgent agent = new ToolCallingAgent([solutionAnalysisTool], new AzureOpenAIModel("gpt-4.1", endpoint, apiKey, false));

Console.WriteLine("Starting agent execution...");
var result = await agent.RunAsync("Analyze the solution at '/Users/montraydavis/NETAgents/NETAgents.sln' and give me basic project information");
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