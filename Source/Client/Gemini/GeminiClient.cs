using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.Client.Gemini;

public class GeminiClient : IAIClient
{
    private static string BaseUrl => AIProvider.Google.GetEndpointUrl();
    private static string CurrentApiKey => Settings.Get().GetActiveConfig()?.ApiKey;
    private static string CurrentModel => Settings.Get().GetCurrentModel();
    
    // Helper properties for endpoints
    private static string GenerateEndpoint => $"{BaseUrl}/models/{CurrentModel}:generateContent?key={CurrentApiKey}";
    private static string StreamEndpoint => $"{BaseUrl}/models/{CurrentModel}:streamGenerateContent?alt=sse&key={CurrentApiKey}";

    private readonly Random _random = new Random();

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages, 
        List<(Role role, string message)> messages, 
        Action<Payload> onRequestPrepared = null)
    {
        string jsonContent = BuildRequestJson(prefixMessages, messages);
        onRequestPrepared?.Invoke(new Payload(BaseUrl, CurrentModel, jsonContent, null, 0));
        string responseText = await SendRequestAsync(GenerateEndpoint, jsonContent, new DownloadHandlerBuffer());

        var response = JsonUtil.DeserializeFromJson<GeminiResponse>(responseText);
        var content = response?.Candidates?[0]?.Content?.Parts?[0]?.Text;
        var tokens = response?.UsageMetadata?.TotalTokenCount ?? 0;
        
        // Specific check for max tokens finish reason
        if (response?.Candidates?[0]?.FinishReason == "MAX_TOKENS")
        {
            var msg = "Quota exceeded (MAX_TOKENS)";
            throw new QuotaExceededException(msg, new Payload(BaseUrl, CurrentModel, jsonContent, responseText, tokens, msg));
        }

        return new Payload(BaseUrl, CurrentModel, jsonContent, content, tokens);
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages, 
        Action<T> onResponseParsed,
        Action<Payload> onRequestPrepared = null) where T : class
    {
        string jsonContent = BuildRequestJson(prefixMessages, messages);
        onRequestPrepared?.Invoke(new Payload(BaseUrl, CurrentModel, jsonContent, null, 0));
        var jsonParser = new JsonStreamParser<T>();
        
        var streamHandler = new GeminiStreamHandler(chunk =>
        {
            foreach (var response in jsonParser.Parse(chunk))
                onResponseParsed?.Invoke(response);
        });

        await SendRequestAsync(StreamEndpoint, jsonContent, streamHandler);

        return new Payload(BaseUrl, CurrentModel, jsonContent, streamHandler.GetFullText(), streamHandler.GetTotalTokens());
    }

    private async Task<string> SendRequestAsync(string url, string jsonContent, DownloadHandler downloadHandler)
    {
        if (string.IsNullOrEmpty(CurrentApiKey))
        {
            Logger.Error("API key is missing.");
            return null;
        }

        Logger.Debug($"API request: {url}\n{jsonContent}");

        using var webRequest = new UnityWebRequest(url, "POST");
        webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonContent));
        webRequest.downloadHandler = downloadHandler;
        webRequest.SetRequestHeader("Content-Type", "application/json");

        var asyncOp = webRequest.SendWebRequest();

        float inactivityTimer = 0f;
        ulong lastBytes = 0;
        const float connectTimeout = 60f;
        const float readTimeout = 60f;

        while (!asyncOp.isDone)
        {
            if (Current.Game == null) return null;
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

            // Cloud Timeout Logic
            if (!hasStartedReceiving && inactivityTimer > connectTimeout)
            {
                webRequest.Abort();
                throw new TimeoutException($"Connection timed out ({connectTimeout}s)");
            }
            
            if (hasStartedReceiving && inactivityTimer > readTimeout)
            {
                webRequest.Abort();
                throw new TimeoutException($"Read timed out ({readTimeout}s)");
            }
        }

        string responseText = downloadHandler.text;

        // For streaming, sometimes text is in the buffer but not fully in .text property depending on handler implementation, 
        // or we need to extract from the stream handler if the request failed.
        if ((webRequest.responseCode >= 400 || webRequest.isNetworkError || webRequest.isHttpError) && 
            downloadHandler is GeminiStreamHandler streamHandler)
        {
            responseText = streamHandler.GetAllReceivedText();
            if (string.IsNullOrEmpty(responseText)) responseText = streamHandler.GetRawJson();
        }

        if (webRequest.responseCode == 429 || webRequest.responseCode == 503)
        {
            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? "Quota exceeded/Overloaded";
            throw new QuotaExceededException(errorMsg, new Payload(BaseUrl, CurrentModel, jsonContent, responseText, 0, errorMsg));
        }

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? $"Request failed: {webRequest.responseCode} - {webRequest.error}";
            Logger.Error(errorMsg);
            throw new AIRequestException(errorMsg, new Payload(BaseUrl, CurrentModel, jsonContent, responseText, 0, errorMsg));
        }

        if (downloadHandler is DownloadHandlerBuffer)
        {
            Logger.Debug($"API response: \n{responseText}");
        }
        else if (downloadHandler is GeminiStreamHandler sHandler)
        {
            Logger.Debug($"API response: \n{sHandler.GetRawJson()}");
        }

        return responseText;
    }

    private string BuildRequestJson(List<(Role role, string message)> prefixMessages, List<(Role role, string message)> messages)
    {
        var allMessages = (prefixMessages ?? new List<(Role role, string message)>()).Concat(messages ?? new List<(Role role, string message)>()).ToList();
        
        SystemInstruction systemInstruction = null;
        var contents = new List<Content>();

        // Handle System Instruction (if first message)
        if (allMessages.Count > 0 && allMessages[0].role == Role.System)
        {
            var instruction = allMessages[0].message;
            if (CurrentModel.Contains("gemma"))
            {
                contents.Add(new Content { Role = "user", Parts = new List<Part> { new Part { Text = $"{_random.Next()} {instruction}" } } });
            }
            else
            {
                systemInstruction = new SystemInstruction { Parts = new List<Part> { new Part { Text = instruction } } };
            }
            allMessages.RemoveAt(0);
        }

        // Add remaining messages as separate turns
        contents.AddRange(allMessages.Select(m => new Content
        {
            Role = m.role == Role.User ? "user" : "model",
            Parts = new List<Part> { new Part { Text = m.message } }
        }));

        var config = new GenerationConfig();
        if (CurrentModel.Contains("flash"))
            config.ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 };

        return JsonUtil.SerializeToJson(new GeminiDto
        {
            SystemInstruction = systemInstruction,
            Contents = contents,
            GenerationConfig = config
        });
    }
    
    public static async Task<List<string>> FetchModelsAsync(string apiKey, string url)
    {
        using var webRequest = UnityWebRequest.Get($"{url}?key={apiKey}");
        var asyncOp = webRequest.SendWebRequest();
        while (!asyncOp.isDone) await Task.Delay(100);

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            Logger.Error($"Failed to fetch Google models: {webRequest.error}");
            return new List<string>();
        }

        try
        {
            var response = JsonUtil.DeserializeFromJson<GoogleModelsResponse>(webRequest.downloadHandler.text);
            return response?.Models?
                .Where(m => m.SupportedGenerationMethods?.Contains("generateContent") ?? false)
                .Select(m => m.Name.StartsWith("models/") ? m.Name.Substring(7) : m.Name)
                .OrderBy(m => m)
                .ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to parse Google models: {ex.Message}");
            return new List<string>();
        }
    }
}
