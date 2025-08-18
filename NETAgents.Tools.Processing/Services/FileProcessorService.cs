using SmolConv.Inference;
using SmolConv.Models;
using NETAgents.Tools.Processing.Models;

namespace NETAgents.Tools.Processing.Services;

public interface IFileProcessorService
{
    Task<string> ProcessFileAsync(FileProcessingJob job, CancellationToken cancellationToken = default);
}

public class FileProcessorService : IFileProcessorService
{
    private readonly ILogger<FileProcessorService> _logger;
    private readonly AzureOpenAIModel _model;
    private readonly ProcessingOptions _options;

    public FileProcessorService(ILogger<FileProcessorService> logger, ProcessingOptions options)
    {
        _logger = logger;
        _options = options;
        
        _model = new AzureOpenAIModel(
            "gpt-4o-mini", 
            Environment.GetEnvironmentVariable("AOAI_ENDPOINT") ?? string.Empty, 
            Environment.GetEnvironmentVariable("AOAI_API_KEY") ?? string.Empty
        );
    }

    public async Task<string> ProcessFileAsync(FileProcessingJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing file: {FilePath}", job.FilePath);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Create the processing prompt
            var processingPrompt = CreateProcessingPrompt(job.Content);
            
            // Process with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);
            
            var result = await _model.GenerateAsync(
                new List<ChatMessage> { new ChatMessage(MessageRole.User, processingPrompt) },
                null,
                timeoutCts.Token
            );
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "File {FilePath} processed successfully in {ElapsedMilliseconds}ms", 
                job.FilePath, 
                stopwatch.ElapsedMilliseconds
            );
            
