# NETAgents

## A .NET Agentic Framework

**Target Framework:** .NET 9.0  

### Overview

NETAgents is a comprehensive .NET agentic framework that provides a deeply integrated solution for building AI agents within the C# ecosystem. This project began as a faithful rewrite of Hugging Face's `Smolagents` framework, adapted specifically for .NET developers.

The framework implements the ReAct (Reasoning and Acting) pattern, allowing agents to solve complex tasks step by step through a cycle of action and observation.

## Architecture

### Core Components

#### 1. **Agent System**
- **`MultiStepAgent`** - Abstract base class for all multi-step agents
- **`ToolCallingAgent`** - Concrete implementation for tool-calling agents
- **`AgentMemory`** - Manages conversation history and step tracking
- **`AgentLogger`** - Comprehensive logging system with multiple verbosity levels

#### 2. **Model Integration**
- **`Model`** - Abstract base class for language models
- **`AzureOpenAIModel`** - Azure OpenAI integration
- **`OpenAIModel`** - OpenAI API integration
- **`ChatMessage`** - Standardized message format with tool call support

#### 3. **Tools & Execution**
- **`BaseTool`** - Base interface for all tools
- **`Tool`** - Abstract implementation for custom tools
- **`PipelineTool`** - Specialized base for ML pipeline tools
- **`LocalPythonExecutor`** - Python code execution capabilities

#### 4. **Type System**
- **`AgentType`** - Base class for agent data types
- **`AgentText`**, **`AgentImage`**, **`AgentAudio`** - Specialized data types
- **`AgentTypeMapping`** - Handles type conversion and validation

### Key Features

✅ **ReAct Framework Implementation** - Step-by-step reasoning and acting  
✅ **Tool Integration** - Extensible tool system with validation  
✅ **Multi-Model Support** - OpenAI and Azure OpenAI integration  
✅ **Type Safety** - Comprehensive C# type system  
✅ **Memory Management** - Conversation history and state tracking  
✅ **Code Execution** - Python code execution capabilities  
✅ **Validation System** - Robust argument and tool validation  
✅ **Logging & Monitoring** - Detailed execution tracking  

## Installation & Setup

### Prerequisites
- .NET 9.0 SDK
- Azure OpenAI or OpenAI API access

### Dependencies
```xml
<PackageReference Include="Azure.AI.OpenAI" Version="2.2.0-beta.5" />
<PackageReference Include="Azure.Core" Version="1.47.2" />
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<PackageReference Include="Microsoft.SemanticKernel" Version="1.62.0" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
<PackageReference Include="Markdig" Version="0.37.0" />
<PackageReference Include="System.Drawing.Common" Version="8.0.0" />
```

## Quick Start

### Basic Agent Setup

```csharp
using SmolConv.Core;
using SmolConv.Models;
using SmolConv.Tools;

// Configure environment
string endpoint = Environment.GetEnvironmentVariable("AOAI_ENDPOINT");
string apiKey = Environment.GetEnvironmentVariable("AOAI_API_KEY");

// Create tools
var tools = new List<Tool> { /* your tools */ };

// Initialize model
var model = new AzureOpenAIModel("gpt-4", endpoint, apiKey);

// Create agent
var agent = new ToolCallingAgent(tools, model);

// Execute task
var result = await agent.RunAsync("Your task description here");
```

### Creating Custom Tools

```csharp
public class CustomTool : Tool
{
    public override string Name => "custom_tool";
    
    public override Dictionary<string, Dictionary<string, object>> Inputs => 
        new()
        {
            ["input"] = new()
            {
                ["type"] = "string",
                ["description"] = "Input description"
            }
        };

    public override async Task<object> ForwardAsync(Dictionary<string, object> arguments)
    {
        // Implement your tool logic
        string input = arguments["input"].ToString();
        return $"Processed: {input}";
    }
}
```

## Framework Components

### Agent Configuration

```csharp
var agent = new ToolCallingAgent(
    tools: toolsList,
    model: model,
    maxSteps: 10,                    // Maximum reasoning steps
    streamOutputs: false,            // Enable streaming
    instructions: "Custom prompt",   // Additional instructions
    addBaseTools: true,             // Include default tools
    verbosityLevel: LogLevel.Info   // Logging level
);
```

