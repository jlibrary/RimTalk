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

public class OpenAIClient : IAIClient
{
    private const string OpenAIPath = "/v1/chat/completions";
    private readonly string _apiKey;
    private readonly string _model;
    private readonly Dictionary<string, string> _extraHeaders;

    public OpenAIClient(string baseUrl, string model, string apiKey = null, Dictionary<string, string> extraHeaders = null)
    {
        _model = model;
        _apiKey = apiKey;
        _extraHeaders = extraHeaders;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var trimmedUrl = baseUrl.Trim().TrimEnd('/');

            var uri = new Uri(trimmedUrl);
            // Check if they provided just a base URL without a specific API path
            if (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')))
            {
                EndpointUrl = trimmedUrl + OpenAIPath;
            }
            else
            {
                // They provided a full path, use as-is
                EndpointUrl = trimmedUrl;
            }
        }
        else
        {
            EndpointUrl = string.Empty;
        }
    }

    private string EndpointUrl { get; }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(string instruction,
        List<(Role role, string message)> messages, Action<T> onResponseParsed) where T : class
    {
        var allMessages = new List<Message>();

        if (!string.IsNullOrEmpty(instruction))
        {
            allMessages.Add(new Message
            {
                Role = "system",
                Content = instruction
            });
        }

        allMessages.AddRange(messages.Select(m => new Message
        {
            Role = ConvertRole(m.role),
            Content = m.message
        }));

        var request = new OpenAIRequest
        {
            Model = _model,
            Messages = allMessages,
            Stream = true,
            StreamOptions = new StreamOptions { IncludeUsage = true }
        };

        string jsonContent = JsonUtil.SerializeToJson(request);
        
        var jsonParser = new JsonStreamParser<T>();
        var streamingHandler = new OpenAIStreamHandler(contentChunk =>
        {
            var responses = jsonParser.Parse(contentChunk);
            foreach (var response in responses)
            {
                onResponseParsed?.Invoke(response);
            }
        });

        if (string.IsNullOrEmpty(EndpointUrl))
        {
            Logger.Error("Endpoint URL is missing.");
            return null;
        }

        try
        {
            Logger.Debug($"API request: {EndpointUrl}\n{jsonContent}");

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            using var webRequest = new UnityWebRequest(EndpointUrl, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = streamingHandler;
            webRequest.SetRequestHeader("Content-Type", "application/json");

            if (_extraHeaders != null)
            {
                foreach (var header in _extraHeaders)
                {
                    webRequest.SetRequestHeader(header.Key, header.Value);
                }
            }

            if (!string.IsNullOrEmpty(_apiKey))
            {
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            }

            var asyncOperation = webRequest.SendWebRequest();

            while (!asyncOperation.isDone)
            {
                if (Current.Game == null) return null;
                await Task.Delay(100);
            }
            
            string responseText = webRequest.downloadHandler?.text;

            if (webRequest.responseCode >= 400 || webRequest.isNetworkError || webRequest.isHttpError)
            {
                 responseText = streamingHandler.GetAllReceivedText();
            }

            if (string.IsNullOrEmpty(responseText) && streamingHandler != null)
            {
                 responseText = streamingHandler.GetRawJson();
            }

            if (webRequest.responseCode == 429)
            {
                string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? "Quota exceeded";
                var payload = new Payload(EndpointUrl, _model, jsonContent, responseText, 0, errorMsg);
                throw new QuotaExceededException(errorMsg, payload);
            }

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? webRequest.error;
                Logger.Error($"Request failed: {webRequest.responseCode} - {errorMsg}");
                var payload = new Payload(EndpointUrl, _model, jsonContent, responseText, 0, errorMsg);
                throw new AIRequestException(errorMsg, payload);
            }
            
            var fullResponse = streamingHandler.GetFullText();
            var tokens = streamingHandler.GetTotalTokens();
            Logger.Debug($"API response: \n{streamingHandler.GetRawJson()}");
            return new Payload(EndpointUrl, _model, jsonContent, fullResponse, tokens);
        }
        catch (AIRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in API request: {ex.Message}");
            var payload = new Payload(EndpointUrl, _model, jsonContent, null, 0, ex.Message);
            throw new AIRequestException(ex.Message, payload);
        }
    }

