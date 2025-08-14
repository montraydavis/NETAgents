namespace SmolConv.Tools
{
    // ===============================
    // PYTHON EXECUTION RESULT
    // ===============================

    /// <summary>
    /// Represents the result of Python code execution
    /// </summary>
    public record PythonExecutionResult
    {
        /// <summary>
        /// Gets the output from the executed code
        /// </summary>
        public object? Output { get; init; }

        /// <summary>
        /// Gets the execution logs
        /// </summary>
        public string Logs { get; init; } = string.Empty;

        /// <summary>
        /// Gets whether this execution result represents a final answer
        /// </summary>
        public bool IsFinalAnswer { get; init; }

        /// <summary>
        /// Gets any error that occurred during execution
        /// </summary>
        public Exception? Error { get; init; }

        /// <summary>
        /// Gets whether the execution was successful
        /// </summary>
        public bool IsSuccess => Error == null;

        public PythonExecutionResult(object? output = null, string logs = "", bool isFinalAnswer = false, Exception? error = null)
        {
            Output = output;
            Logs = logs;
            IsFinalAnswer = isFinalAnswer;
            Error = error;
        }
    }
}