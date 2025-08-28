using System.Text.Json;

namespace NETAgents.Models
{
    public record TaskStep : MemoryStep
    {
        public string Task { get; init; }
        public List<object>? TaskImages { get; init; }

        public TaskStep(string task, List<object>? taskImages = null, Timing? timing = null) : base(timing)
        {
            Task = task;
            TaskImages = taskImages;
        }

        public override List<LLMMessage> ToMessages(bool summaryMode = false)
        {
            List<Dictionary<string, object>> content = new List<Dictionary<string, object>>
            {
                new() { ["type"] = "text", ["text"] = Task }
            };

            if (TaskImages != null)
            {
                foreach (var image in TaskImages)
                {
                    content.Add(new Dictionary<string, object> { ["type"] = "image", ["image"] = image });
                }
            }

            return new List<LLMMessage>
            {
                new LLMMessage(MessageRole.User, content, JsonSerializer.Serialize(content))
            };
        }
    }
}