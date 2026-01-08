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

namespace RimTalk.Client.OpenAI;

public class OpenAIClient(
    string baseUrl,
    string model,
    string apiKey = null,
    Dictionary<string, string> extraHeaders = null)
    : IAIClient
{
    private const string DefaultPath = "/v1/chat/completions";
    private readonly string _endpointUrl = FormatEndpointUrl(baseUrl);

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

    public async Task<Payload> GetChatCompletionAsync(string instruction, List<(Role role, string message)> messages)
    {
        string jsonContent = BuildRequestJson(instruction, messages, stream: false);
        string responseText = await SendRequestAsync(jsonContent, new DownloadHandlerBuffer());

        var response = JsonUtil.DeserializeFromJson<OpenAIResponse>(responseText);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;

        return new Payload(_endpointUrl, model, jsonContent, content, tokens);
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(string instruction,
        List<(Role role, string message)> messages, Action<T> onResponseParsed) where T : class
    {
        string jsonContent = BuildRequestJson(instruction, messages, stream: true);
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

    private string BuildRequestJson(string instruction, List<(Role role, string message)> messages, bool stream)
    {
        var allMessages = new List<Message>();
        if (!string.IsNullOrEmpty(instruction))
        {
            allMessages.Add(new Message { Role = "system", Content = instruction });
        }

        allMessages.AddRange(messages.Select(m => new Message
        {
            Role = m.role == Role.User ? "user" : "assistant",
            Content = m.message
        }));

        var request = new OpenAIRequest
        {
            Model = model,
            Messages = allMessages,
            Stream = stream,
            StreamOptions = stream ? new StreamOptions { IncludeUsage = true } : null
        };

        return JsonUtil.SerializeToJson(request);
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
        if ((webRequest.responseCode >= 400 || webRequest.isNetworkError || webRequest.isHttpError) &&
            downloadHandler is OpenAIStreamHandler sHandler)
        {
            responseText = sHandler.GetAllReceivedText();
            if (string.IsNullOrEmpty(responseText)) responseText = sHandler.GetRawJson();
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
            Logger.Error($"Request failed: {webRequest.responseCode} - {errorMsg}");
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