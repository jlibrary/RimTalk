namespace RimTalk.AI.OpenAI
{
    public class OpenAICustomClient : OpenAICompatibleClient
    {
        protected override string BaseUrl => Settings.Get().GetActiveConfig()?.BaseUrl;
    }
}
