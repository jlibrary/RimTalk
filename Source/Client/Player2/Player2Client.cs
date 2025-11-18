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

namespace RimTalk.Client.Player2;

public class Player2Client : IAIClient
{
    private readonly string _apiKey;
    private const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb";
    private const string BaseUrl = "https://api.player2.game/v1";
    private static DateTime _lastHealthCheck = DateTime.MinValue;
    private static bool _healthCheckActive = false;

    public Player2Client(string apiKey)
    {
        _apiKey = apiKey;
        
        if (!_healthCheckActive && !string.IsNullOrEmpty(apiKey))
        {
            _healthCheckActive = true;
            StartHealthCheck();
        }
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(string instruction,
        List<(Role role, string message)> messages, Action<T> onResponseParsed) where T : class
    {
        await EnsureHealthCheck();
        
        var allMessages = BuildMessages(instruction, messages);
        var request = new Player2Request
        {
            Messages = allMessages,
            Stream = true
        };

        string jsonContent = JsonUtil.SerializeToJson(request);
        var jsonParser = new JsonStreamParser<T>();
        var streamingHandler = new Player2StreamHandler(contentChunk =>
        {
            var responses = jsonParser.Parse(contentChunk);
            foreach (var response in responses)
            {
                onResponseParsed?.Invoke(response);
            }
        });

        try
        {
            Logger.Debug($"Player2 API streaming request: {BaseUrl}/chat/completions\n{jsonContent}");

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            using var webRequest = new UnityWebRequest($"{BaseUrl}/chat/completions", "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = streamingHandler;
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

            var asyncOperation = webRequest.SendWebRequest();

            while (!asyncOperation.isDone)
            {
                if (Current.Game == null) return null;
                await Task.Delay(100);
            }

            if (webRequest.responseCode == 429)
                throw new QuotaExceededException("Player2 quota exceeded");

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                Logger.Error($"Player2 streaming request failed: {webRequest.responseCode} - {webRequest.error}");
                throw new Exception($"Player2 API Error: {webRequest.error}");
            }

            var fullResponse = streamingHandler.GetFullText();
            var tokens = streamingHandler.GetTotalTokens();
            Logger.Debug($"Player2 streaming response completed. Tokens: {tokens}");
            return new Payload(jsonContent, fullResponse, tokens);
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in Player2 streaming request: {ex.Message}");
            throw;
        }
    }

    public async Task<Payload> GetChatCompletionAsync(string instruction,
        List<(Role role, string message)> messages)
    {
        await EnsureHealthCheck();
        
        var allMessages = BuildMessages(instruction, messages);
        var request = new Player2Request
        {
            Messages = allMessages,
            Stream = false
        };

        string jsonContent = JsonUtil.SerializeToJson(request);
        var response = await GetCompletionAsync(jsonContent);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;
        return new Payload(jsonContent, content, tokens);
    }

    private async Task<Player2Response> GetCompletionAsync(string jsonContent)
    {
        try
        {
            Logger.Debug($"Player2 API request: {BaseUrl}/chat/completions\n{jsonContent}");

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            using var webRequest = new UnityWebRequest($"{BaseUrl}/chat/completions", "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

            var asyncOperation = webRequest.SendWebRequest();

            while (!asyncOperation.isDone)
            {
                if (Current.Game == null) return null;
                await Task.Delay(100);
            }

            Logger.Debug($"Player2 API response: {webRequest.responseCode}\n{webRequest.downloadHandler.text}");

            if (webRequest.responseCode == 429)
                throw new QuotaExceededException("Player2 quota exceeded");

            if (webRequest.isNetworkError || webRequest.isHttpError)
            {
                Logger.Error($"Player2 request failed: {webRequest.responseCode} - {webRequest.error}");
                throw new Exception($"Player2 API Error: {webRequest.error}");
            }

            return JsonUtil.DeserializeFromJson<Player2Response>(webRequest.downloadHandler.text);
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in Player2 API request: {ex.Message}");
            throw;
        }
    }

    private List<Message> BuildMessages(string instruction, List<(Role role, string message)> messages)
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

        return allMessages;
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

    private async void StartHealthCheck()
    {
        while (_healthCheckActive && Current.Game != null)
        {
            try
            {
                await Task.Delay(60000);
                if (_healthCheckActive && !string.IsNullOrEmpty(_apiKey))
                {
                    await PerformHealthCheck();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Player2 health check error: {ex.Message}");
            }
        }
    }

    private async Task EnsureHealthCheck()
    {
        if (DateTime.Now.Subtract(_lastHealthCheck).TotalSeconds > 60)
        {
            await PerformHealthCheck();
        }
    }

    private async Task PerformHealthCheck()
    {
        if (string.IsNullOrEmpty(_apiKey)) return;

        try
        {
            using var webRequest = new UnityWebRequest($"{BaseUrl}/health", "GET");
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

            var asyncOperation = webRequest.SendWebRequest();

            while (!asyncOperation.isDone)
            {
                if (Current.Game == null) return;
                await Task.Delay(100);
            }

            _lastHealthCheck = DateTime.Now;

            if (webRequest.responseCode == 200)
            {
                Logger.Debug("Player2 health check successful");
            }
            else
            {
                Logger.Warning($"Player2 health check failed: {webRequest.responseCode} - {webRequest.error}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Player2 health check exception: {ex.Message}");
        }
    }

    public static void StopHealthCheck()
    {
        _healthCheckActive = false;
    }
}