namespace SmolConv.Core
{
    /// <summary>
    /// Tool for providing final answers
    /// </summary>
    public class FinalAnswerTool : Tool
    {
        /// <summary>
        /// Gets the tool name
        /// </summary>
        public override string Name => "final_answer";

        /// <summary>
        /// Gets the tool description
        /// </summary>
        public override string Description => "Provides the final answer to the user's question or task.";

        /// <summary>
        /// Gets the input specifications
        /// </summary>
        public override Dictionary<string, Dictionary<string, object>> Inputs => new()
        {
            ["answer"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "The final answer to provide to the user"
            }
        };

        /// <summary>
        /// Gets the output type
        /// </summary>
        public override string OutputType => "string";

        /// <summary>
        /// Executes the final answer tool
        /// </summary>
        /// <param name="args">Positional arguments</param>
        /// <param name="kwargs">Named arguments</param>
        /// <returns>The final answer</returns>
        protected override object? Forward(object?[]? args, Dictionary<string, object>? kwargs)
        {
            if (kwargs?.TryGetValue("answer", out object? answer) == true)
            {
                return answer;
            }

            if (args?.Length > 0)
            {
                return args[0];
            }

            throw new ArgumentException("No answer provided to final_answer tool");
        }
    }
}