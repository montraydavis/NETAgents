
namespace SmolConv.Core
{
    using System.Diagnostics;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using SmolConv.Models;
    using SmolConv.Tools;


    // ===============================
    // LOCAL PYTHON EXECUTOR
    // ===============================

    /// <summary>
    /// Local Python executor that runs code in the current process environment
    /// </summary>
    public class LocalPythonExecutor : PythonExecutor
    {
        private readonly Dictionary<string, object> _state = new();
        private readonly List<string> _authorizedImports;
        private readonly int? _maxPrintOutputsLength;
        private readonly StringBuilder _printOutputs = new();
        private readonly Dictionary<string, BaseTool> _tools = new();

        /// <summary>
        /// Base builtin modules that are always authorized
        /// </summary>
        public static readonly List<string> BaseBuiltinModules = new()
        {
            "collections", "datetime", "itertools", "math", "queue", "random",
            "re", "stat", "statistics", "time", "unicodedata"
        };

        /// <summary>
        /// Gets the current execution state
        /// </summary>
        public override Dictionary<string, object> State => new(_state);

        /// <summary>
        /// Gets the authorized imports
        /// </summary>
        public override List<string> AuthorizedImports => new(_authorizedImports);

        /// <summary>
        /// Initializes a new instance of the LocalPythonExecutor class
        /// </summary>
        /// <param name="additionalAuthorizedImports">Additional imports to authorize beyond base modules</param>
        /// <param name="maxPrintOutputsLength">Maximum length for print outputs</param>
        public LocalPythonExecutor(List<string>? additionalAuthorizedImports = null, int? maxPrintOutputsLength = null)
        {
            _maxPrintOutputsLength = maxPrintOutputsLength;
            _authorizedImports = new List<string>(BaseBuiltinModules);

            if (additionalAuthorizedImports != null)
            {
                _authorizedImports.AddRange(additionalAuthorizedImports);
            }

            InitializeState();
        }

        /// <summary>
        /// Initializes the execution state with built-in functions
        /// </summary>
        private void InitializeState()
        {
            _state["_print_outputs"] = _printOutputs;
            _state["print"] = new Action<object>(obj =>
            {
                string output = obj?.ToString() ?? "None";
                _printOutputs.AppendLine(output);

                if (_maxPrintOutputsLength.HasValue && _printOutputs.Length > _maxPrintOutputsLength.Value)
                {
                    int excess = _printOutputs.Length - _maxPrintOutputsLength.Value;
                    _printOutputs.Remove(0, excess);
                }
            });

            _state["final_answer"] = new Func<object, object>(answer =>
            {
                _state["_final_answer"] = answer;
                return answer;
            });
        }

        /// <summary>
        /// Executes Python code
        /// </summary>
        /// <param name="code">The Python code to execute</param>
        /// <returns>Execution result</returns>
        public override PythonExecutionResult Execute(string code)
        {
            try
            {
                // Validate imports
                ValidateImports(code);

                // Clear previous outputs
                _printOutputs.Clear();
                _state.Remove("_final_answer");

                // Execute the code using Python.NET or similar
                object? result = ExecutePythonCode(code);

                // Check if final answer was called
                bool isFinalAnswer = _state.ContainsKey("_final_answer");
                object? finalAnswer = isFinalAnswer ? _state["_final_answer"] : result;

                return new PythonExecutionResult(
                    output: finalAnswer,
                    logs: _printOutputs.ToString(),
                    isFinalAnswer: isFinalAnswer
                );
            }
            catch (Exception ex)
            {
                return new PythonExecutionResult(
                    output: null,
                    logs: _printOutputs.ToString(),
                    isFinalAnswer: false,
                    error: ex
                );
            }
        }

        /// <summary>
        /// Validates that all imports in the code are authorized
        /// </summary>
        /// <param name="code">Code to validate</param>
        private void ValidateImports(string code)
        {
            List<string> imports = ExtractImports(code);
            foreach (string import in imports)
            {
                if (!IsImportAuthorized(import))
                {
                    throw new UnauthorizedAccessException($"Import of '{import}' is not allowed. Authorized imports: {string.Join(", ", _authorizedImports)}");
                }
            }
        }

