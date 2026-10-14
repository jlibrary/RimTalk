using System.Collections.Generic;

namespace RimTalk;

public enum AIProvider
{
    Google,
    OpenAI,
    DeepSeek,
    Grok,
    GLM,
    GLMCoding,
    AlibabaIntl,
    AlibabaCN,
    OpenRouter,
    Player2,
    Local,
    Custom,
    None,
    Claude,
    Moonshot,
    OpenCodeZen,
    OpenCodeGo
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
                EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/openai/models"
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
            AIProvider.GLMCoding, new ProviderDef
            {
                Label = "GLM (Coding)",
                EndpointUrl = "https://api.z.ai/api/coding/paas/v4/chat/completions",
                ListModelsUrl = "https://api.z.ai/api/coding/paas/v4/models"
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
        },
        {
            AIProvider.Claude, new ProviderDef
            {
                EndpointUrl = "https://api.anthropic.com/v1/chat/completions",
                ListModelsUrl = "https://api.anthropic.com/v1/models",
                ExtraHeaders = new Dictionary<string, string>
                {
                    { "anthropic-version", "2023-06-01" }
                }
            }
        },
        {
            AIProvider.Moonshot, new ProviderDef
            {
                EndpointUrl = "https://api.moonshot.ai/v1/chat/completions",
                ListModelsUrl = "https://api.moonshot.ai/v1/models"
            }
        },
        {
            AIProvider.OpenCodeZen, new ProviderDef
            {
                Label = "OpenCode Zen",
                EndpointUrl = "https://opencode.ai/zen/v1/chat/completions",
                ListModelsUrl = "https://opencode.ai/zen/v1/models"
            }
        },
        {
            AIProvider.OpenCodeGo, new ProviderDef
            {
                Label = "OpenCode GO",
                EndpointUrl = "https://opencode.ai/zen/go/v1/chat/completions",
                ListModelsUrl = "https://opencode.ai/zen/go/v1/models"
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

    public static Dictionary<string, string> GetExtraHeaders(this AIProvider p)
    {
        // Value comes from settings, so it can't live in the static Defs table.
        if (p == AIProvider.OpenCodeZen || p == AIProvider.OpenCodeGo)
            return new Dictionary<string, string> { { "x-opencode-session", Settings.Get().OpenCodeSessionId } };
        return Defs.TryGetValue(p, out var def) ? def.ExtraHeaders : null;
    }
}