    public async Task<Payload> GetChatCompletionAsync(string instruction,
        List<(Role role, string message)> messages)
    {
        var allMessages = new List<Message>();

        if (!string.IsNullOrEmpty(instruction))
        {
            allMessages.Add(new Message
            {
                Role = "system",
                Content = instruction
            });
        }

        allMessages.AddRange(messages.Select(m => new Message
        {
            Role = ConvertRole(m.role),
            Content = m.message
        }));

        var request = new OpenAIRequest
        {
            Model = _model,
            Messages = allMessages
        };

        string jsonContent = JsonUtil.SerializeToJson(request);
        var response = await GetCompletionAsync(jsonContent);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;
        return new Payload(EndpointUrl, _model, jsonContent, content, tokens);
    }

    private async Task<OpenAIResponse> GetCompletionAsync(string jsonContent)
    {
        if (string.IsNullOrEmpty(EndpointUrl))
        {
            Logger.Error("Endpoint URL is missing.");
            return null;
        }

        try
        {
            Logger.Debug($"API request: {EndpointUrl}\n{jsonContent}");

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            using var webRequest = new UnityWebRequest(EndpointUrl, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            if (_extraHeaders != null)
            {
                foreach (var header in _extraHeaders)
                {
                    webRequest.SetRequestHeader(header.Key, header.Value);
                }
            }

            if (!string.IsNullOrEmpty(_apiKey))
            {
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            }

            var asyncOperation = webRequest.SendWebRequest();

            while (!asyncOperation.isDone)
            {
                if (Current.Game == null) return null;
                await Task.Delay(100);
            }

            Logger.Debug($"API response: \n{webRequest.downloadHandler.text}");

            string responseText = webRequest.downloadHandler.text;

            if (webRequest.responseCode == 429)
            {
                string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? "Quota exceeded";
                var payload = new Payload(EndpointUrl, _model, jsonContent, responseText, 0, errorMsg);
                throw new QuotaExceededException(errorMsg, payload);
            }

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? webRequest.error;
                Logger.Error($"Request failed: {webRequest.responseCode} - {errorMsg}");
                var payload = new Payload(EndpointUrl, _model, jsonContent, responseText, 0, errorMsg);
                throw new AIRequestException(errorMsg, payload);
            }

            return JsonUtil.DeserializeFromJson<OpenAIResponse>(responseText);
        }
        catch (AIRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in API request: {ex.Message}");
            var payload = new Payload(EndpointUrl, _model, jsonContent, null, 0, ex.Message);
            throw new AIRequestException(ex.Message, payload);
        }
    }

    private string ConvertRole(Role role)
    {
        switch (role)
        {
            case Role.User:
                return "user";
            case Role.AI:
                return "assistant";
            default:
                throw new ArgumentException($"Unknown role: {role}");
        }
    }
    
    public static async Task<List<string>> FetchModelsAsync(string apiKey, string url)
    {
        var models = new List<string>();
        using var webRequest = UnityWebRequest.Get(url);
        webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
        var asyncOperation = webRequest.SendWebRequest();

        while (!asyncOperation.isDone)
        {
            await Task.Delay(100);
        }

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            Logger.Error($"Failed to fetch models: {webRequest.error}");
        }
        else
        {
            var response = JsonUtil.DeserializeFromJson<OpenAIModelsResponse>(webRequest.downloadHandler.text);
            if (response != null && response.Data != null)
            {
                models = response.Data.Select(m => m.Id).ToList();
            }
        }

        return models;
    }
}
