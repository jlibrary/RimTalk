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
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private static string CurrentApiKey => Settings.Get().GetActiveConfig()?.ApiKey;
    private static string CurrentModel => Settings.Get().GetCurrentModel();
    private static string EndpointUrl => $"{BaseUrl}/models/{CurrentModel}:generateContent?key={CurrentApiKey}";
    private static string StreamEndpointUrl => $"{BaseUrl}/models/{CurrentModel}:streamGenerateContent?alt=sse&key={CurrentApiKey}";

    private readonly Random _random = new();

    /// <summary>
    /// Gets a standard chat completion.
    /// </summary>
    public async Task<Payload> GetChatCompletionAsync(string instruction, List<(Role role, string message)> messages)
    {
        string jsonContent = BuildRequestJson(instruction, messages);
        var response = await SendRequestAsync<GeminiResponse>(EndpointUrl, jsonContent, new DownloadHandlerBuffer());

        var content = response?.Candidates?[0]?.Content?.Parts?[0]?.Text;
        var tokens = response?.UsageMetadata?.TotalTokenCount ?? 0;

        return new Payload(jsonContent, content, tokens);
    }

    /// <summary>
    /// Streams chat completion and invokes a callback for each response chunk.
    /// </summary>
    public async Task<Payload> GetStreamingChatCompletionAsync<T>(string instruction,
        List<(Role role, string message)> messages, Action<T> onResponseParsed) where T : class
    {
        string jsonContent = BuildRequestJson(instruction, messages);
        var jsonParser = new JsonStreamParser<T>();

        var streamingHandler = new GeminiStreamHandler(jsonChunk =>
        {
            var responses = jsonParser.Parse(jsonChunk);
            foreach (var response in responses)
            {
                onResponseParsed?.Invoke(response);
            }
        });

        await SendRequestAsync<object>(StreamEndpointUrl, jsonContent,
            streamingHandler); // Type param is not used here, so 'object' is a placeholder.

        var fullResponse = streamingHandler.GetFullText();
        var tokens = streamingHandler.GetTotalTokens();

        Logger.Debug($"API response: \n{streamingHandler.GetRawJson()}");
        return new Payload(jsonContent, fullResponse, tokens);
    }

    /// <summary>
    /// Builds the JSON payload for the Gemini API request.
    /// </summary>
    private string BuildRequestJson(string instruction, List<(Role role, string message)> messages)
    {
        SystemInstruction systemInstruction = null;
        var allMessages = new List<(Role role, string message)>();

        if (CurrentModel.Contains("gemma"))
        {
            // For Gemma models, the instruction is added as a user message with a random prefix.
            allMessages.Add((Role.User, $"{_random.Next()} {instruction}"));
        }
        else
        {
            systemInstruction = new SystemInstruction
            {
                Parts = [new Part { Text = instruction }]
            };
        }

        allMessages.AddRange(messages);

        var generationConfig = new GenerationConfig();
        if (CurrentModel.Contains("flash"))
        {
            generationConfig.ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 };
        }

        var request = new GeminiDto()
        {
            SystemInstruction = systemInstruction,
            Contents = allMessages.Select(m => new Content
            {
                Role = ConvertRole(m.role),
                Parts = [new Part { Text = m.message }]
            }).ToList(),
            GenerationConfig = generationConfig
        };

        return JsonUtil.SerializeToJson(request);
    }

    /// <summary>
    /// A generic method to handle sending UnityWebRequests.
    /// </summary>
    private async Task<T> SendRequestAsync<T>(string url, string jsonContent, DownloadHandler downloadHandler)
        where T : class
    {
        if (string.IsNullOrEmpty(CurrentApiKey))
        {
            Logger.Error("API key is missing.");
            return null;
        }

        try
        {
            Logger.Debug($"API request: {url}\n{jsonContent}");

            using var webRequest = new UnityWebRequest(url, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonContent));
            webRequest.downloadHandler = downloadHandler;
            webRequest.SetRequestHeader("Content-Type", "application/json");

            var asyncOperation = webRequest.SendWebRequest();

            while (!asyncOperation.isDone)
            {
                if (Current.Game == null) return null; // Exit if the game is no longer running.
                await Task.Delay(100);
            }

            if (downloadHandler is DownloadHandlerBuffer)
            {
                Logger.Debug($"API response: \n{webRequest.downloadHandler.text}");
            }

            if (webRequest.responseCode == 429)
                throw new QuotaExceededException("Quota exceeded");
            if (webRequest.responseCode == 503)
                throw new QuotaExceededException("Model overloaded");

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                var errorMessage = $"Request failed: {webRequest.responseCode} - {webRequest.error}";
                Logger.Error(errorMessage);
                throw new Exception(errorMessage);
            }

            // For non-streaming, deserialize the response. For streaming, the handler processes data, and we return null.
            if (downloadHandler is DownloadHandlerBuffer)
            {
                var response = JsonUtil.DeserializeFromJson<GeminiResponse>(webRequest.downloadHandler.text);
                if (response?.Candidates?[0]?.FinishReason == "MAX_TOKENS")
                    throw new QuotaExceededException("Quota exceeded (MAX_TOKENS)");

                return response as T;
            }

            return null; // For streaming, the result is handled by the callback.
        }
        catch (QuotaExceededException)
        {
            throw; // Re-throw specific exceptions to be handled upstream.
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in API request: {ex.Message}");
            throw;
        }
    }

    private string ConvertRole(Role role)
    {
        return role switch
        {
            Role.User => "user",
            Role.AI => "model",
            _ => throw new ArgumentException($"Unknown role: {role}"),
        };
    }
    public static async Task<List<string>> FetchModelsAsync(string apiKey, string url)
    {
        var models = new List<string>();

        using var webRequest = UnityWebRequest.Get($"{url}?key={apiKey}");
        var asyncOperation = webRequest.SendWebRequest();

        while (!asyncOperation.isDone)
        {
            await Task.Delay(100);
        }

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            Logger.Error($"Failed to fetch Google models: {webRequest.error}");
        }
        else
        {
            try
            {
                var response = JsonUtil.DeserializeFromJson<GoogleModelsResponse>(webRequest.downloadHandler.text);
                if (response != null && response.Models != null)
                {
                    models = response.Models
                        .Where(m => m.SupportedGenerationMethods != null &&
                                    m.SupportedGenerationMethods.Contains("generateContent"))
                        .Select(m => m.Name.StartsWith("models/") ? m.Name.Substring(7) : m.Name)
                        .OrderBy(m => m)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse Google models response: {ex.Message}");
            }
        }

        return models;
    }
}