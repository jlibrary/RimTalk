namespace RimTalk.AI.OpenAI
{
    public class OpenRouterClient : OpenAICompatibleClient
    {
        protected override string BaseUrl => "https://openrouter.ai/api";
    }
}
