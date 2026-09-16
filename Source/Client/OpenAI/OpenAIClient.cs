using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            await SendRequestAsync(jsonContent, streamHandler);

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
                LongEventHandler.ExecuteWhenFinished(() =>
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
                LongEventHandler.ExecuteWhenFinished(() =>
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

    private async Task<string> SendRequestAsync(string jsonContent, DownloadHandler downloadHandler)
    {
        if (string.IsNullOrEmpty(_endpointUrl))
        {
            Logger.Error("Endpoint URL is missing.");
            return null;
        }

        Logger.Debug($"API request: {_endpointUrl}\n{jsonContent}");

        using var webRequest = new UnityWebRequest(_endpointUrl, "POST");
        webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonContent));
        webRequest.downloadHandler = downloadHandler;
        webRequest.SetRequestHeader("Content-Type", "application/json");

        if (!string.IsNullOrEmpty(apiKey))
        {
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            webRequest.SetRequestHeader("x-api-key", apiKey);
        }

        if (extraHeaders != null)
        {
            foreach (var header in extraHeaders)
                webRequest.SetRequestHeader(header.Key, header.Value);
        }

        var asyncOp = webRequest.SendWebRequest();

        // Determine if target is local
        bool isLocal = _endpointUrl.Contains("localhost") || _endpointUrl.Contains("127.0.0.1") ||
                       _endpointUrl.Contains("192.168.") || _endpointUrl.Contains("10.");

        DateTime lastActiveTime = DateTime.UtcNow;
        ulong lastBytes = 0;
        float connectTimeout = isLocal ? 300f : 60f;
        float readTimeout = 60f;

        while (!asyncOp.isDone)
        {
            if (Current.Game == null) return null;
            if (AIService.IsCancellationRequested())
            {
                webRequest.Abort();
                throw new OperationCanceledException("Request canceled by fast-track request.");
            }
            await Task.Delay(100);

            ulong currentBytes = webRequest.downloadedBytes;
            bool hasStartedReceiving = currentBytes > 0;

            if (currentBytes > lastBytes)
            {
                lastActiveTime = DateTime.UtcNow;
                lastBytes = currentBytes;
            }

            float inactiveSeconds = (float)(DateTime.UtcNow - lastActiveTime).TotalSeconds;

            if (!hasStartedReceiving && inactiveSeconds > connectTimeout)
            {
                webRequest.Abort();
                throw new TimeoutException($"Connection timed out (Waited {connectTimeout}s for first token)");
            }

            if (hasStartedReceiving && inactiveSeconds > readTimeout)
            {
                webRequest.Abort();
                throw new TimeoutException($"Read timed out (Stalled for {readTimeout}s during generation)");
            }
        }

        string responseText = downloadHandler.text;

        // Recover text for streaming errors
        if (downloadHandler is OpenAIStreamHandler sHandler)
        {
            sHandler.Flush();
            if (!string.IsNullOrEmpty(sHandler.DetectedError))
            {
                string errorMsg = sHandler.DetectedError;
                string allText = sHandler.GetAllReceivedText();
                throw new AIRequestException(errorMsg,
                    new Payload(_endpointUrl, model, jsonContent, allText, 0, errorMsg) { StatusCode = (int)webRequest.responseCode });
            }

            if (webRequest.responseCode >= 400 || webRequest.isNetworkError || webRequest.isHttpError)
            {
                responseText = sHandler.GetAllReceivedText();
                if (string.IsNullOrEmpty(responseText)) responseText = sHandler.GetRawJson();
            }
        }

        if (webRequest.responseCode == 429)
        {
            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? "Quota exceeded";
            throw new QuotaExceededException(errorMsg,
                new Payload(_endpointUrl, model, jsonContent, responseText, 0, errorMsg) { StatusCode = (int)webRequest.responseCode });
        }

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? webRequest.error;
            Logger.Warning($"Request failed: {webRequest.responseCode} - {errorMsg}");
            throw new AIRequestException(errorMsg,
                new Payload(_endpointUrl, model, jsonContent, responseText, 0, errorMsg) { StatusCode = (int)webRequest.responseCode });
        }

        if (downloadHandler is DownloadHandlerBuffer)
            Logger.Debug($"API response: \n{responseText}");
        else if (downloadHandler is OpenAIStreamHandler sh)
            Logger.Debug($"API response: \n{sh.GetRawJson()}");

        return responseText;
    }

    public static async Task<List<string>> FetchModelsAsync(string apiKey, string url)
    {
        using var webRequest = UnityWebRequest.Get(url);
        if (!string.IsNullOrEmpty(apiKey))
        {
            webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            webRequest.SetRequestHeader("x-api-key", apiKey);
            webRequest.SetRequestHeader("anthropic-version", "2023-06-01");
        }

        var asyncOp = webRequest.SendWebRequest();
        while (!asyncOp.isDone) await Task.Delay(100);

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            Logger.Warning($"Failed to fetch models: {webRequest.error}");
            return [];
        }

        var response = JsonUtil.DeserializeFromJson<OpenAIModelsResponse>(webRequest.downloadHandler.text);
        return response?.Data?.Select(m => m.Id).ToList() ?? [];
    }
}