using RimTalk.AI.Gemini;
using RimTalk.AI.OpenAI;

namespace RimTalk.Service
{
    public static class AIClientFactory
    {
        private static IAIClient _instance;
        private static AIProvider _currentProvider;

        public static IAIClient GetAIClient()
        {
            var config = Settings.Get().GetActiveConfig();
            if (config == null)
            {
                return null;
            }

            if (_instance == null || _currentProvider != config.Provider)
            {
                _instance = CreateServiceInstance(config);
                _currentProvider = config.Provider;
            }

            return _instance;
        }

        private static IAIClient CreateServiceInstance(ApiConfig config)
        {
            switch (config.Provider)
            {
                case AIProvider.Google:
                    return new GeminiClient();
                case AIProvider.OpenAI:
                    return new OpenAIClient("https://api.openai.com", config.SelectedModel, config.ApiKey);
                case AIProvider.DeepSeek:
                    return new OpenAIClient("https://api.deepseek.com", config.SelectedModel, config.ApiKey);
                case AIProvider.OpenRouter:
                    return new OpenAIClient("https://openrouter.ai/api", config.SelectedModel, config.ApiKey);
                case AIProvider.Local:
                    return new OpenAIClient(config.BaseUrl, config.CustomModelName);
                case AIProvider.Custom:
                    return new OpenAIClient(config.BaseUrl, config.CustomModelName, config.ApiKey);
                default:
                    return null;
            }
        }

        public static void Clear()
        {
            _instance = null;
            _currentProvider = AIProvider.None;
        }
    }
}