namespace RimTalk.AI.OpenAI
{
    public class DeepSeekClient : OpenAICompatibleClient
    {
        protected override string BaseUrl => "https://api.deepseek.com";
    }
}
