namespace SmolConv.Models
{
    public record FinalAnswerPromptTemplate
    {
        public string PreMessages { get; init; }
        public string PostMessages { get; init; }

        public FinalAnswerPromptTemplate(string preMessages, string postMessages)
        {
            PreMessages = preMessages;
            PostMessages = postMessages;
        }
    }
}