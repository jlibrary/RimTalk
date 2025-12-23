using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.Client.Player2;

public class Player2Client : IAIClient
{
    private readonly string _apiKey;
    private readonly bool _isLocalConnection;
    private const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb";
    private const string BaseUrl = "https://api.player2.game/v1";
    private const string LocalUrl = "http://localhost:4315/v1";
    private static DateTime _lastHealthCheck = DateTime.MinValue;
    private static bool _healthCheckActive = false;

    /// <summary>
    /// Factory method that attempts local Player2 app detection before falling back to manual API key.
    /// This provides the best user experience by auto-detecting the Player2 desktop app when available.
    /// </summary>
    /// <param name="fallbackApiKey">Manual API key to use if local app is not detected</param>
    /// <returns>Configured Player2Client instance</returns>
    public static async Task<Player2Client> CreateAsync(string fallbackApiKey = null)
    {
        try
        {
            // 1. First attempt: Try to detect and use local Player2 app (zero-config experience)
            string localKey = await TryGetLocalPlayer2Key();
            if (!string.IsNullOrEmpty(localKey))
            {
                Logger.Debug("Player2 local app detected, using local connection");
                ShowPlayer2LocalDetectedNotification();
                return new Player2Client(localKey, isLocal: true);
            }

            // 2. Fallback: Use manually provided API key from settings
            if (!string.IsNullOrEmpty(fallbackApiKey))
            {
                Logger.Debug("Using manual Player2 API key");
                return new Player2Client(fallbackApiKey, isLocal: false);
            }

            // 3. Neither local app nor manual key available
            ShowPlayer2LocalNotFoundNotification();
            throw new Exception("Player2 not available: no local app running and no API key provided");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to create Player2 client: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Private constructor - use CreateAsync() instead for proper initialization
    /// </summary>
    private Player2Client(string apiKey, bool isLocal)
    {
        _apiKey = apiKey;
        _isLocalConnection = isLocal;
        
        // Only start health check for remote connections (local connections don't require periodic checks)
        if (!_healthCheckActive && !string.IsNullOrEmpty(apiKey) && !isLocal)
        {
            _healthCheckActive = true;
            StartHealthCheck();
        }
    }

    /// <summary>
    /// Shows notification when Player2 local app is detected and used automatically
    /// </summary>
    private static void ShowPlayer2LocalDetectedNotification()
    {
        try
        {
            // Schedule notification on main thread
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Messages.Message(
                        "RimTalk: Player2 desktop app detected! Using automatic authentication (no API key needed).",
                        MessageTypeDefOf.PositiveEvent
                    );
                    Logger.Message("RimTalk: ✓ Successfully connected to local Player2 app");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error showing Player2 detection notification: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Error scheduling Player2 detection notification: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows notification when Player2 local app is not found
    /// </summary>
    private static void ShowPlayer2LocalNotFoundNotification()
    {
        try
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    Messages.Message(
                        "RimTalk: Player2 desktop app not found. Please start Player2 app or add API key manually.",
                        MessageTypeDefOf.CautionInput
                    );
                    Logger.Message("RimTalk: Player2 local app not available, manual API key required");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Error showing Player2 not found notification: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Error scheduling Player2 not found notification: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to detect and authenticate with a local Player2 desktop app.
    /// This provides the optimal user experience as it requires no manual API key setup.
    /// </summary>
    /// <returns>API key from local app, or null if app is not running/available</returns>
    private static async Task<string> TryGetLocalPlayer2Key()
    {
        try
        {
            Logger.Debug("Checking for local Player2 app...");

            // First verify the app is running with a health check
            using (var healthRequest = UnityWebRequest.Get("http://localhost:4315/v1/health"))
            {
                healthRequest.timeout = 2; // 2 second timeout
                await SendWebRequestAsync(healthRequest);

                if (healthRequest.isNetworkError || healthRequest.isHttpError)
                {
                    Logger.Debug($"Player2 local app health check failed: {healthRequest.error}");
                    return null;
                }

                Logger.Debug("Player2 local app health check passed");
            }

            // If health check passed, get API key through local authentication
            string loginUrl = $"http://localhost:4315/v1/login/web/{GameClientId}";
            byte[] bodyRaw = "{}"u8.ToArray();

            using (var webRequest = new UnityWebRequest(loginUrl, "POST"))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.timeout = 3;

                await SendWebRequestAsync(webRequest);

                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    Logger.Debug($"Player2 local login failed: {webRequest.responseCode} - {webRequest.error}");
                    return null;
                }

                string responseText = webRequest.downloadHandler.text;
                var response = JsonUtil.DeserializeFromJson<LocalPlayer2Response>(responseText);

                if (!string.IsNullOrEmpty(response?.p2Key))
                {
                    Logger.Message("[Player2] ✓ Local app authenticated successfully");
                    return response.p2Key;
                }

                Logger.Warning("Player2 local app responded but no API key in response");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Local Player2 detection failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Helper to convert UnityWebRequestAsyncOperation to a Task for async/await
    /// </summary>
    private static Task SendWebRequestAsync(UnityWebRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        request.SendWebRequest().completed += _ => tcs.SetResult(true);
        return tcs.Task;
    }

    /// <summary>
    /// Returns the appropriate API base URL based on connection type
    /// </summary>
    private string GetApiUrl() => _isLocalConnection ? LocalUrl : BaseUrl;

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(string instruction,
        List<(Role role, string message)> messages, Action<T> onResponseParsed) where T : class
    {
        // Only perform health checks for remote connections (local connections don't need them)
        if (!_isLocalConnection)
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
            Logger.Debug($"Player2 API streaming request ({(_isLocalConnection ? "local" : "remote")}): {GetApiUrl()}/chat/completions\n{jsonContent}");

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            using var webRequest = new UnityWebRequest($"{GetApiUrl()}/chat/completions", "POST");
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
        // Only perform health checks for remote connections (local connections don't need them)
        if (!_isLocalConnection)
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
            Logger.Debug($"Player2 API request ({(_isLocalConnection ? "local" : "remote")}): {GetApiUrl()}/chat/completions\n{jsonContent}");

            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            using var webRequest = new UnityWebRequest($"{GetApiUrl()}/chat/completions", "POST");
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

    /// <summary>
    /// Health check functionality - only runs for remote connections as local connections don't require periodic monitoring
    /// </summary>
    private async void StartHealthCheck()
    {
        while (_healthCheckActive && Current.Game != null)
        {
            try
            {
                await Task.Delay(60000); // Check every 60 seconds as required by Player2 API
                if (_healthCheckActive && !string.IsNullOrEmpty(_apiKey) && !_isLocalConnection)
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
        if (string.IsNullOrEmpty(_apiKey) || _isLocalConnection) return;

        try
        {
            using var webRequest = new UnityWebRequest($"{GetApiUrl()}/health", "GET");
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
    
    public static void CheckPlayer2StatusAndNotify()
    {
        Task.Run(async () =>
        {
            try
            {
                bool isAvailable = await IsPlayer2LocalAppAvailableAsync();

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (isAvailable)
                    {
                        Messages.Message(
                            "RimTalk: Player2 desktop app detected and ready for use!",
                            MessageTypeDefOf.PositiveEvent
                        );
                        Logger.Message("RimTalk: Player2 desktop app status check - Available");
                    }
                    else
                    {
                        Messages.Message(
                            "RimTalk: Player2 desktop app not detected. Install and start Player2 app, or add API key manually.",
                            MessageTypeDefOf.CautionInput
                        );
                        Logger.Message("RimTalk: Player2 desktop app status check - Not available");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warning($"RimTalk: Error checking Player2 status: {ex.Message}");
            }
        });
    }

    private static async Task<bool> IsPlayer2LocalAppAvailableAsync()
    {
        try
        {
            using var webRequest = UnityWebRequest.Get("http://localhost:4315/v1/health");
            webRequest.timeout = 2;
            var asyncOperation = webRequest.SendWebRequest();

            var startTime = DateTime.Now;
            while (!asyncOperation.isDone)
            {
                if (DateTime.Now.Subtract(startTime).TotalSeconds > 2)
                    break;
                await Task.Delay(50);
            }

            return webRequest.responseCode == 200;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Data model for deserializing local Player2 app authentication response
/// </summary>
[System.Runtime.Serialization.DataContract]
public class LocalPlayer2Response
{
    [System.Runtime.Serialization.DataMember(Name = "p2Key")]
    public string p2Key { get; set; }
}