using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Service;
using RimTalk.Util;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.Client.OpenAI;

public class OpenAIClient(
    string baseUrl,
    string model,
    string apiKey = null,
    Dictionary<string, string> extraHeaders = null,
    string customRequestJson = null)
    : IAIClient
{
    private const string DefaultPath = "/v1/chat/completions";
    private readonly string _endpointUrl = FormatEndpointUrl(baseUrl);
    private readonly Random _random = new();
    private readonly AIProvider _provider = AIProvider.None;

    public OpenAIClient(
        string baseUrl,
        string model,
        string apiKey,
        Dictionary<string, string> extraHeaders,
        string customRequestJson,
        AIProvider provider) : this(baseUrl, model, apiKey, extraHeaders, customRequestJson)
    {
        _provider = provider;
    }

    private AIProvider GetEffectiveProvider()
    {
        if (_provider != AIProvider.None) return _provider;
        return Settings.Get()?.GetActiveConfig()?.Provider ?? AIProvider.None;
    }

    private static string FormatEndpointUrl(string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
        var trimmed = baseUrl.Trim().TrimEnd('/');
        var uri = new Uri(trimmed);
        // Append default path if only base domain or /v1 is provided
        if (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')))
            return trimmed + DefaultPath;
        if (uri.AbsolutePath.TrimEnd('/') == "/v1")
            return trimmed + "/chat/completions";
        return trimmed;
    }

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        Action<Payload> onRequestPrepared = null)
    {
        return await GetChatCompletionAsync(prefixMessages, messages, null, onRequestPrepared);
    }

    private static readonly string[] ThinkingLadder = ["disabled", "minimal", "low", "standard"];

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        string imageBase64,
        Action<Payload> onRequestPrepared = null)
    {
        return await ExecuteWithFallbackAsync(async reasoningLevel =>
        {
            string jsonContent = BuildRequestJson(prefixMessages, messages, stream: false, imageBase64: imageBase64, reasoningLevel: reasoningLevel);
            string effectiveModel = GetEffectiveModel(jsonContent);
            onRequestPrepared?.Invoke(new Payload(_endpointUrl, effectiveModel, jsonContent, null, 0));
            string responseText = await SendRequestAsync(jsonContent, new DownloadHandlerBuffer());

            var response = JsonUtil.DeserializeFromJson<OpenAIResponse>(responseText);
            return new Payload(_endpointUrl, effectiveModel, jsonContent, response?.Choices?[0]?.Message?.Content, response?.Usage?.TotalTokens ?? 0)
            {
                StatusCode = 200
            };
        });
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        Action<T> onResponseParsed,
        Action<Payload> onRequestPrepared = null) where T : class
    {
        return await GetStreamingChatCompletionAsync(prefixMessages, messages, null, onResponseParsed, onRequestPrepared);
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        string imageBase64,
        Action<T> onResponseParsed,
        Action<Payload> onRequestPrepared = null) where T : class
    {
        var parser = new JsonStreamParser<T>();
        return await StreamAsync(prefixMessages, messages, imageBase64, chunk =>
        {
            foreach (var response in parser.Parse(chunk))
                onResponseParsed?.Invoke(response);
        }, onRequestPrepared);
    }

    private async Task<Payload> StreamAsync(
        List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        string imageBase64,
        Action<string> onChunk,
        Action<Payload> onRequestPrepared)
    {
        return await ExecuteWithFallbackAsync(async reasoningLevel =>
        {
            string jsonContent = BuildRequestJson(prefixMessages, messages, stream: true, imageBase64: imageBase64, reasoningLevel: reasoningLevel);
            string effectiveModel = GetEffectiveModel(jsonContent);
            onRequestPrepared?.Invoke(new Payload(_endpointUrl, effectiveModel, jsonContent, null, 0));

            var streamHandler = new OpenAIStreamHandler(onChunk);
            await SendHttpAsync(jsonContent, streamHandler);

            return new Payload(_endpointUrl, effectiveModel, jsonContent, streamHandler.GetFullText(),
                streamHandler.GetTotalTokens())
            {
                StatusCode = 200
            };
        });
    }

    private async Task<Payload> ExecuteWithFallbackAsync(Func<string, Task<Payload>> requestFunc)
    {
        if (!string.IsNullOrEmpty(AIService.CurrentRequest?.RawJsonOverride))
            return await requestFunc(null);

        bool userOverrodeReasoning = !string.IsNullOrWhiteSpace(customRequestJson) &&
            (customRequestJson.Contains("\"thinking\"") || customRequestJson.Contains("\"reasoning_effort\""));

        if (userOverrodeReasoning)
            return await requestFunc(null);

        var settings = Settings.Get();
        string normalizedModel = model?.StartsWith("models/") == true ? model.Substring(7) : model;
        string cacheKey = $"{GetEffectiveProvider()}_{normalizedModel}";

        if (settings?.DetectedThinkingLevels != null &&
            settings.DetectedThinkingLevels.TryGetValue(cacheKey, out var cachedLevel))
        {
            try
            {
                return await requestFunc(cachedLevel);
            }
            catch (AIRequestException ex) when (ex.Payload?.StatusCode == 400)
            {
                AIErrorHandler.EnqueueMessage(() =>
                {
                    var s = Settings.Get();
                    if (s?.DetectedThinkingLevels != null && s.DetectedThinkingLevels.Remove(cacheKey))
                    {
                        s.Write();
                    }
                });
                Logger.Warning($"Cached thinking level '{cachedLevel}' failed for '{cacheKey}'. Retrying ladder...");
            }
        }

        AIRequestException lastEx = null;
        foreach (var level in ThinkingLadder)
        {
            try
            {
                var payload = await requestFunc(level);
                AIErrorHandler.EnqueueMessage(() =>
                {
                    var s = Settings.Get();
                    if (s != null)
                    {
                        s.DetectedThinkingLevels ??= new Dictionary<string, string>();
                        s.DetectedThinkingLevels[cacheKey] = level;
                        s.Write();
                        Logger.Message($"Detected and saved thinking level '{level}' for '{cacheKey}'.");
                    }
                });
                return payload;
            }
            catch (AIRequestException ex) when (ex.Payload?.StatusCode == 400)
            {
                lastEx = ex;
                Logger.Warning($"Model '{model}' failed with thinking level '{level}' (HTTP 400). Trying next level...");
            }
        }

        if (lastEx != null)
            throw lastEx;

        return null;
    }

    private string GetEffectiveModel(string jsonContent)
    {
        if (!string.IsNullOrEmpty(AIService.CurrentRequest?.RawJsonOverride))
        {
            var parsed = JsonUtil.ParseJsonValue(jsonContent, out _) as Dictionary<string, object>;
            if (parsed != null && parsed.TryGetValue("model", out var m) && m is string mStr && !string.IsNullOrWhiteSpace(mStr))
                return mStr;
        }
        return model;
    }

    private string BuildRequestJson(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages, bool stream, string imageBase64 = null,
        string reasoningLevel = null)
    {
        if (!string.IsNullOrEmpty(AIService.CurrentRequest?.RawJsonOverride))
            return AIService.CurrentRequest.RawJsonOverride;
        
        bool disableThinking = reasoningLevel == "disabled";
        string effort = reasoningLevel is "minimal" or "low" ? reasoningLevel : null;

        var request = new ChatRequest
        {
            Model = model,
            Stream = stream,
            DisableThinking = disableThinking,
            ReasoningEffort = effort,
            Messages = BuildMessages(prefixMessages, messages, imageBase64)
        };

        string baseJson = JsonUtil.SerializeJsonValue(request.ToPayload());
        return string.IsNullOrWhiteSpace(customRequestJson)
            ? baseJson
            : JsonUtil.MergeJson(baseJson, customRequestJson);
    }

    private List<ChatMessage> BuildMessages(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages, string imageBase64)
    {
        var rawMessages = new List<(Role role, string message)>();
        if (prefixMessages != null) rawMessages.AddRange(prefixMessages);
        if (messages != null) rawMessages.AddRange(messages);

        var merged = new List<ChatMessage>();

        // Gemma-3 workaround: convert system messages into an initial user message
        if (!string.IsNullOrEmpty(model) && model.Contains("gemma-3"))
        {
            var systemMessages = rawMessages.Where(m => m.role == Role.System).ToList();
            if (systemMessages.Count > 0)
            {
                var systemText = string.Join("\n\n", systemMessages.Select(m => m.message));
                merged.Add(new ChatMessage("user", $"{_random.Next()} {systemText}"));
                rawMessages.RemoveAll(m => m.role == Role.System);
            }
        }

        foreach (var (role, text) in rawMessages)
        {
            var roleStr = RoleToString(role);
            if (merged.Count > 0 && merged.Last().Role == roleStr)
                merged.Last().Text += "\n\n" + text;
            else
                merged.Add(new ChatMessage(roleStr, text));
        }

        if (!string.IsNullOrEmpty(imageBase64))
        {
            var lastUser = merged.LastOrDefault(m => m.Role == "user");
            if (lastUser != null && lastUser == merged.Last())
                lastUser.ImageBase64 = imageBase64;
            else
                merged.Add(new ChatMessage("user", "", imageBase64));
        }

        return merged;
    }

    private static string RoleToString(Role role)
    {
        return role switch
        {
            Role.System => "system",
            Role.User => "user",
            Role.AI => "assistant",
            _ => "user"
        };
    }

    // downloadHandler is unused; the parameter stays so addon patches on this signature keep working.
    private Task<string> SendRequestAsync(string jsonContent, DownloadHandler downloadHandler) =>
        SendHttpAsync(jsonContent, null);

    private async Task<string> SendHttpAsync(string jsonContent, OpenAIStreamHandler streamHandler)
    {
        if (string.IsNullOrEmpty(_endpointUrl))
        {
            Logger.Error("Endpoint URL is missing.");
            return null;
        }

        Logger.Debug($"API request: {_endpointUrl}\n{jsonContent}");

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        AddAuthHeaders(request, apiKey, extraHeaders);

        bool isLocal = LocalHost.IsLocal(new Uri(_endpointUrl).DnsSafeHost);
        float connectTimeout = isLocal ? 300f : 60f;
        const float readTimeout = 60f;

        using var timeoutCts = new CancellationTokenSource();
        using var response = await HttpUtil.SendWithTimeoutAsync(request, connectTimeout, timeoutCts,
            streamHandler != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
        if (response == null) return null;

        int statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            string errorText = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == (HttpStatusCode)429)
            {
                string quotaMsg = ErrorUtil.ExtractErrorMessage(errorText) ?? "Quota exceeded";
                throw new QuotaExceededException(quotaMsg,
                    new Payload(_endpointUrl, model, jsonContent, errorText, 0, quotaMsg) { StatusCode = statusCode });
            }

            string errorMsg = ErrorUtil.ExtractErrorMessage(errorText) ?? response.ReasonPhrase;
            Logger.Warning($"Request failed: {statusCode} - {errorMsg}");
            throw new AIRequestException(errorMsg,
                new Payload(_endpointUrl, model, jsonContent, errorText, 0, errorMsg) { StatusCode = statusCode });
        }

        if (streamHandler == null)
        {
            string responseText = await response.Content.ReadAsStringAsync();
            Logger.Debug($"API response: \n{responseText}");
            return responseText;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        await HttpUtil.ReadStreamWithTimeoutAsync(stream, readTimeout, timeoutCts, (buf, len) =>
        {
            streamHandler.FeedBytes(buf, len);
            ThrowIfStreamError(streamHandler, jsonContent, statusCode);
        });

        streamHandler.Flush();
        ThrowIfStreamError(streamHandler, jsonContent, statusCode);
        Logger.Debug($"API response: \n{streamHandler.GetRawJson()}");
        return streamHandler.GetFullText();
    }

    private void ThrowIfStreamError(OpenAIStreamHandler streamHandler, string jsonContent, int statusCode)
    {
        if (string.IsNullOrEmpty(streamHandler.DetectedError)) return;

        string errorMsg = streamHandler.DetectedError;
        throw new AIRequestException(errorMsg,
            new Payload(_endpointUrl, model, jsonContent, streamHandler.GetAllReceivedText(), 0, errorMsg) { StatusCode = statusCode });
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string key, Dictionary<string, string> headers)
    {
        if (!string.IsNullOrEmpty(key))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
            request.Headers.TryAddWithoutValidation("x-api-key", key);
        }

        if (headers == null) return;
        foreach (var header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    public static Task<List<string>> FetchModelsAsync(string apiKey, string url) =>
        FetchModelsAsync(apiKey, url, null);

    internal static async Task<List<string>> FetchModelsAsync(string apiKey, string url, Dictionary<string, string> extraHeaders)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeaders(request, apiKey, extraHeaders);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await HttpUtil.Client.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"Failed to fetch models: {(int)response.StatusCode} {response.ReasonPhrase}");
                return [];
            }

            var models = JsonUtil.DeserializeFromJson<OpenAIModelsResponse>(await response.Content.ReadAsStringAsync());
            return models?.Data?.Select(m => m.Id).ToList() ?? [];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to fetch models: {ex.Message}");
            return [];
        }
    }
}