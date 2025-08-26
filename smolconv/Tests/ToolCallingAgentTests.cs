using Xunit;
using SmolConv.Core;
using SmolConv.Models;
using SmolConv.Tools;
using System.Collections.Generic;
using System.Linq;

namespace SmolConv.Tests
{
    public class ToolCallingAgentTests
    {
        [Fact]
        public void ActionStep_ShouldIncludeToolResponsesInMessages()
        {
            // Arrange
            var toolResponse = new ToolOutput(
                id: "call_test123",
                output: "Test result",
                isFinalAnswer: false,
                observation: "Tool executed successfully",
                toolCall: new ToolCall("test_tool", new Dictionary<string, object>(), "call_test123")
            );

            var actionStep = new ActionStep(1)
            {
                ModelOutputMessage = new ChatMessage(MessageRole.Assistant, "I'll call a tool", "I'll call a tool"),
                ToolResponses = new List<ToolOutput> { toolResponse }
            };

            // Act
            var messages = actionStep.ToMessages();

            // Assert
            Assert.Equal(2, messages.Count); // Model output + tool response
            
            // Check that the tool response message is properly formatted
            var toolMessage = messages[1];
            Assert.Equal(MessageRole.ToolResponse, toolMessage.Role);
            Assert.Contains("Call id: call_test123", toolMessage.ContentString);
            Assert.Contains("Tool executed successfully", toolMessage.ContentString);
        }

        [Fact]
        public void ActionStep_ShouldHandleMultipleToolResponses()
        {
            // Arrange
            var toolResponse1 = new ToolOutput(
                id: "call_test1",
                output: "Result 1",
                isFinalAnswer: false,
                observation: "First tool executed",
                toolCall: new ToolCall("tool1", new Dictionary<string, object>(), "call_test1")
            );

            var toolResponse2 = new ToolOutput(
                id: "call_test2",
                output: "Result 2",
                isFinalAnswer: false,
                observation: "Second tool executed",
                toolCall: new ToolCall("tool2", new Dictionary<string, object>(), "call_test2")
            );

            var actionStep = new ActionStep(1)
            {
                ModelOutputMessage = new ChatMessage(MessageRole.Assistant, "I'll call multiple tools", "I'll call multiple tools"),
                ToolResponses = new List<ToolOutput> { toolResponse1, toolResponse2 }
            };

            // Act
            var messages = actionStep.ToMessages();

            // Assert
            Assert.Equal(3, messages.Count); // Model output + 2 tool responses
            
            // Check first tool response
            var toolMessage1 = messages[1];
            Assert.Equal(MessageRole.ToolResponse, toolMessage1.Role);
            Assert.Contains("Call id: call_test1", toolMessage1.ContentString);
            
            // Check second tool response
            var toolMessage2 = messages[2];
            Assert.Equal(MessageRole.ToolResponse, toolMessage2.Role);
            Assert.Contains("Call id: call_test2", toolMessage2.ContentString);
        }

        [Fact]
        public void ActionStep_ShouldHandleNoToolResponses()
        {
            // Arrange
            var actionStep = new ActionStep(1)
            {
                ModelOutputMessage = new ChatMessage(MessageRole.Assistant, "No tools needed", "No tools needed"),
                ToolResponses = null
            };

            // Act
            var messages = actionStep.ToMessages();

            // Assert
            Assert.Single(messages); // Only model output
            Assert.Equal(MessageRole.Assistant, messages[0].Role);
        }
    }
}





