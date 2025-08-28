namespace NETAgents.Exceptions
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    // ===============================
    // LOGGER INTERFACE (for dependency injection)
    // ===============================

    /// <summary>
    /// Interface for logging agent errors
    /// </summary>
    public interface IAgentLogger
    {
        void LogError(string message);
    }

    // ===============================
    // EXCEPTION CLASSES
    // ===============================

    /// <summary>
    /// Base class for other agent-related exceptions
    /// </summary>
    public class AgentError : Exception
    {
        /// <summary>
        /// Gets the error message
        /// </summary>
        public new string Message { get; }

        /// <summary>
        /// Initializes a new instance of the AgentError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentError(string message, IAgentLogger logger) : base(message)
        {
            Message = message;
            logger?.LogError(message);
        }

        /// <summary>
        /// Initializes a new instance of the AgentError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentError(string message, IAgentLogger logger, Exception innerException)
            : base(message, innerException)
        {
            Message = message;
            logger?.LogError(message);
        }

        /// <summary>
        /// Converts the exception to a dictionary representation
        /// </summary>
        /// <returns>Dictionary containing exception type and message</returns>
        public virtual Dictionary<string, string> ToDict()
        {
            return new Dictionary<string, string>
            {
                { "type", GetType().Name },
                { "message", Message }
            };
        }
    }

    /// <summary>
    /// Exception raised for errors in parsing in the agent
    /// </summary>
    public class AgentParsingError : AgentError
    {
        /// <summary>
        /// Initializes a new instance of the AgentParsingError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentParsingError(string message, IAgentLogger logger) : base(message, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AgentParsingError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentParsingError(string message, IAgentLogger logger, Exception innerException)
            : base(message, logger, innerException)
        {
        }
    }

    /// <summary>
    /// Exception raised for errors in execution in the agent
    /// </summary>
    public class AgentExecutionError : AgentError
    {
        /// <summary>
        /// Initializes a new instance of the AgentExecutionError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentExecutionError(string message, IAgentLogger logger) : base(message, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AgentExecutionError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentExecutionError(string message, IAgentLogger logger, Exception innerException)
            : base(message, logger, innerException)
        {
        }
    }

    /// <summary>
    /// Exception raised when agent reaches maximum steps
    /// </summary>
    public class AgentMaxStepsError : AgentError
    {
        /// <summary>
        /// Initializes a new instance of the AgentMaxStepsError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentMaxStepsError(string message, IAgentLogger logger) : base(message, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AgentMaxStepsError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentMaxStepsError(string message, IAgentLogger logger, Exception innerException)
            : base(message, logger, innerException)
        {
        }
    }

    /// <summary>
    /// Exception raised for errors when incorrect arguments are passed to the tool
    /// </summary>
    public class AgentToolCallError : AgentExecutionError
    {
        /// <summary>
        /// Initializes a new instance of the AgentToolCallError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentToolCallError(string message, IAgentLogger logger) : base(message, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AgentToolCallError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentToolCallError(string message, IAgentLogger logger, Exception innerException)
            : base(message, logger, innerException)
        {
        }
    }

    /// <summary>
    /// Exception raised for errors when executing a tool
    /// </summary>
    public class AgentToolExecutionError : AgentExecutionError
    {
        /// <summary>
        /// Gets the tool name
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// Gets the arguments that were passed to the tool
        /// </summary>
        public object? Arguments { get; }

        /// <summary>
        /// Gets whether this is a managed agent
        /// </summary>
        public bool IsManagedAgent { get; }

        /// <summary>
        /// Initializes a new instance of the AgentToolExecutionError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentToolExecutionError(string message, IAgentLogger logger) : base(message, logger)
        {
            ToolName = "";
            Arguments = null;
            IsManagedAgent = false;
        }

        /// <summary>
        /// Initializes a new instance of the AgentToolExecutionError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentToolExecutionError(string message, IAgentLogger logger, Exception innerException)
            : base(message, logger, innerException)
        {
            ToolName = "";
            Arguments = null;
            IsManagedAgent = false;
        }

        /// <summary>
        /// Initializes a new instance of the AgentToolExecutionError class with enhanced context
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="toolName">The name of the tool that failed</param>
        /// <param name="arguments">The arguments that were passed to the tool</param>
        /// <param name="isManagedAgent">Whether this is a managed agent</param>
        public AgentToolExecutionError(string message, IAgentLogger logger, string toolName = "", 
                                     object? arguments = null, bool isManagedAgent = false) 
            : base(message, logger)
        {
            ToolName = toolName;
            Arguments = arguments;
            IsManagedAgent = isManagedAgent;
        }

        /// <summary>
        /// Initializes a new instance of the AgentToolExecutionError class with enhanced context
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        /// <param name="toolName">The name of the tool that failed</param>
        /// <param name="arguments">The arguments that were passed to the tool</param>
        /// <param name="isManagedAgent">Whether this is a managed agent</param>
        public AgentToolExecutionError(string message, IAgentLogger logger, Exception innerException,
                                     string toolName = "", object? arguments = null, bool isManagedAgent = false)
            : base(message, logger, innerException)
        {
            ToolName = toolName;
            Arguments = arguments;
            IsManagedAgent = isManagedAgent;
        }
    }

    /// <summary>
    /// Exception raised for errors in generation in the agent
    /// </summary>
    public class AgentGenerationError : AgentError
    {
        /// <summary>
        /// Initializes a new instance of the AgentGenerationError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        public AgentGenerationError(string message, IAgentLogger logger) : base(message, logger)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AgentGenerationError class
        /// </summary>
        /// <param name="message">The error message</param>
        /// <param name="logger">The logger to log the error to</param>
        /// <param name="innerException">The inner exception</param>
        public AgentGenerationError(string message, IAgentLogger logger, Exception innerException)
            : base(message, logger, innerException)
        {
        }
    }

    // ===============================
    // RATE LIMITER UTILITY
    // ===============================

    /// <summary>
    /// Simple rate limiter that enforces a minimum delay between consecutive requests.
    /// 
    /// This class is useful for limiting the rate of operations such as API requests,
    /// by ensuring that calls to Throttle() are spaced out by at least a given interval
    /// based on the desired requests per minute.
    /// 
    /// If no rate is specified (i.e., requestsPerMinute is null), rate limiting
    /// is disabled and Throttle() becomes a no-op.
    /// </summary>
    public class RateLimiter
    {
        private readonly bool _enabled;
        private readonly double _intervalSeconds;
        private DateTime _lastCall = DateTime.MinValue;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Initializes a new instance of the RateLimiter class
        /// </summary>
        /// <param name="requestsPerMinute">Maximum number of allowed requests per minute. Use null to disable rate limiting.</param>
        public RateLimiter(double? requestsPerMinute = null)
        {
            _enabled = requestsPerMinute.HasValue;
            _intervalSeconds = _enabled ? 60.0 / requestsPerMinute!.Value : 0.0;
        }

        /// <summary>
        /// Pause execution to respect the rate limit, if enabled (synchronous version)
        /// </summary>
        public void Throttle()
        {
            if (!_enabled)
                return;

            lock (_lockObject)
            {
                DateTime now = DateTime.UtcNow;
                double elapsed = (now - _lastCall).TotalSeconds;

                if (elapsed < _intervalSeconds)
                {
                    int delayMs = (int)(((_intervalSeconds - elapsed) * 1000));
                    Thread.Sleep(delayMs);
                }

                _lastCall = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Pause execution to respect the rate limit, if enabled (asynchronous version)
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task that represents the throttling delay</returns>
        public async Task ThrottleAsync(CancellationToken cancellationToken = default)
        {
            if (!_enabled)
                return;

            TimeSpan delay;

            lock (_lockObject)
            {
                DateTime now = DateTime.UtcNow;
                double elapsed = (now - _lastCall).TotalSeconds;

                if (elapsed < _intervalSeconds)
                {
                    delay = TimeSpan.FromSeconds(_intervalSeconds - elapsed);
                }
                else
                {
                    delay = TimeSpan.Zero;
                }

                _lastCall = now.Add(delay);
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        /// <summary>
        /// Gets whether rate limiting is enabled
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Gets the interval between requests in seconds
        /// </summary>
        public double IntervalSeconds => _intervalSeconds;
    }
}