### Memory & State Management

The framework maintains conversation state through `AgentMemory`:

```csharp
// Access agent memory
foreach (var step in agent.Memory.Steps)
{
    if (step is ActionStep actionStep)
    {
        Console.WriteLine($"Action: {actionStep.ActionName}");
        Console.WriteLine($"Result: {actionStep.ActionOutput}");
    }
}
```

### Tool Validation

Tools are validated automatically with comprehensive error handling:

```csharp
// Tools must define input schemas
public override Dictionary<string, Dictionary<string, object>> Inputs => 
    new()
    {
        ["required_param"] = new()
        {
            ["type"] = "string",
            ["description"] = "Required parameter"
        },
        ["optional_param"] = new()
        {
            ["type"] = "integer", 
            ["optional"] = true
        }
    };
```

## Advanced Features

### Managed Agents
Support for hierarchical agent orchestration:

```csharp
var managedAgents = new List<MultiStepAgent> { subAgent1, subAgent2 };
var masterAgent = new ToolCallingAgent(tools, model, managedAgents: managedAgents);
```

### Python Execution
Execute Python code within the agent workflow:

```csharp
var pythonExecutor = new LocalPythonExecutor(
    additionalAuthorizedImports: new[] { "numpy", "pandas" },
    maxPrintOutputsLength: 1000
);
```

### Streaming & Callbacks
Real-time execution monitoring:

```csharp
var stepCallbacks = new Dictionary<Type, List<Action<MemoryStep, object>>>
{
    [typeof(ActionStep)] = new List<Action<MemoryStep, object>>
    {
        (step, result) => Console.WriteLine($"Step completed: {step}")
    }
};
```

## Project Structure

```
smolconv/
├── Core/
│   ├── MultiStepAgent.cs      # Base agent implementation
│   ├── ToolCallingAgent.cs    # Tool-calling agent
│   ├── AgentLogger.cs         # Logging system
│   ├── Logger.cs              # Pipeline tools
│   └── LocalPythonExecutor.cs # Python execution
├── Models/
│   ├── AgentTypeMapping.cs    # Type system
│   ├── MessageRole.cs         # Message types
│   └── ToolCall.cs           # Tool call models
├── Tools/                     # Tool implementations
├── Inference/                 # Model inference
├── Exceptions/                # Custom exceptions
└── Tests/                     # Unit tests
```

## Development Guidelines

Following the original `Smolagents` architecture while adapting to C# conventions:

- **Async/Await Pattern** - All operations are asynchronous
- **Nullable Reference Types** - Full null safety
- **Dependency Injection Ready** - Modular design
- **Comprehensive Testing** - Unit and integration tests
- **Documentation** - XML documentation for all public APIs

## Configuration

### Environment Variables
```bash
AOAI_ENDPOINT=https://your-resource.openai.azure.com/
AOAI_API_KEY=your-api-key
```

### Logging Levels
- `LogLevel.Debug` - Detailed debugging information  
- `LogLevel.Info` - General information (default)  
- `LogLevel.Warning` - Warning messages  
- `LogLevel.Error` - Error messages only  

## Error Handling

The framework provides comprehensive error handling:

- **`AgentToolCallError`** - Tool execution failures
- **`AgentMaxStepsError`** - Maximum steps exceeded  
- **`AgentExecutionError`** - General execution errors
- **`ValidationException`** - Tool argument validation failures

## Performance Considerations

- **Memory Management** - Efficient conversation history handling
- **Tool Execution** - Parallel tool execution support
- **Rate Limiting** - Built-in API rate limiting
- **Caching** - Response caching mechanisms

## Roadmap

- ✅ Core framework implementation
- ✅ Tool system and validation
- ✅ Azure OpenAI integration
- 🔄 Additional model providers
- 🔄 Advanced tool marketplace
- 🔄 Web UI dashboard
- 🔄 Docker containerization

---
 
**Status:** Active Development  
