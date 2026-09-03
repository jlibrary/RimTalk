using System.Threading.Tasks;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;

namespace RimTalk.Client;

/// <summary>
/// Factory for creating AI client instances with support for async initialization
/// Handles Player2 local app detection and fallback mechanisms
/// </summary>
public static class AIClientFactory
{
    private static IAIClient _instance;
    private static AIProvider _currentProvider;
    private static string _currentApiKey;
    private static string _currentModel;
    private static string _currentBaseUrl;

    /// <summary>
    /// Async method for getting AI client - required for Player2 local detection
    /// </summary>
    public static async Task<IAIClient> GetAIClientAsync()
    {
        var config = Settings.Get().GetActiveConfig();
        if (config == null)
        {
            return null;
        }

        var effectiveModel = config.GetEffectiveModelName();
        if (_instance == null || _currentProvider != config.Provider || _currentApiKey != config.ApiKey 
            || _currentModel != effectiveModel || _currentBaseUrl != config.BaseUrl)
        {
            _instance = await CreateServiceInstanceAsync(config);
            _currentProvider = config.Provider;
            _currentApiKey = config.ApiKey;
            _currentModel = effectiveModel;
            _currentBaseUrl = config.BaseUrl;
        }
        else if (_instance is Player2Client p2)
        {
            p2.SetFallbackApiKey(config.ApiKey);
        }

        return _instance;
    }

    /// <summary>
    /// Creates appropriate AI client instance based on provider configuration
    /// Player2 uses async factory method for local app detection
    /// </summary>
    private static async Task<IAIClient> CreateServiceInstanceAsync(ApiConfig config)
    {
        var model = config.GetEffectiveModelName();

        // 1. Handle Special/Dynamic cases
        switch (config.Provider)
        {
            case AIProvider.Player2: return await Player2Client.CreateAsync(config.ApiKey, config.CustomRequestJson);
            case AIProvider.Local:   return new OpenAIClient(config.BaseUrl, config.CustomModelName, customRequestJson: config.CustomRequestJson);
            case AIProvider.Custom:  return new OpenAIClient(config.BaseUrl, config.CustomModelName, config.ApiKey, customRequestJson: config.CustomRequestJson);
        }

        // 2. Handle Standard Clients via Registry
        if (AIProviderRegistry.Defs.TryGetValue(config.Provider, out var def))
        {
            return new OpenAIClient(def.EndpointUrl, model, config.ApiKey, def.ExtraHeaders, customRequestJson: config.CustomRequestJson);
        }

        return null;
    }

    /// <summary>
    /// Clean up resources and stop background processes
    /// </summary>
    public static void Clear()
    {
        if (_currentProvider == AIProvider.Player2)
        {
            Player2Client.StopHealthCheck();
        }
        _instance = null;
        _currentProvider = AIProvider.None;
        _currentApiKey = null;
        _currentModel = null;
        _currentBaseUrl = null;
    }
}