            return result.Content?.ToString() ?? string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("File processing cancelled for {FilePath}", job.FilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError("File processing timed out for {FilePath} after {Timeout}ms", 
                job.FilePath, _options.ProcessingTimeout.TotalMilliseconds);
            throw new TimeoutException($"Processing timed out after {_options.ProcessingTimeout.TotalMilliseconds}ms");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing file {FilePath}", job.FilePath);
            throw;
        }
    }

    private static string CreateProcessingPrompt(string content)
    {
        return $$$"""
        ---
        instructions: |
            Extract a simplified Abstract Syntax Tree (AST) from C# source code following strict guidelines.
            
            MANDATORY STRUCTURE RULES:
            
            1. ROOT NODE REQUIREMENTS:
                - MUST always be type "CompilationUnit"
                - MUST contain exactly these top-level properties: type, namespace, usings, classes, interfaces, enums, records, structs
                - namespace MUST be string (empty string if no namespace)
                - All collection properties MUST be arrays (never null or undefined)
            
            2. NAMESPACE EXTRACTION:
                - Extract only the namespace name (e.g., "MyApp.Services" not "namespace MyApp.Services")
                - If no namespace declared, use empty string ""
                - Do NOT include nested namespace syntax
            
            3. USING DIRECTIVES:
                - Extract only the imported namespace/type name
                - Remove "using" keyword and semicolons
                - Store as simple string array
                - Example: "using System.Text;" becomes "System.Text"
            
            4. TYPE DECLARATION RULES:
                - name: MUST be the exact type identifier
                - modifiers: MUST be array of strings (public, private, static, abstract, sealed, etc.)
                - baseTypes: MUST be array of inherited classes/interfaces (empty array if none)
                - attributes: MUST be array of attribute names without brackets
                - members: MUST be array of member objects
            
            5. MEMBER CLASSIFICATION (kind field):
                - "field" - fields and constants
                - "property" - properties with get/set
                - "method" - methods and functions
                - "constructor" - constructors
                - "destructor" - finalizers/destructors
                - "event" - events
                - "indexer" - indexers
            
            6. PROPERTY SPECIFIC RULES:
                - MUST include hasGetter (boolean)
                - MUST include hasSetter (boolean)
                - IF hasSetter is true, MUST include setterModifier (public, private, protected, internal)
                - type MUST be the property return type
            
            7. METHOD/CONSTRUCTOR RULES:
                - returnType: MUST be present for methods (use "void" if no return)
                - parameters: MUST be array (empty if no parameters)
                - Each parameter MUST have: name, type
                - Optional parameter properties: defaultValue, isParams, isRef, isOut
                - body: MUST be array of simplified statements
            
            8. STATEMENT SIMPLIFICATION:
                - "assignment": target and value properties
                - "return": value property (expression as string)
                - "call": method and arguments properties
                - "declaration": name, type, and optional initializer
                - "if": condition and body properties
                - "loop": type (for/while/foreach), condition, body
            
            9. ATTRIBUTE PROCESSING:
                - Remove square brackets [ ]
                - Extract only attribute name and parameters as single string
                - Example: [HttpGet("{id}")] becomes "HttpGet(\"{id}\")"
            
            10. TYPE REFERENCE RULES:
                - Preserve generic type syntax: List<string>, Task<User>
                - Preserve nullable syntax: string?, int?
                - Preserve array syntax: string[], int[,]
                - Use fully qualified names when available
            
            11. MODIFIER EXTRACTION:
                - Extract ALL access and non-access modifiers
                - Include: public, private, protected, internal, static, abstract, virtual, override, sealed, async, readonly, const
                - Store as lowercase strings in array
            
            12. ENUM SPECIFIC RULES:
                - values: MUST be array of enum member names (strings)
                - Include explicit values if specified: ["Success = 0", "Error = 1"]
                - For simple enums, just member names: ["Pending", "Running"]
            
            13. ERROR HANDLING:
                - If any required property cannot be determined, use sensible defaults
                - Empty arrays for collections, empty strings for names
                - Never omit required properties
            
            PROCESSING ORDER:
            1. Parse compilation unit and extract namespace
            2. Extract all using directives
            3. Identify and classify all type declarations
            4. For each type, extract modifiers, base types, attributes
            5. Process all members within each type
            6. Simplify method bodies to essential statements
            7. Validate final structure against rules above

        examples:
            - input: |
                using System;
                using System.Collections.Generic;
                
                namespace SmolConv.Models
                {
                    public class RunResult
                    {
                        public object Output { get; set; }
                        public bool IsSuccess { get; private set; }
                        
                        public RunResult(object output, bool isSuccess = true)
                        {
                            Output = output;
                            IsSuccess = isSuccess;
                        }
                        
                        public void SetOutput(object output)
                        {
                            Output = output;
                        }
                        
                        public string GetFormattedOutput()
                        {
                            return Output?.ToString() ?? "No output";
                        }
                    }
                    
                    public interface IProcessor
                    {
                        Task<RunResult> ProcessAsync(string input);
                    }
                    
                    public enum Status
                    {
                        Pending,
                        Running,
                        Completed,
                        Failed
                    }
                }
            output: |
                {
                "type": "CompilationUnit",
                "namespace": "SmolConv.Models",
                "usings": [
                    "System",
                    "System.Collections.Generic"
                ],
                "classes": [
                    {
                    "name": "RunResult",
                    "modifiers": ["public"],
                    "baseTypes": [],
                    "attributes": [],
                    "members": [
                        {
                        "kind": "property",
                        "name": "Output",
                        "type": "object",
                        "modifiers": ["public"],
                        "attributes": [],
                        "hasGetter": true,
                        "hasSetter": true,
                        "setterModifier": "public"
                        },
                        {
                        "kind": "property", 
                        "name": "IsSuccess",
                        "type": "bool",
                        "modifiers": ["public"],
                        "attributes": [],
                        "hasGetter": true,
                        "hasSetter": true,
                        "setterModifier": "private"
                        },
                        {
                        "kind": "constructor",
                        "name": "RunResult",
                        "modifiers": ["public"],
                        "attributes": [],
                        "parameters": [
                            {
                            "name": "output",
                            "type": "object"
                            },
                            {
                            "name": "isSuccess", 
                            "type": "bool",
                            "defaultValue": "true"
                            }
                        ],
                        "body": [
                            {
                            "kind": "assignment",
                            "target": "Output",
                            "value": "output"
                            },
                            {
                            "kind": "assignment", 
                            "target": "IsSuccess",
                            "value": "isSuccess"
                            }
                        ]
                        },
                        {
                        "kind": "method",
                        "name": "SetOutput",
                        "returnType": "void",
                        "modifiers": ["public"],
                        "attributes": [],
                        "parameters": [
                            {
                            "name": "output",
                            "type": "object"
                            }
                        ],
                        "body": [
                            {
                            "kind": "assignment",
                            "target": "Output", 
                            "value": "output"
                            }
                        ]
                        },
                        {
                        "kind": "method",
                        "name": "GetFormattedOutput",
                        "returnType": "string",
                        "modifiers": ["public"],
                        "attributes": [],
                        "parameters": [],
                        "body": [
                            {
                            "kind": "return",
                            "value": "Output?.ToString() ?? \"No output\""
                            }
                        ]
                        }
                    ]
                    }
                ],
                "interfaces": [
                    {
                    "name": "IProcessor",
                    "modifiers": ["public"],
                    "baseTypes": [],
                    "attributes": [],
                    "members": [
                        {
                        "kind": "method",
                        "name": "ProcessAsync",
                        "returnType": "Task<RunResult>",
                        "modifiers": [],
                        "attributes": [],
                        "parameters": [
                            {
                            "name": "input",
                            "type": "string"
                            }
                        ]
                        }
                    ]
                    }
                ],
                "enums": [
                    {
                    "name": "Status",
                    "modifiers": ["public"],
                    "attributes": [],
                    "values": [
                        "Pending",
                        "Running", 
                        "Completed",
                        "Failed"
                    ]
                    }
                ],
                "records": [],
                "structs": []
                }

            - input: |
                namespace Demo.Services
                {
                    [ApiController]
                    public class UserController : ControllerBase
                    {
                        private readonly IUserService _userService;
                        
                        public UserController(IUserService userService)
                        {
                            _userService = userService;
                        }
                        
                        [HttpGet("{id}")]
                        public async Task<User> GetUser(int id)
                        {
                            return await _userService.GetByIdAsync(id);
                        }
                    }
                }
            output: |
                {
                "type": "CompilationUnit",
                "namespace": "Demo.Services",
                "usings": [],
                "classes": [
                    {
                    "name": "UserController", 
                    "modifiers": ["public"],
                    "baseTypes": ["ControllerBase"],
                    "attributes": ["ApiController"],
                    "members": [
                        {
                        "kind": "field",
                        "name": "_userService",
                        "type": "IUserService", 
                        "modifiers": ["private", "readonly"],
                        "attributes": []
                        },
                        {
                        "kind": "constructor",
                        "name": "UserController",
                        "modifiers": ["public"],
                        "attributes": [],
                        "parameters": [
                            {
                            "name": "userService",
                            "type": "IUserService"
                            }
                        ],
                        "body": [
                            {
                            "kind": "assignment",
                            "target": "_userService",
                            "value": "userService"
                            }
                        ]
                        },
                        {
                        "kind": "method",
                        "name": "GetUser", 
                        "returnType": "Task<User>",
                        "modifiers": ["public", "async"],
                        "attributes": ["HttpGet(\"{id}\")"],
                        "parameters": [
                            {
                            "name": "id",
                            "type": "int"
                            }
                        ],
                        "body": [
                            {
                            "kind": "return",
                            "value": "await _userService.GetByIdAsync(id)"
                            }
                        ]
                        }
                    ]
                    }
                ],
                "interfaces": [],
                "enums": [],
                "records": [],
                "structs": []
                }
        ---

        {{{content}}}
        """;
    }
}
