using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NETAgents.Tools.Processing.Models;
using NETAgents.Tools.Processing.Models.Ast;
using SmolConv.Inference;
using SmolConv.Models;
using SmolConv.Tools;

namespace NETAgents.Tools.Processing.Services;

public class MultiLevelFileProcessorService : IMultiLevelFileProcessorService
{
    private readonly ILogger<MultiLevelFileProcessorService> _logger;
    private readonly ProcessingOptions _options;
    private Model? _model;
    private readonly object _modelLock = new object();

    public MultiLevelFileProcessorService(ILogger<MultiLevelFileProcessorService> logger, ProcessingOptions options)
    {
        _logger = logger;
        _options = options;
    }

    private Model GetOrCreateModel()
    {
        if (_model != null)
            return _model;

        lock (_modelLock)
        {
            if (_model != null)
                return _model;

            string? endpoint = Environment.GetEnvironmentVariable("AOAI_ENDPOINT");
            string? apiKey = Environment.GetEnvironmentVariable("AOAI_API_KEY");
            
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Azure OpenAI credentials not configured. Please set AOAI_ENDPOINT and AOAI_API_KEY environment variables.");
            }
            
            _logger.LogInformation("Initializing Azure OpenAI model with endpoint: {Endpoint}", endpoint);
            _model = new AzureOpenAIModel("gpt-4.1", endpoint, apiKey);
            return _model;
        }
    }

    public async Task<ProcessingResult> ProcessLevelAsync(MultiLevelProcessingJob job, ProcessingLevel level, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing {Level} for file: {FilePath}", level, job.FilePath);
        
        Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            string prompt = level switch
            {
                ProcessingLevel.Ast => CreateAstProcessingPrompt(job.Content),
                ProcessingLevel.DomainKeywords => CreateDomainKeywordsPrompt(job.Content),
                _ => throw new ArgumentException($"Unsupported processing level: {level}")
            };
            
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ProcessingTimeout);
            
            Model model = GetOrCreateModel();
            ChatMessage result = await model.GenerateAsync(
                new List<ChatMessage> { new ChatMessage(MessageRole.User, prompt) },
                null,
                timeoutCts.Token
            );
            
            stopwatch.Stop();
            
            string content = result.Content?.ToString() ?? string.Empty;
            
            // Validate result based on level
            if (level == ProcessingLevel.DomainKeywords)
            {
                ValidateDomainKeywordsJson(content);
            }
            else if (level == ProcessingLevel.Ast)
            {
                ValidateAstJson(content);
            }
            
            // Additional safety check - ensure content is valid JSON
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException($"Empty content returned for {level} processing");
            }
            
            // Basic JSON validation to catch obvious malformed JSON
            try
            {
                using JsonDocument doc = JsonDocument.Parse(content);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Invalid JSON returned for {Level} processing of {FilePath}", level, job.FilePath);
                throw new InvalidOperationException($"Invalid JSON returned for {level} processing: {jsonEx.Message}", jsonEx);
            }
            
            _logger.LogInformation(
                "{Level} processing completed for {FilePath} in {ElapsedMilliseconds}ms", 
                level, job.FilePath, stopwatch.ElapsedMilliseconds
            );
            
            return new ProcessingResult
            {
                IsSuccess = true,
                Content = content,
                ProcessingDuration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("{Level} processing cancelled for {FilePath}", level, job.FilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            string errorMessage = $"{level} processing timed out after {_options.ProcessingTimeout.TotalMilliseconds}ms";
            _logger.LogError(errorMessage + " for {FilePath}", job.FilePath);
            
            return new ProcessingResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ProcessingDuration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing {Level} for file {FilePath}", level, job.FilePath);
            
            return new ProcessingResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                ProcessingDuration = stopwatch.Elapsed
            };
        }
    }

    private static string CreateAstProcessingPrompt(string content)
    {
        return $$$"""
        ---
        instructions: |
            Extract a simplified Abstract Syntax Tree (AST) from C# source code embedded in markdown files following STRICT JSON formatting rules.
            
            CRITICAL JSON STRUCTURE REQUIREMENTS:
            
            1. RESPONSE FORMAT - NO WRAPPERS:
                - Return ONLY the JSON object directly
                - NO "root" property, NO "response" wrapper, NO "data" wrapper
                - The JSON must start with { and end with }
                - MUST always have type: "CompilationUnit" as the first property
            
            2. REQUIRED TOP-LEVEL PROPERTIES (ALL MUST BE PRESENT):
                "type": "CompilationUnit" (exactly this string)
                "namespace": string (empty string "" if no namespace)
                "usings": array of strings (empty array [] if no usings)
                "classes": array of class objects (empty array [] if no classes)
                "interfaces": array of interface objects (empty array [] if no interfaces)
                "enums": array of enum objects (empty array [] if no enums)
                "records": array of record objects (empty array [] if no records)
                "structs": array of struct objects (empty array [] if no structs)
            
            3. ARRAY REQUIREMENTS:
                - ALL arrays must be present and non-null
                - Use empty arrays [] instead of null
                - Even if no items exist, return [] not null
                - This is critical for JSON parsing
            
            4. STRING REQUIREMENTS:
                - ALL strings must be non-null
                - Use empty string "" instead of null
                - Do not include C# keywords like "namespace", "using", "class", etc.
                - Extract only the actual values
            
            5. INPUT PROCESSING:
                - Extract YAML frontmatter information (namespace, type name, kind)
                - Extract C# source code from markdown code blocks
                - Combine both sources for complete information
            
            6. TYPE DECLARATION STRUCTURE:
                Each type object must have these properties:
                - "name": string (required, non-empty, from YAML frontmatter if available)
                - "modifiers": array of strings (empty array [] if no modifiers)
                - "baseTypes": array of strings (empty array [] if no base types)
                - "attributes": array of strings (empty array [] if no attributes)
                - "members": array of member objects (empty array [] if no members)
            
            7. MEMBER STRUCTURE:
                Each member must have:
                - "kind": string (required: "method", "property", "field", "constructor")
                - "name": string (required, non-empty)
                - "type": string (required, non-empty)
                - "modifiers": array of strings (empty array [] if no modifiers)
                - "attributes": array of strings (empty array [] if no attributes)
                - "returnType": string (for methods, empty string "" if void)
                - "parameters": array of parameter objects (empty array [] if no parameters)
                - "hasGetter": boolean (for properties, true/false)
                - "hasSetter": boolean (for properties, true/false)
                - "setterModifier": string (for properties with setters, empty string "" if none)
            
            8. PARAMETER STRUCTURE:
                Each parameter must have:
                - "name": string (required, non-empty)
                - "type": string (required, non-empty)
                - "defaultValue": string (empty string "" if no default)
                - "isParams": boolean (true/false)
                - "isRef": boolean (true/false)
                - "isOut": boolean (true/false)
            
            JSON SYNTAX RULES:
            - Use double quotes for all strings
            - No trailing commas
            - No comments
            - No extra whitespace or formatting
            - Must be valid JSON that System.Text.Json can parse
            
            EXAMPLE RESPONSE FORMAT:
            {
              "type": "CompilationUnit",
              "namespace": "MyApp.Services",
              "usings": ["System", "System.Threading.Tasks"],
              "classes": [
                {
                  "name": "UserService",
                  "modifiers": ["public"],
                  "baseTypes": ["IUserService"],
                  "attributes": [],
                  "members": [
                    {
                      "kind": "field",
                      "name": "_connectionString",
                      "type": "string",
                      "modifiers": ["private", "readonly"],
                      "attributes": [],
                      "returnType": "",
                      "parameters": [],
                      "hasGetter": false,
                      "hasSetter": false,
                      "setterModifier": ""
                    }
                  ]
                }
              ],
              "interfaces": [],
              "enums": [],
              "records": [],
              "structs": []
            }
            
            FINAL INSTRUCTIONS:
            - Return ONLY the JSON object, no explanatory text
            - Ensure ALL required properties are present
            - Use empty arrays [] and empty strings "" instead of null
            - Validate your JSON syntax before returning
            - The response must parse successfully with System.Text.Json
            
            Source code to analyze:
        {{{content}}}
        """;
    }

    private static string CreateDomainKeywordsPrompt(string content)
    {
        return $$$"""
        ---
        instructions: |
            Analyze the provided C# source code embedded in markdown files and identify the primary business domains it represents.
            
            CRITICAL JSON STRUCTURE REQUIREMENTS:
            
            1. RESPONSE FORMAT - NO WRAPPERS:
                - Return ONLY the JSON object directly
                - NO "response" wrapper, NO "data" wrapper, NO "result" wrapper
                - The JSON must start with { and end with }
                - Must contain exactly one property: "domains"
            
            2. REQUIRED STRUCTURE:
                "domains": array of domain objects (empty array [] if no domains found)
                Each domain object must have exactly these properties:
                - "name": string (required, non-empty, the domain name)
                - "reasoning": string (required, non-empty, explanation for the domain)
            
            3. ARRAY REQUIREMENTS:
                - "domains" array must be present and non-null
                - Use empty array [] if no domains can be identified
                - Limit to 3-5 most relevant domains
                - Order by relevance/confidence
            
            4. STRING REQUIREMENTS:
                - ALL strings must be non-null
                - Use empty string "" instead of null
                - Domain names should be specific and descriptive
                - Reasoning should be substantive and evidence-based
            
            5. INPUT PROCESSING:
                - Extract YAML frontmatter information (namespace, type name, kind)
                - Extract C# source code from markdown code blocks
                - Analyze both sources for domain identification
            
            6. DOMAIN IDENTIFICATION RULES:
                - Focus on business logic, not technical implementation details
                - Consider class names, method names, properties, and comments
                - Look for domain-specific terminology and concepts
                - Identify the primary business purpose of the code
                - Use the type information from YAML frontmatter for context
            
            7. DOMAIN CATEGORIES TO CONSIDER:
                - Business domains (e.g., "E-commerce", "Financial Services", "Healthcare")
                - Technical domains (e.g., "Data Processing", "Authentication", "Logging")
                - Industry verticals (e.g., "Insurance", "Banking", "Retail")
                - Functional areas (e.g., "User Management", "Order Processing", "Inventory")
                - Framework domains (e.g., "AI/ML", "Web Development", "Data Analysis")
            
            8. QUALITY GUIDELINES:
                - Be specific rather than generic (prefer "Order Processing" over "Business Logic")
                - Focus on primary domains, not every possible classification
                - Ensure reasoning is substantive and evidence-based
                - Avoid duplicate or overlapping domain names
                - Consider the broader context from the YAML frontmatter
            
            JSON SYNTAX RULES:
            - Use double quotes for all strings
            - No trailing commas
            - No comments
            - No extra whitespace or formatting
            - Must be valid JSON that System.Text.Json can parse
            
            EXAMPLE RESPONSE FORMAT:
            {
              "domains": [
                {
                  "name": "AI/ML Processing",
                  "reasoning": "The RunResult class contains AI/ML specific fields like TokenUsage, Steps (likely processing steps), and State management, indicating this is part of an AI processing system."
                },
                {
                  "name": "Data Processing Pipeline",
                  "reasoning": "The class manages processing results with Steps collection and State tracking, suggesting a data processing pipeline with step-by-step execution tracking."
                }
              ]
            }
            
            FINAL INSTRUCTIONS:
            - Return ONLY the JSON object, no explanatory text
            - Ensure the "domains" array is present
            - Use empty array [] if no domains can be identified
            - Validate your JSON syntax before returning
            - The response must parse successfully with System.Text.Json
            
            Source code to analyze:
            {content}
        """;
    }

    private static void ValidateDomainKeywordsJson(string jsonContent)
    {
        try
        {
            DomainKeywordsResponse? response = JsonSerializer.Deserialize<DomainKeywordsResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (response?.Domains == null || !response.Domains.Any())
            {
                throw new InvalidOperationException("Domain keywords response must contain at least one domain");
            }
            
            foreach (DomainKeyword domain in response.Domains)
            {
                if (string.IsNullOrWhiteSpace(domain.Name))
                    throw new InvalidOperationException("Domain name cannot be empty");
                
                if (string.IsNullOrWhiteSpace(domain.Reasoning))
                    throw new InvalidOperationException("Domain reasoning cannot be empty");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON format for domain keywords: {ex.Message}", ex);
        }
    }

    private static void ValidateAstJson(string jsonContent)
    {
        try
        {
            AstCompilationUnit? root = JsonSerializer.Deserialize<AstCompilationUnit>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            
            if (root == null)
            {
                throw new InvalidOperationException("AST response must contain a valid root node");
            }
            
            // Validate root node structure
            if (string.IsNullOrWhiteSpace(root.Type))
                throw new InvalidOperationException("AST root node type cannot be empty");
            
            if (root.Type != "CompilationUnit")
                throw new InvalidOperationException("AST root node must be of type 'CompilationUnit'");
            
            // Validate namespace (can be empty string)
            if (root.Namespace == null)
                throw new InvalidOperationException("AST root node must have a namespace property (can be empty string)");
            
            // Validate collections are not null
            if (root.Usings == null)
                throw new InvalidOperationException("AST root node must have a usings array");
            
            if (root.Classes == null)
                throw new InvalidOperationException("AST root node must have a classes array");
            
            if (root.Interfaces == null)
                throw new InvalidOperationException("AST root node must have an interfaces array");
            
            if (root.Enums == null)
                throw new InvalidOperationException("AST root node must have an enums array");
            
            if (root.Records == null)
                throw new InvalidOperationException("AST root node must have a records array");
            
            if (root.Structs == null)
                throw new InvalidOperationException("AST root node must have a structs array");
            
            // Validate members in all type collections
            ValidateMembers(root.Classes);
            ValidateMembers(root.Interfaces);
            ValidateMembers(root.Records);
            ValidateMembers(root.Structs);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON format for AST: {ex.Message}", ex);
        }
    }
    
    private static void ValidateMembers<T>(List<T> types) where T : class
    {
        foreach (T type in types)
        {
            // Use reflection to get the Members property from each type
            PropertyInfo? membersProperty = type.GetType().GetProperty("Members");
            if (membersProperty?.GetValue(type) is List<AstMember> members)
            {
                foreach (AstMember member in members)
                {
                    if (string.IsNullOrWhiteSpace(member.Kind))
                        throw new InvalidOperationException("AST member must have a valid kind");
                    
                    if (string.IsNullOrWhiteSpace(member.Name))
                        throw new InvalidOperationException("AST member name cannot be empty");
                    
                    // Validate member-specific properties
                    if (member.Kind == "property")
                    {
                        if (!member.HasGetter.HasValue)
                            throw new InvalidOperationException("Property must specify hasGetter");
                        
                        if (!member.HasSetter.HasValue)
                            throw new InvalidOperationException("Property must specify hasSetter");
                        
                        // Note: SetterModifier can be empty string for public setters (default)
                        // Only validate if it's explicitly set to a non-public modifier
                    }
                    
                    if (member.Kind == "method" || member.Kind == "constructor")
                    {
                        // ReturnType can be empty string for void methods
                        if (member.Kind == "method" && member.ReturnType == null)
                            throw new InvalidOperationException("Method must specify returnType (can be empty string for void)");
                        
                        if (member.Parameters == null)
                            throw new InvalidOperationException("Method/constructor must have a parameters array");
                        
                        foreach (AstParameter param in member.Parameters)
                        {
                            if (string.IsNullOrWhiteSpace(param.Name))
                                throw new InvalidOperationException("Parameter name cannot be empty");
                            
                            if (string.IsNullOrWhiteSpace(param.Type))
                                throw new InvalidOperationException("Parameter type cannot be empty");
                        }
                    }
                }
            }
        }
    }
}