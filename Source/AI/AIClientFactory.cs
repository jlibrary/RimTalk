using RimTalk.AI.Gemini;
using RimTalk.AI.Local;
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
                _instance = CreateServiceInstance(config.Provider);
                _currentProvider = config.Provider;
            }

            return _instance;
        }

        private static IAIClient CreateServiceInstance(AIProvider provider)
        {
            if (provider == AIProvider.Google)
                return new GeminiClient();
            if (provider == AIProvider.OpenAI)
                return new OpenAIClient();
            if (provider == AIProvider.DeepSeek)
                return new DeepSeekClient();
            if (provider == AIProvider.OpenRouter)
                return new OpenRouterClient();
            if (provider == AIProvider.Local)
                return new LocalClient();
            return null;
        }

        public static void Clear()
        {
            _instance = null;
            _currentProvider = AIProvider.None;
        }
    }
}