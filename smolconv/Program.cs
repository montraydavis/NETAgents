// See https://aka.ms/new-console-template for more information

using SmolConv.Core;
using SmolConv.Exceptions;
using SmolConv.Models;
using System.Text.Json;

var agent = new ToolCallingAgent([], new AzureOpenAIModel("gpt-4.1", Environment.GetEnvironmentVariable("AOAI_ENDPOINT"), Environment.GetEnvironmentVariable("AOAI_API_KEY")));

Console.WriteLine("Starting agent execution...");
var result = await agent.RunAsync("What is the capital of paris ?");
Console.WriteLine("Agent execution completed.");

// Look for the final answer in the agent's memory steps
var finalAnswer = "No final answer found";
foreach (var step in agent.Memory.Steps)
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