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
using Enumerable = System.Linq.Enumerable;

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

    private static string FormatEndpointUrl(string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
        var trimmed = baseUrl.Trim().TrimEnd('/');
        var uri = new Uri(trimmed);
        // Append default path if only base domain is provided
        return (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')))
            ? trimmed + DefaultPath
            : trimmed;
    }

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        Action<Payload> onRequestPrepared = null)
    {
        return await GetChatCompletionAsync(prefixMessages, messages, null, onRequestPrepared);
    }

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        string imageBase64,
        Action<Payload> onRequestPrepared = null)
    {
        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: false, imageBase64: imageBase64);
        onRequestPrepared?.Invoke(new Payload(_endpointUrl, model, jsonContent, null, 0));
        string responseText = await SendRequestAsync(jsonContent, new DownloadHandlerBuffer());

        var response = JsonUtil.DeserializeFromJson<OpenAIResponse>(responseText);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;

        return new Payload(_endpointUrl, model, jsonContent, content, tokens);
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
        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: true, imageBase64: imageBase64);
        onRequestPrepared?.Invoke(new Payload(_endpointUrl, model, jsonContent, null, 0));
        var jsonParser = new JsonStreamParser<T>();

        var streamHandler = new OpenAIStreamHandler(chunk =>
        {
            foreach (var response in jsonParser.Parse(chunk))
                onResponseParsed?.Invoke(response);
        });

        await SendRequestAsync(jsonContent, streamHandler);

        return new Payload(_endpointUrl, model, jsonContent, streamHandler.GetFullText(),
            streamHandler.GetTotalTokens());
    }

    public async Task<Payload> GetStreamingTextCompletionAsync(
        List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages,
        string imageBase64,
        Action<string> onChunkReceived,
        Action<Payload> onRequestPrepared = null)
    {
        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: true, imageBase64: imageBase64);
        onRequestPrepared?.Invoke(new Payload(_endpointUrl, model, jsonContent, null, 0));

        var streamHandler = new OpenAIStreamHandler(chunk =>
        {
            if (!string.IsNullOrEmpty(chunk))
            {
                onChunkReceived?.Invoke(chunk);
            }
        });

        await SendRequestAsync(jsonContent, streamHandler);

        return new Payload(_endpointUrl, model, jsonContent, streamHandler.GetFullText(),
            streamHandler.GetTotalTokens());
    }

    private string BuildRequestJson(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages, bool stream, string imageBase64 = null)
    {
        var rawMessages = new List<(Role role, string message)>();
        if (prefixMessages != null) rawMessages.AddRange(prefixMessages);
        if (messages != null) rawMessages.AddRange(messages);

        var mergedMessages = new List<Message>();

        bool isGemma3 = !string.IsNullOrEmpty(model) && model.Contains("gemma-3");
        if (isGemma3)
        {
            var systemMessages = Enumerable.ToList(Enumerable.Where(rawMessages, m => m.role == Role.System));
            if (systemMessages.Any())
            {
                var systemText = string.Join("\n\n", Enumerable.Select(systemMessages, m => m.message));

                mergedMessages.Add(new Message
                {
                    Role = "user",
                    Content = $"{_random.Next()} {systemText}"
                });
                rawMessages.RemoveAll(m => m.role == Role.System);
            }
        }

        foreach (var m in rawMessages)
        {
            var roleStr = RoleToString(m.role);
            if (mergedMessages.Count > 0 && mergedMessages.Last().Role == roleStr)
            {
                mergedMessages.Last().Content += "\n\n" + m.message;
            }
            else
            {
                mergedMessages.Add(new Message
                {
                    Role = roleStr,
                    Content = m.message
                });
            }
        }
        
        string reasoningEffort = null;

        if (!string.IsNullOrEmpty(model))
        {
            string m = model.ToLower();
            if (m.Contains("gemini") && (m.Contains("pro") || m.Contains("3.7-flash")))
                reasoningEffort = "low";
            else if ((m.Contains("gemini") && m.Contains("flash")) || m.Contains("gemma-4"))
                reasoningEffort = "minimal";
        }

        string baseJson;
        if (!string.IsNullOrEmpty(imageBase64))
        {
            var messageDicts = new List<object>();
            bool imageAttached = false;

            for (int i = 0; i < mergedMessages.Count; i++)
            {
                var msg = mergedMessages[i];
                if (i == mergedMessages.Count - 1 && msg.Role == "user")
                {
                    imageAttached = true;
                    messageDicts.Add(new Dictionary<string, object>
                    {
                        ["role"] = msg.Role,
                        ["content"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["type"] = "text",
                                ["text"] = msg.Content ?? ""
                            },
                            new Dictionary<string, object>
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new Dictionary<string, object>
                                {
                                    ["url"] = $"data:image/jpeg;base64,{imageBase64}",
                                    ["detail"] = "auto"
                                }
                            }
                        }
                    });
                }
                else
                {
                    messageDicts.Add(new Dictionary<string, object>
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? ""
                    });
                }
            }

            if (!imageAttached)
            {
                messageDicts.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new Dictionary<string, object>
                            {
                                ["url"] = $"data:image/jpeg;base64,{imageBase64}",
                                ["detail"] = "auto"
                            }
                        }
                    }
                });
            }

            var rootDict = new Dictionary<string, object>
            {
                ["model"] = model,
                ["messages"] = messageDicts,
                ["stream"] = stream
            };
            if (stream)
            {
                rootDict["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true };
            }
            if (!string.IsNullOrEmpty(reasoningEffort))
            {
                rootDict["reasoning_effort"] = reasoningEffort;
            }

            baseJson = JsonUtil.SerializeJsonValue(rootDict);
        }
        else
        {
            var request = new OpenAIRequest
            {
                Model = model,
                Messages = mergedMessages,
                Stream = stream,
                StreamOptions = stream ? new StreamOptions { IncludeUsage = true } : null,
                ReasoningEffort = reasoningEffort
            };

            baseJson = JsonUtil.SerializeToJson(request);
        }

        if (!string.IsNullOrWhiteSpace(customRequestJson))
        {
            return JsonUtil.MergeJson(baseJson, customRequestJson);
        }

        return baseJson;
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
            webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        if (extraHeaders != null)
        {
            foreach (var header in extraHeaders)
                webRequest.SetRequestHeader(header.Key, header.Value);
        }

        var asyncOp = webRequest.SendWebRequest();

        // Determine if target is local
        bool isLocal = _endpointUrl.Contains("localhost") || _endpointUrl.Contains("127.0.0.1") ||
                       _endpointUrl.Contains("192.168.") || _endpointUrl.Contains("10.");

        float inactivityTimer = 0f;
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
                inactivityTimer = 0f;
                lastBytes = currentBytes;
            }
            else
            {
                inactivityTimer += 0.1f;
            }

            if (!hasStartedReceiving && inactivityTimer > connectTimeout)
            {
                webRequest.Abort();
                throw new TimeoutException($"Connection timed out (Waited {connectTimeout}s for first token)");
            }

            if (hasStartedReceiving && inactivityTimer > readTimeout)
            {
                webRequest.Abort();
                throw new TimeoutException($"Read timed out (Stalled for {readTimeout}s during generation)");
            }
        }

        string responseText = downloadHandler.text;

        // Recover text for streaming errors
        if (downloadHandler is OpenAIStreamHandler sHandler)
        {
            if (!string.IsNullOrEmpty(sHandler.DetectedError))
            {
                string errorMsg = sHandler.DetectedError;
                string allText = sHandler.GetAllReceivedText();
                throw new AIRequestException(errorMsg,
                    new Payload(_endpointUrl, model, jsonContent, allText, 0, errorMsg));
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
                new Payload(_endpointUrl, model, jsonContent, responseText, 0, errorMsg));
        }

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? webRequest.error;
            Logger.Warning($"Request failed: {webRequest.responseCode} - {errorMsg}");
            throw new AIRequestException(errorMsg,
                new Payload(_endpointUrl, model, jsonContent, responseText, 0, errorMsg));
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
        webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

        var asyncOp = webRequest.SendWebRequest();
        while (!asyncOp.isDone) await Task.Delay(100);

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            Logger.Error($"Failed to fetch models: {webRequest.error}");
            return new List<string>();
        }

        var response = JsonUtil.DeserializeFromJson<OpenAIModelsResponse>(webRequest.downloadHandler.text);
        return response?.Data?.Select(m => m.Id).ToList() ?? new List<string>();
    }
}