        /// <summary>
        /// Extracts import statements from Python code
        /// </summary>
        /// <param name="code">Python code</param>
        /// <returns>List of imported modules</returns>
        private List<string> ExtractImports(string code)
        {
            List<string> imports = new List<string>();

            // Match "import xxx" and "from xxx import yyy"
            string importPattern = @"^\s*(?:import\s+(\S+)|from\s+(\S+)\s+import)";
            MatchCollection matches = Regex.Matches(code, importPattern, RegexOptions.Multiline);

            foreach (Match match in matches)
            {
                string importName = match.Groups[1].Value;
                if (string.IsNullOrEmpty(importName))
                    importName = match.Groups[2].Value;

                if (!string.IsNullOrEmpty(importName))
                {
                    imports.Add(importName.Split('.')[0]); // Get base module name
                }
            }

            return imports.Distinct().ToList();
        }

        /// <summary>
        /// Executes Python code (placeholder - would use Python.NET or subprocess)
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <returns>Execution result</returns>
        private object? ExecutePythonCode(string code)
        {
            // This is a placeholder implementation
            // In a real implementation, you would use:
            // 1. Python.NET (pythonnet)
            // 2. IronPython
            // 3. Subprocess with python.exe
            // 4. Python C API

            throw new NotImplementedException(
                "Python execution not implemented. This requires integration with a Python runtime like Python.NET");
        }

