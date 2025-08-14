using SmolConv.Models;


namespace SmolConv.Core
{
    // ===============================
    // MEMORY & COLLECTIONS
    // ===============================

    /// <summary>
    /// Registry for managing callbacks for different memory step types
    /// </summary>
    public class CallbackRegistry
    {
        private readonly Dictionary<Type, List<Action<MemoryStep, object>>> _callbacks = new();
        private readonly object _lockObject = new();

        /// <summary>
        /// Registers a callback for a specific memory step type
        /// </summary>
        /// <typeparam name="T">Memory step type</typeparam>
        /// <param name="callback">Callback to register</param>
        public void Register<T>(Action<T, object> callback) where T : MemoryStep
        {
            lock (_lockObject)
            {
                var stepType = typeof(T);
                if (!_callbacks.ContainsKey(stepType))
                {
                    _callbacks[stepType] = new List<Action<MemoryStep, object>>();
                }
                
                _callbacks[stepType].Add((step, agent) => callback((T)step, agent));
            }
        }

        /// <summary>
        /// Executes all callbacks for a memory step
        /// </summary>
        /// <param name="step">Memory step</param>
        /// <param name="agent">Agent instance</param>
        public void ExecuteCallbacks(MemoryStep step, object agent)
        {
            lock (_lockObject)
            {
                var stepType = step.GetType();
                if (_callbacks.TryGetValue(stepType, out var callbacks))
                {
                    foreach (var callback in callbacks)
                    {
                        try
                        {
                            callback(step, agent);
                        }
                        catch (Exception ex)
                        {
                            // Log callback errors but don't fail the agent
                            Console.WriteLine($"Callback error: {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clears all callbacks
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _callbacks.Clear();
            }
        }
    }
}