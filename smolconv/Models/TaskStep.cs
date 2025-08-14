using System.Text.Json;

namespace SmolConv.Models
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

        public override List<ChatMessage> ToMessages(bool summaryMode = false)
        {
            var content = new List<Dictionary<string, object>>
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

            return new List<ChatMessage>
            {
                new ChatMessage(MessageRole.User, content, JsonSerializer.Serialize(content))
            };
        }
    }
}