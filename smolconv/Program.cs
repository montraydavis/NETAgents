// See https://aka.ms/new-console-template for more information

using SmolConv.Core;
using SmolConv.Exceptions;

var agent = new ToolCallingAgent([], new OpenAISemanticKernelModel("gpt-4.1", ""));

var result = await agent.RunAsync("invoke final_answer: 'Hello, how are you?'");

Console.WriteLine(result);