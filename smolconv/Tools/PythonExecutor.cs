namespace SmolConv.Tools
{
    // ===============================
    // PYTHON EXECUTOR ABSTRACTION
    // ===============================

    /// <summary>
    /// Abstract base class for Python code executors
    /// </summary>
    public abstract class PythonExecutor
    {
        /// <summary>
        /// Gets the current execution state/variables
        /// </summary>
        public abstract Dictionary<string, object> State { get; }

        /// <summary>
        /// Gets the list of authorized imports
        /// </summary>
        public abstract List<string> AuthorizedImports { get; }

        /// <summary>
        /// Executes Python code and returns the result
        /// </summary>
        /// <param name="code">The Python code to execute</param>
        /// <returns>The execution result</returns>
        public abstract PythonExecutionResult Execute(string code);

        /// <summary>
        /// Executes Python code and returns the result (async version)
        /// </summary>
        /// <param name="code">The Python code to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The execution result</returns>
        public virtual Task<PythonExecutionResult> ExecuteAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Execute(code));
        }

        /// <summary>
        /// Sends variables to the execution environment
        /// </summary>
        /// <param name="variables">Variables to send</param>
        public abstract void SendVariables(Dictionary<string, object> variables);

        /// <summary>
        /// Sends tools to the execution environment
        /// </summary>
        /// <param name="tools">Tools to send</param>
        public abstract void SendTools(Dictionary<string, BaseTool> tools);

        /// <summary>
        /// Resets the execution environment
        /// </summary>
        public abstract void Reset();

        /// <summary>
        /// Cleans up resources used by the executor
        /// </summary>
        public virtual void Cleanup()
        {
            // Default implementation does nothing
        }

        /// <summary>
        /// Disposes the executor and cleans up resources
        /// </summary>
        public virtual void Dispose()
        {
            Cleanup();
        }

        /// <summary>
        /// Validates that an import is authorized
        /// </summary>
        /// <param name="importName">The import name to validate</param>
        /// <returns>True if the import is authorized, false otherwise</returns>
        protected virtual bool IsImportAuthorized(string importName)
        {
            return AuthorizedImports.Contains("*") || AuthorizedImports.Contains(importName);
        }
    }
}