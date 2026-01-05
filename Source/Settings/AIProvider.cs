using System.Collections.Generic;

namespace RimTalk;

public enum AIProvider
{
    Google,
    OpenAI,
    DeepSeek,
    Grok,
    GLM,
    AlibabaIntl,
    AlibabaCN,
    OpenRouter,
    Player2,
    Local,
    Custom,
    None
}

public struct ProviderDef
{
    public string Label;
    public string EndpointUrl;
    public string ListModelsUrl;
    public Dictionary<string, string> ExtraHeaders;
}

public static class AIProviderRegistry
{
    public static readonly Dictionary<AIProvider, ProviderDef> Defs = new()
    {
        {
            AIProvider.Google, new ProviderDef
            {
                EndpointUrl = "https://generativelanguage.googleapis.com/v1beta",
                ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models"
            }
        },
        {
            AIProvider.OpenAI, new ProviderDef
            {
                EndpointUrl = "https://api.openai.com/v1/chat/completions",
                ListModelsUrl = "https://api.openai.com/v1/models"
            }
        },
        {
            AIProvider.DeepSeek, new ProviderDef
            {
                EndpointUrl = "https://api.deepseek.com/v1/chat/completions",
                ListModelsUrl = "https://api.deepseek.com/models"
            }
        },
        {
            AIProvider.Grok, new ProviderDef
            {
                EndpointUrl = "https://api.x.ai/v1/chat/completions",
                ListModelsUrl = "https://api.x.ai/v1/models"
            }
        },
        {
            AIProvider.GLM, new ProviderDef
            {
                EndpointUrl = "https://api.z.ai/api/paas/v4/chat/completions",
                ListModelsUrl = "https://api.z.ai/api/paas/v4/models"
            }
        },
        {
            AIProvider.OpenRouter, new ProviderDef
            {
                EndpointUrl = "https://openrouter.ai/api/v1/chat/completions",
                ListModelsUrl = "https://openrouter.ai/api/v1/models",
                ExtraHeaders = new Dictionary<string, string>
                {
                    { "HTTP-Referer", "https://github.com/jlibrary/RimTalk" },
                    { "X-Title", "RimTalk" }
                }
            }
        },
        {
            AIProvider.AlibabaIntl, new ProviderDef
            {
                Label = "Alibaba (Intl)",
                EndpointUrl = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions",
                ListModelsUrl = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/models"
            }
        },
        {
            AIProvider.AlibabaCN, new ProviderDef
            {
                Label = "Alibaba (CN)",
                EndpointUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
                ListModelsUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/models"
            }
        },
        {
            AIProvider.Player2, new ProviderDef
            {
                EndpointUrl = "https://api.player2.game"
            }
        }
    };
    
    public static string GetLabel(this AIProvider p)
    {
        if (Defs.TryGetValue(p, out var def) && !string.IsNullOrEmpty(def.Label))
        {
            return def.Label;
        }
        return p.ToString();
    }

    public static string GetEndpointUrl(this AIProvider p)
    {
        return Defs.TryGetValue(p, out var def) ? def.EndpointUrl : null;
    }

    public static string GetListModelsUrl(this AIProvider p)
    {
        return Defs.TryGetValue(p, out var def) ? def.ListModelsUrl : null;
    }
}