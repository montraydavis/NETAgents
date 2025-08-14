# Python to C# Agent Types Mapping

This document shows the 1:1 mapping between the Python `agent_types.py` and C# agent type implementations.

## Core Classes

### AgentType (Base Class)

**Python:**
```python
class AgentType:
    def __init__(self, value):
        self._value = value
    
    def to_raw(self):
        logger.error("This is a raw AgentType of unknown type...")
        return self._value
    
    def to_string(self) -> str:
        logger.error("This is a raw AgentType of unknown type...")
        return str(self._value)
```

**C#:**
```csharp
public abstract class AgentType
{
    protected object _value;
    
    protected AgentType(object value)
    {
        _value = value;
    }
    
    public abstract object ToRaw();
    public abstract string ToString();
    
    protected virtual object ToRawDefault()
    {
        System.Diagnostics.Debug.WriteLine("This is a raw AgentType of unknown type...");
        return _value;
    }
    
    protected virtual string ToStringDefault()
    {
        System.Diagnostics.Debug.WriteLine("This is a raw AgentType of unknown type...");
        return _value?.ToString() ?? string.Empty;
    }
}
```

### AgentText

**Python:**
```python
class AgentText(AgentType, str):
    def to_raw(self):
        return self._value
    
    def to_string(self):
        return str(self._value)
```

**C#:**
```csharp
public class AgentText : AgentType
{
    public AgentText(object value) : base(value) { }
    
    public override object ToRaw() => _value;
    public override string ToString() => _value?.ToString() ?? string.Empty;
    
    // Implicit conversion to string to match Python's str inheritance
    public static implicit operator string(AgentText agentText) => agentText.ToString();
}
```

### AgentImage

**Python:**
```python
class AgentImage(AgentType, PIL.Image.Image):
    def __init__(self, value):
        # Complex initialization with PIL, tensors, file paths
        # to_raw() returns PIL.Image.Image
        # to_string() returns file path
```

**C#:**
```csharp
public class AgentImage : AgentType
{
    private object? _raw;
    private string? _path;
    private object? _tensor;
    
    public AgentImage(object value) : base(value)
    {
        // Complex initialization with System.Drawing, tensors, file paths
        // ToRaw() returns System.Drawing.Image
        // ToString() returns file path
    }
}
```

### AgentAudio

**Python:**
```python
class AgentAudio(AgentType, str):
    def __init__(self, value, samplerate=16_000):
        # Handles audio files, tensors, sample rates
        # to_raw() returns torch.Tensor
        # to_string() returns file path
```

**C#:**
```csharp
public class AgentAudio : AgentType
{
    private string? _path;
    private object? _tensor;
    public int SampleRate { get; set; }
    
    public AgentAudio(object value, int sampleRate = 16000) : base(value)
    {
        // Handles audio files, tensors, sample rates
        // ToRaw() returns tensor object
        // ToString() returns file path
    }
}
```

## Utility Functions

### Type Mapping

**Python:**
```python
_AGENT_TYPE_MAPPING = {"string": AgentText, "image": AgentImage, "audio": AgentAudio}
```

**C#:**
```csharp
private static readonly Dictionary<string, Type> _agentTypeMapping = new()
{
    { "string", typeof(AgentText) },
    { "image", typeof(AgentImage) },
    { "audio", typeof(AgentAudio) }
};
```

### Output Type Handling

**Python:**
```python
def handle_agent_output_types(output: Any, output_type: str | None = None) -> Any:
    if output_type in _AGENT_TYPE_MAPPING:
        decoded_outputs = _AGENT_TYPE_MAPPING[output_type](output)
        return decoded_outputs
    
    if isinstance(output, str):
        return AgentText(output)
    if isinstance(output, PIL.Image.Image):
        return AgentImage(output)
    # ... tensor handling
    return output
```

**C#:**
```csharp
public static AgentType? HandleAgentOutputTypes(object? output, string? outputType = null)
{
    if (!string.IsNullOrEmpty(outputType) && _agentTypeMapping.ContainsKey(outputType))
    {
        var agentType = _agentTypeMapping[outputType];
        return (AgentType?)Activator.CreateInstance(agentType, output);
    }
    
    if (output is string str)
        return new AgentText(str);
    if (output is System.Drawing.Image image)
        return new AgentImage(image);
    // ... tensor handling
    return null;
}
```

### Input Type Handling

**Python:**
```python
def handle_agent_input_types(*args, **kwargs):
    args = [(arg.to_raw() if isinstance(arg, AgentType) else arg) for arg in args]
    kwargs = {k: (v.to_raw() if isinstance(v, AgentType) else v) for k, v in kwargs.items()}
    return args, kwargs
```

**C#:**
```csharp
public static (object?[] args, Dictionary<string, object?> kwargs) HandleAgentInputTypes(
    object?[] args, Dictionary<string, object?> kwargs)
{
    var processedArgs = args.Select(arg => arg is AgentType agentType ? agentType.ToRaw() : arg).ToArray();
    var processedKwargs = kwargs.ToDictionary(
        kvp => kvp.Key, 
        kvp => kvp.Value is AgentType agentType ? agentType.ToRaw() : kvp.Value);
    return (processedArgs, processedKwargs);
}
```

## Integration with ActionOutput

**Python:** (No direct equivalent - handled at agent level)

**C#:**
```csharp
public record ActionOutput
{
    public object? Output { get; init; }
    public AgentType? TypedOutput { get; init; }
    public bool IsFinalAnswer { get; init; }
    public string? OutputType { get; init; }
    
    // Automatically converts to typed output
    public ActionOutput(object? output, bool isFinalAnswer, string? outputType = null)
    {
        TypedOutput = AgentTypeMapping.HandleAgentOutputTypes(output, outputType);
    }
}
```

## Key Differences and Limitations

1. **Tensor Support**: Python uses PyTorch tensors, C# would need ML.NET or similar
2. **Image Processing**: Python uses PIL, C# uses System.Drawing
3. **Audio Processing**: Python uses soundfile/torch, C# needs NAudio or similar
4. **Inheritance**: Python uses multiple inheritance, C# uses composition and implicit operators
5. **Error Handling**: Python uses logging, C# uses Debug.WriteLine

## Testing

The `AgentTypeTests.cs` file provides comprehensive tests to validate the 1:1 mapping behavior.
