namespace SmolConv.Exceptions
{
    using System.ClientModel;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.SemanticKernel;
    using Microsoft.SemanticKernel.Connectors.OpenAI;
    using OpenAI;
    using SmolConv.Core;
    using SmolConv.Models;
    using SmolConv.Tools;

    public class OpenAISemanticKernelModel(string modelId, string apiKey) : Model
    {
        public override ChatMessage ParseToolCalls(ChatMessage message)
        {
            // If the message already has tool calls, return as is
            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                return message;
            }

            // Try to parse tool calls from the content if it's a string
            if (message.Content is string content)
            {
                // This is a simplified implementation - you might need more sophisticated parsing
                // based on your model's output format
                var toolCalls = new List<ChatMessageToolCall>();

                // Add parsing logic here based on your model's output format
                // For now, return the original message
                return message;
            }

            return message;
        }

        public override async Task<ChatMessage> Generate(List<ChatMessage> messages, ModelCompletionOptions? options = null)
        {
            var oaiClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions()
            {
            });

            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId, apiKey)
                .Build();

            string messageHistory = string.Join("\n", messages.Select(m =>
            {
                try
                {
                    var contentDict = JsonSerializer.Deserialize<Dictionary<string, string>[]>(m.ContentString ?? throw new Exception("Content string is null"))
                    ?? throw new Exception("Content string is not a valid JSON object");

                    var text = contentDict[0]["text"].ToString();

                    return $"{m.Role}: {text}";
                }
                catch (Exception ex)
                {
                    throw;
                }
            }));

            var functionChoiceBehaviorOptions = new FunctionChoiceBehaviorOptions()
            {
                AllowConcurrentInvocation = true,
                AllowParallelCalls = true,
            };

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: functionChoiceBehaviorOptions)
            };

            try
            {
                var fn = kernel.CreateFunctionFromMethod((string final_answer) =>
                {
                    new FinalAnswerTool().Call([], new Dictionary<string, object>() { { "answer", final_answer } });
                });

                var plugin = KernelPluginFactory.CreateFromFunctions(
                    "FinalAnswerPlugin",
                    new[] { fn }
                );

                kernel.Plugins.Add(plugin);

                var response = await kernel.InvokePromptAsync(messageHistory, new KernelArguments(promptExecutionSettings));
                var res = response.GetValue<string>();

                return new ChatMessage(MessageRole.Assistant, response, response.GetValue<string>());
            }
            catch (Exception ex)
            {
                throw new Exception("Error while generating output", ex);
            }
        }
    }
}