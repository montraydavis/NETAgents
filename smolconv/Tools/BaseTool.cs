namespace SmolConv.Tools
{
    // ===============================
    // BASE TOOL ABSTRACTION
    // ===============================

    /// <summary>
    /// Abstract base class for all tools used by agents
    /// </summary>
    public abstract class BaseTool
    {
        /// <summary>
        /// Gets or sets the name of the tool
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Executes the tool with the provided arguments
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <param name="sanitizeInputsOutputs">Whether to sanitize inputs and outputs</param>
        /// <returns>The result of the tool execution</returns>
        public abstract object? Call(object[]? args = null, Dictionary<string, object>? kwargs = null, bool sanitizeInputsOutputs = false);

        /// <summary>
        /// Executes the tool with the provided arguments (async version)
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The result of the tool execution</returns>
        public virtual Task<object?> CallAsync(object[]? args = null, Dictionary<string, object>? kwargs = null,
                                              CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Call(args, kwargs));
        }

        /// <summary>
        /// Executes the tool with the provided arguments (async version with sanitization)
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <param name="sanitizeInputsOutputs">Whether to sanitize inputs and outputs</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The result of the tool execution</returns>
        public virtual Task<object?> CallAsync(object[]? args = null, Dictionary<string, object>? kwargs = null,
                                              bool sanitizeInputsOutputs = false,
                                              CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Call(args, kwargs, sanitizeInputsOutputs));
        }
    }
}