        /// <summary>
        /// Sends variables to the execution environment
        /// </summary>
        /// <param name="variables">Variables to send</param>
        public override void SendVariables(Dictionary<string, object> variables)
        {
            foreach (KeyValuePair<string, object> kvp in variables)
            {
                _state[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Sends tools to the execution environment
        /// </summary>
        /// <param name="tools">Tools to send</param>
        public override void SendTools(Dictionary<string, BaseTool> tools)
        {
            _tools.Clear();
            foreach (KeyValuePair<string, BaseTool> kvp in tools)
            {
                _tools[kvp.Key] = kvp.Value;
                _state[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Resets the execution environment
        /// </summary>
        public override void Reset()
        {
            _state.Clear();
            _printOutputs.Clear();
            _tools.Clear();
            InitializeState();
        }
    }

    // ===============================
    // DOCKER EXECUTOR
    // ===============================

    /// <summary>
    /// Docker-based Python executor that runs code in a Docker container
    /// </summary>
    public class DockerExecutor : PythonExecutor
    {
        private readonly List<string> _authorizedImports;
        private readonly AgentLogger _logger;
        private readonly string _imageName;
        private readonly Dictionary<string, object> _dockerKwargs;
        private string? _containerId;
        private readonly Dictionary<string, object> _state = new();

        /// <summary>
        /// Gets the current execution state
        /// </summary>
        public override Dictionary<string, object> State => new(_state);

        /// <summary>
        /// Gets the authorized imports
        /// </summary>
        public override List<string> AuthorizedImports => new(_authorizedImports);

        /// <summary>
        /// Initializes a new instance of the DockerExecutor class
        /// </summary>
        /// <param name="authorizedImports">List of authorized imports</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="imageName">Docker image name</param>
        /// <param name="dockerKwargs">Additional Docker arguments</param>
        public DockerExecutor(List<string> authorizedImports, AgentLogger logger,
                             string imageName = "python:3.11-slim", Dictionary<string, object>? dockerKwargs = null)
        {
            _authorizedImports = authorizedImports;
            _logger = logger;
            _imageName = imageName;
            _dockerKwargs = dockerKwargs ?? new Dictionary<string, object>();

            InitializeContainer();
        }

        /// <summary>
        /// Initializes the Docker container
        /// </summary>
        private void InitializeContainer()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"run -d -i --rm {_imageName} python -u",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using Process? process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException("Failed to start Docker process");

                process.WaitForExit(30000); // 30 second timeout

                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException($"Docker container failed to start: {error}");
                }

                _containerId = process.StandardOutput.ReadToEnd().Trim();
                _logger.Log($"Docker container started: {_containerId}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Docker container: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executes Python code in the Docker container
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <returns>Execution result</returns>
        public override PythonExecutionResult Execute(string code)
        {
            if (string.IsNullOrEmpty(_containerId))
                throw new InvalidOperationException("Docker container not initialized");

            try
            {
                // Prepare the Python code with proper encoding
                string encodedCode = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
                string pythonCommand = $"import base64; exec(base64.b64decode('{encodedCode}').decode('utf-8'))";

                // Execute in Docker container
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"exec -i {_containerId} python -c \"{pythonCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using Process? process = Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException("Failed to start Docker exec process");

                process.WaitForExit(60000); // 60 second timeout

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                {
                    return new PythonExecutionResult(
                        output: null,
                        logs: error,
                        isFinalAnswer: false,
                        error: new Exception($"Docker execution failed: {error}")
                    );
                }

                // Parse output to determine if it's a final answer
                bool isFinalAnswer = output.Contains("FINAL_ANSWER:");
                string finalOutput = isFinalAnswer ?
                    output.Substring(output.IndexOf("FINAL_ANSWER:") + 13).Trim() :
                    output;

                return new PythonExecutionResult(
                    output: finalOutput,
                    logs: output,
                    isFinalAnswer: isFinalAnswer
                );
            }
            catch (Exception ex)
            {
                return new PythonExecutionResult(
                    output: null,
                    logs: "",
                    isFinalAnswer: false,
                    error: ex
                );
            }
        }

        /// <summary>
        /// Sends variables to the Docker container
        /// </summary>
        /// <param name="variables">Variables to send</param>
        public override void SendVariables(Dictionary<string, object> variables)
        {
            foreach (KeyValuePair<string, object> kvp in variables)
            {
                _state[kvp.Key] = kvp.Value;
            }

            // Send variables to container via pickle or JSON
            string serializedVars = JsonSerializer.Serialize(variables);
            string code = $"import json; globals().update(json.loads('{serializedVars}'))";
            Execute(code);
        }

        /// <summary>
        /// Sends tools to the Docker container
        /// </summary>
        /// <param name="tools">Tools to send</param>
        public override void SendTools(Dictionary<string, BaseTool> tools)
        {
            // Tools would need to be serialized and sent to the container
            // This is a simplified implementation
            foreach (KeyValuePair<string, BaseTool> kvp in tools)
            {
                _state[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Resets the execution environment
        /// </summary>
        public override void Reset()
        {
            _state.Clear();
            // Restart the container or clear its state
            Execute("globals().clear()");
        }

        /// <summary>
        /// Cleans up the Docker container
        /// </summary>
        public override void Cleanup()
        {
            if (!string.IsNullOrEmpty(_containerId))
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = $"stop {_containerId}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using Process? process = Process.Start(startInfo);
                    process?.WaitForExit(10000);

                    _logger.Log($"Docker container stopped: {_containerId}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to stop Docker container: {ex.Message}");
                }
                finally
                {
                    _containerId = null;
                }
            }
        }
    }

    // ===============================
    // E2B EXECUTOR
    // ===============================

    /// <summary>
    /// E2B-based Python executor that runs code using E2B service
    /// </summary>
    public class E2BExecutor : PythonExecutor
    {
        private readonly List<string> _authorizedImports;
        private readonly AgentLogger _logger;
        private readonly string _apiKey;
        private readonly string _templateId;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, object> _state = new();
        private string? _sessionId;

        /// <summary>
        /// Gets the current execution state
        /// </summary>
        public override Dictionary<string, object> State => new(_state);

        /// <summary>
        /// Gets the authorized imports
        /// </summary>
        public override List<string> AuthorizedImports => new(_authorizedImports);

        /// <summary>
        /// Initializes a new instance of the E2BExecutor class
        /// </summary>
        /// <param name="authorizedImports">List of authorized imports</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="apiKey">E2B API key</param>
        /// <param name="templateId">E2B template ID</param>
        public E2BExecutor(List<string> authorizedImports, AgentLogger logger,
                          string? apiKey = null, string templateId = "python")
        {
            _authorizedImports = authorizedImports;
            _logger = logger;
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("E2B_API_KEY") ??
                     throw new ArgumentException("E2B API key not provided");
            _templateId = templateId;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            InitializeSession();
        }

        /// <summary>
        /// Initializes an E2B session
        /// </summary>
        private async void InitializeSession()
        {
            try
            {
                var request = new
                {
                    templateId = _templateId
                };

                StringContent content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync("https://api.e2b.dev/sessions", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"Failed to create E2B session: {error}");
                }

                string result = await response.Content.ReadAsStringAsync();
                Dictionary<string, object>? sessionData = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
                _sessionId = sessionData?["sessionId"]?.ToString();

                _logger.Log($"E2B session created: {_sessionId}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize E2B session: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executes Python code using E2B
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <returns>Execution result</returns>
        public override PythonExecutionResult Execute(string code)
        {
            return ExecuteAsync(code).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Executes Python code using E2B (async)
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Execution result</returns>
        public override async Task<PythonExecutionResult> ExecuteAsync(string code, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_sessionId))
                throw new InvalidOperationException("E2B session not initialized");

            try
            {
                var request = new
                {
                    code = code,
                    language = "python"
                };

                StringContent content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"https://api.e2b.dev/sessions/{_sessionId}/exec", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new PythonExecutionResult(
                        output: null,
                        logs: "",
                        isFinalAnswer: false,
                        error: new Exception($"E2B execution failed: {error}")
                    );
                }

                string result = await response.Content.ReadAsStringAsync(cancellationToken);
                Dictionary<string, object>? executionResult = JsonSerializer.Deserialize<Dictionary<string, object>>(result);

                string output = executionResult?.GetValueOrDefault("stdout")?.ToString() ?? "";
                string errorOutput = executionResult?.GetValueOrDefault("stderr")?.ToString() ?? "";
                string exitCode = executionResult?.GetValueOrDefault("exitCode")?.ToString() ?? "0";

                if (exitCode != "0" && !string.IsNullOrEmpty(errorOutput))
                {
                    return new PythonExecutionResult(
                        output: null,
                        logs: errorOutput,
                        isFinalAnswer: false,
                        error: new Exception($"E2B execution error: {errorOutput}")
                    );
                }

                bool isFinalAnswer = output.Contains("FINAL_ANSWER:");
                string finalOutput = isFinalAnswer ?
                    output.Substring(output.IndexOf("FINAL_ANSWER:") + 13).Trim() :
                    output;

                return new PythonExecutionResult(
                    output: finalOutput,
                    logs: output,
                    isFinalAnswer: isFinalAnswer
                );
            }
            catch (Exception ex)
            {
                return new PythonExecutionResult(
                    output: null,
                    logs: "",
                    isFinalAnswer: false,
                    error: ex
                );
            }
        }

        /// <summary>
        /// Sends variables to the E2B session
        /// </summary>
        /// <param name="variables">Variables to send</param>
        public override void SendVariables(Dictionary<string, object> variables)
        {
            foreach (KeyValuePair<string, object> kvp in variables)
            {
                _state[kvp.Key] = kvp.Value;
            }

            // Send variables via code execution
            string serializedVars = JsonSerializer.Serialize(variables);
            string code = $"import json; globals().update(json.loads('{serializedVars}'))";
            Execute(code);
        }

        /// <summary>
        /// Sends tools to the E2B session
        /// </summary>
        /// <param name="tools">Tools to send</param>
        public override void SendTools(Dictionary<string, BaseTool> tools)
        {
            foreach (KeyValuePair<string, BaseTool> kvp in tools)
            {
                _state[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Resets the execution environment
        /// </summary>
        public override void Reset()
        {
            _state.Clear();
            Execute("globals().clear()");
        }

        /// <summary>
        /// Cleans up the E2B session
        /// </summary>
        public override void Cleanup()
        {
            if (!string.IsNullOrEmpty(_sessionId))
            {
                try
                {
                    HttpResponseMessage response = _httpClient.DeleteAsync($"https://api.e2b.dev/sessions/{_sessionId}").GetAwaiter().GetResult();
                    _logger.Log($"E2B session deleted: {_sessionId}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to delete E2B session: {ex.Message}");
                }
                finally
                {
                    _sessionId = null;
                }
            }

            _httpClient.Dispose();
        }
    }

    // ===============================
    // WASM EXECUTOR
    // ===============================

    /// <summary>
    /// WebAssembly-based Python executor using Pyodide
    /// </summary>
    public class WasmExecutor : PythonExecutor
    {
        private readonly List<string> _authorizedImports;
        private readonly AgentLogger _logger;
        private readonly Dictionary<string, object> _state = new();
        private bool _isInitialized;

        /// <summary>
        /// Gets the current execution state
        /// </summary>
        public override Dictionary<string, object> State => new(_state);

        /// <summary>
        /// Gets the authorized imports
        /// </summary>
        public override List<string> AuthorizedImports => new(_authorizedImports);

        /// <summary>
        /// Initializes a new instance of the WasmExecutor class
        /// </summary>
        /// <param name="authorizedImports">List of authorized imports</param>
        /// <param name="logger">Logger instance</param>
        public WasmExecutor(List<string> authorizedImports, AgentLogger logger)
        {
            _authorizedImports = authorizedImports;
            _logger = logger;
            InitializePyodide();
        }

        /// <summary>
        /// Initializes Pyodide WebAssembly Python runtime
        /// </summary>
        private void InitializePyodide()
        {
            try
            {
                // This would initialize Pyodide in a WebAssembly context
                // For now, this is a placeholder implementation
                _logger.Log("Initializing Pyodide WebAssembly runtime", LogLevel.Debug);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Pyodide: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executes Python code in Pyodide
        /// </summary>
        /// <param name="code">Python code to execute</param>
        /// <returns>Execution result</returns>
        public override PythonExecutionResult Execute(string code)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Pyodide not initialized");

            try
            {
                // This would use Pyodide JavaScript API to execute Python code
                // Placeholder implementation
                object? result = ExecutePyodideCode(code);

                bool isFinalAnswer = result?.ToString()?.Contains("FINAL_ANSWER:") == true;
                object? output = isFinalAnswer ?
                    ExtractFinalAnswer(result?.ToString() ?? "") :
                    result;

                return new PythonExecutionResult(
                    output: output,
                    logs: "", // Would capture print outputs from Pyodide
                    isFinalAnswer: isFinalAnswer
                );
            }
            catch (Exception ex)
            {
                return new PythonExecutionResult(
                    output: null,
                    logs: "",
                    isFinalAnswer: false,
                    error: ex
                );
            }
        }

        /// <summary>
        /// Executes code in Pyodide (placeholder implementation)
        /// </summary>
        /// <param name="code">Python code</param>
        /// <returns>Execution result</returns>
        private object? ExecutePyodideCode(string code)
        {
            // This would interface with Pyodide JavaScript API
            // Requires browser environment or Node.js with Pyodide
            throw new NotImplementedException(
                "Pyodide execution not implemented. This requires integration with Pyodide WebAssembly runtime.");
        }

        /// <summary>
        /// Extracts final answer from output
        /// </summary>
        /// <param name="output">Output string</param>
        /// <returns>Final answer</returns>
        private string ExtractFinalAnswer(string output)
        {
            int index = output.IndexOf("FINAL_ANSWER:");
            return index >= 0 ? output.Substring(index + 13).Trim() : output;
        }

        /// <summary>
        /// Sends variables to Pyodide
        /// </summary>
        /// <param name="variables">Variables to send</param>
        public override void SendVariables(Dictionary<string, object> variables)
        {
            foreach (KeyValuePair<string, object> kvp in variables)
            {
                _state[kvp.Key] = kvp.Value;
            }

            // Would send variables to Pyodide global namespace
        }

        /// <summary>
        /// Sends tools to Pyodide
        /// </summary>
        /// <param name="tools">Tools to send</param>
        public override void SendTools(Dictionary<string, BaseTool> tools)
        {
            foreach (KeyValuePair<string, BaseTool> kvp in tools)
            {
                _state[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Resets the execution environment
        /// </summary>
        public override void Reset()
        {
            _state.Clear();
            // Would reset Pyodide global namespace
        }

        /// <summary>
        /// Cleans up Pyodide resources
        /// </summary>
        public override void Cleanup()
        {
            _isInitialized = false;
            _state.Clear();
        }
    }
}