# Azure OpenAI Tool Calling Fix

## Problem Description

The error `"An assistant message with 'tool_calls' must be followed by tool messages responding to each 'tool_call_id'"` occurs when the Azure OpenAI API receives a conversation that includes an assistant message with tool calls but is missing the corresponding tool response messages.

## Root Cause

The issue was in the conversation history management:

1. **ToolCallingAgent** generated assistant messages with tool calls
2. **Tool calls were executed** and results were yielded as `ToolOutput` objects
3. **Tool responses were NOT being added back to the conversation history** that gets sent to the API

Azure OpenAI requires this conversation flow:
1. User message: "Do something"
2. Assistant message: Contains tool calls with IDs like `call_wbFQgcdYIg3R4Orf1CgcSWiD`
3. Tool message: `role: "tool"`, `tool_call_id: "call_wbFQgcdYIg3R4Orf1CgcSWiD"`, `content: "tool result"`
4. Next assistant message: Can now continue the conversation

## Solution Implemented

### 1. Enhanced ActionStep Model

Added a new property to capture tool responses:

```csharp
public List<ToolOutput>? ToolResponses { get; init; }
```

### 2. Updated ToMessages Method

Modified `ActionStep.ToMessages()` to include tool response messages:

```csharp
// Add tool response messages if present
if (ToolResponses != null && ToolResponses.Count > 0)
{
    foreach (var toolResponse in ToolResponses)
    {
        // Create a tool message for each tool response
        // The content should include the tool_call_id in a format the model can extract
        string toolContent = $"Call id: {toolResponse.Id}\n{toolResponse.Observation}";
        
        var toolMessage = new ChatMessage(
            MessageRole.ToolResponse, 
            toolContent, 
            toolContent
        );
        messages.Add(toolMessage);
    }
}
```

### 3. Modified ToolCallingAgent

Updated `StepStreamAsync()` to capture tool responses and update the ActionStep:

```csharp
List<ToolOutput> toolResponses = new List<ToolOutput>(); // Capture tool responses

if (chatMessage.ToolCalls != null)
{
    await foreach (object toolOutput in ProcessToolCallsAsync(chatMessage.ToolCalls, cancellationToken))
    {
        yield return toolOutput;
        
        if (toolOutput is ToolOutput output)
        {
            toolResponses.Add(output); // Capture tool response
            // ... existing logic
        }
    }
}

// Update the action step with tool responses and model output
actionStep = actionStep with 
{ 
    ModelOutputMessage = chatMessage,
    ToolResponses = toolResponses.Count > 0 ? toolResponses : null
};
```

## How It Works

1. **Tool Execution**: When tools are called, their responses are captured in a list
2. **Message Conversion**: Tool responses are converted to `MessageRole.ToolResponse` messages with the proper `tool_call_id` format
3. **Conversation History**: These tool response messages are included in the conversation history when calling the API
4. **API Compliance**: Azure OpenAI now receives the complete conversation flow it expects

## Testing

Added comprehensive tests in `ToolCallingAgentTests.cs` to verify:
- Single tool response handling
- Multiple tool response handling  
- No tool response handling
- Proper message formatting

## Benefits

- ✅ Fixes the Azure OpenAI tool calling error
- ✅ Maintains proper conversation flow
- ✅ Preserves existing functionality
- ✅ Adds comprehensive test coverage
- ✅ No breaking changes to the public API

## Files Modified

- `NETAgents/Models/ActionStep.cs` - Added ToolResponses property and updated ToMessages
- `NETAgents/Core/ToolCallingAgent.cs` - Modified to capture and store tool responses
- `NETAgents/Core/MultiStepAgent.cs` - Added comment explaining the flow
- `NETAgents/Tests/ToolCallingAgentTests.cs` - New test file for verification





