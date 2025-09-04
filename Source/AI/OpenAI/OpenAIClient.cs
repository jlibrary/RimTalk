namespace RimTalk.AI.OpenAI
{
    public class OpenAIClient : OpenAICompatibleClient
    {
        protected override string BaseUrl => "https://api.openai.com";
    }
}
