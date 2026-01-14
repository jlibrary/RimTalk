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
    private const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb";
    private const string LocalUrl = "http://localhost:4315";
    private static string RemoteUrl => AIProvider.Player2.GetEndpointUrl();

    private readonly string _apiKey;
    private readonly bool _isLocalConnection;
    private static DateTime _lastHealthCheck = DateTime.MinValue;
    private static bool _healthCheckActive;

    private string CurrentApiUrl => _isLocalConnection ? LocalUrl : RemoteUrl;

    private Player2Client(string apiKey, bool isLocal)
    {
        _apiKey = apiKey;
        _isLocalConnection = isLocal;

        if (!_healthCheckActive && !string.IsNullOrEmpty(apiKey) && !isLocal)
        {
            _healthCheckActive = true;
            StartHealthCheckLoop();
        }
    }

    public static async Task<Player2Client> CreateAsync(string fallbackApiKey = null)
    {
        try
        {
            string localKey = await TryGetLocalPlayer2Key();
            if (!string.IsNullOrEmpty(localKey))
            {
                Logger.Debug("Player2 local app detected.");
                ShowNotification("RimTalk.Player2.LocalDetected", MessageTypeDefOf.PositiveEvent);
                return new Player2Client(localKey, isLocal: true);
            }

            if (!string.IsNullOrEmpty(fallbackApiKey))
            {
                Logger.Debug("Using manual Player2 API key.");
                return new Player2Client(fallbackApiKey, isLocal: false);
            }

            ShowNotification("RimTalk.Player2.LocalNotFound", MessageTypeDefOf.CautionInput);
            throw new Exception("Player2 not available: no local app and no API key.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to create Player2 client: {ex.Message}");
            throw;
        }
    }

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages, List<(Role role, string message)> messages)
    {
        await EnsureHealthCheck();

        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: false);
        string responseText = await SendRequestAsync($"{CurrentApiUrl}/v1/chat/completions", jsonContent,
            new DownloadHandlerBuffer());

        var response = JsonUtil.DeserializeFromJson<Player2Response>(responseText);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;

        return new Payload(CurrentApiUrl, null, jsonContent, content, tokens);
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages, Action<T> onResponseParsed) where T : class
    {
        await EnsureHealthCheck();

        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: true);
        var jsonParser = new JsonStreamParser<T>();

        var streamHandler = new Player2StreamHandler(chunk =>
        {
            foreach (var item in jsonParser.Parse(chunk))
                onResponseParsed?.Invoke(item);
        });

        await SendRequestAsync($"{CurrentApiUrl}/v1/chat/completions", jsonContent, streamHandler);

        return new Payload(CurrentApiUrl, null, jsonContent, streamHandler.GetFullText(),
            streamHandler.GetTotalTokens());
    }

    private string BuildRequestJson(List<(Role role, string message)> prefixMessages, List<(Role role, string message)> messages, bool stream)
    {
        var allMessages = new List<Message>();
        
        // Add prefix messages with their original roles
        if (prefixMessages != null)
        {
            allMessages.AddRange(prefixMessages.Select(m => new Message
            {
                Role = RoleToString(m.role),
                Content = m.message
            }));
        }

        // Add conversation messages
        allMessages.AddRange(messages.Select(m => new Message
        {
            Role = RoleToString(m.role),
            Content = m.message
        }));

        return JsonUtil.SerializeToJson(new Player2Request
        {
            Messages = allMessages,
            Stream = stream
        });
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

    private async Task<string> SendRequestAsync(string url, string jsonContent, DownloadHandler downloadHandler)
    {
        Logger.Debug($"Player2 Request ({(_isLocalConnection ? "local" : "remote")}): {url}\n{jsonContent}");

        using var webRequest = new UnityWebRequest(url, "POST");
        webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonContent));
        webRequest.downloadHandler = downloadHandler;
        webRequest.SetRequestHeader("Content-Type", "application/json");
        webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
        webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

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

        if (downloadHandler is Player2StreamHandler sHandler)
        {
            sHandler.Flush();

            if (!string.IsNullOrEmpty(sHandler.DetectedError))
            {
                string errorMsg = sHandler.DetectedError;
                string allText = sHandler.GetAllReceivedText();

                if (errorMsg.Contains("ResourceExhausted") || errorMsg.Contains("Insufficient"))
                {
                    throw new QuotaExceededException("Player2 quota exceeded",
                        new Payload(url, null, jsonContent, allText, 0, errorMsg));
                }

                throw new AIRequestException(errorMsg, new Payload(url, null, jsonContent, allText, 0, errorMsg));
            }
        }

        string responseText = downloadHandler.text;

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? webRequest.error;
            Logger.Error($"Player2 failed: {webRequest.responseCode} - {errorMsg}");
            throw new AIRequestException(errorMsg, new Payload(url, null, jsonContent, responseText, 0, errorMsg));
        }

        if (downloadHandler is DownloadHandlerBuffer)
            Logger.Debug($"Player2 Response: \n{responseText}");
        else if (downloadHandler is Player2StreamHandler sh)
            Logger.Debug($"Player2 Streaming complete. Tokens: {sh.GetTotalTokens()}");

        return responseText;
    }

    // --- Static / Connection Helpers ---

    private static async Task<string> TryGetLocalPlayer2Key()
    {
        try
        {
            Logger.Debug("Checking for local Player2 app...");
            // Health check
            using (var healthRequest = UnityWebRequest.Get($"{LocalUrl}/v1/health"))
            {
                healthRequest.timeout = 2;
                await SendWebRequestAsync(healthRequest);
                if (healthRequest.isNetworkError || healthRequest.isHttpError)
                {
                    Logger.Debug($"Player2 local app health check failed: {healthRequest.error}");
                    return null;
                }

                Logger.Debug("Player2 local app health check passed");
            }

            // Login
            using (var loginRequest = new UnityWebRequest($"{LocalUrl}/v1/login/web/{GameClientId}", "POST"))
            {
                loginRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                loginRequest.downloadHandler = new DownloadHandlerBuffer();
                loginRequest.SetRequestHeader("Content-Type", "application/json");
                loginRequest.timeout = 3;

                await SendWebRequestAsync(loginRequest);
                if (loginRequest.isNetworkError || loginRequest.isHttpError)
                {
                    Logger.Debug($"Player2 local login failed: {loginRequest.responseCode} - {loginRequest.error}");
                    return null;
                }

                var response = JsonUtil.DeserializeFromJson<LocalPlayer2Response>(loginRequest.downloadHandler.text);
                if (!string.IsNullOrEmpty(response?.P2Key))
                {
                    Logger.Message("[Player2] ✓ Local app authenticated successfully");
                    return response.P2Key;
                }

                Logger.Warning("Player2 local app responded but no API key in response");
                return null;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Local Player2 detection failed: {ex.Message}");
            return null;
        }
    }

    private static Task SendWebRequestAsync(UnityWebRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        request.SendWebRequest().completed += _ => tcs.SetResult(true);
        return tcs.Task;
    }

    private static void ShowNotification(string messageKey, MessageTypeDef type)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                bool isDetected = messageKey == "RimTalk.Player2.LocalDetected";
                string text = isDetected
                    ? "RimTalk: Player2 desktop app detected! Using automatic authentication (no API key needed)."
                    : "RimTalk: Player2 desktop app not found. Please start app or add API key manually.";

                Messages.Message(text, type);
                Logger.Message(isDetected
                    ? "RimTalk: ✓ Successfully connected to local Player2 app"
                    : "RimTalk: Player2 local app not available, manual API key required");
            }
            catch
            {
                /* Ignore UI errors */
            }
        });
    }

    // --- Health Check Logic ---

    private async void StartHealthCheckLoop()
    {
        while (_healthCheckActive && Current.Game != null)
        {
            await Task.Delay(60000);
            if (_healthCheckActive) await EnsureHealthCheck(force: true);
        }
    }

    private async Task EnsureHealthCheck(bool force = false)
    {
        if (_isLocalConnection || string.IsNullOrEmpty(_apiKey)) return;
        if (!force && (DateTime.Now - _lastHealthCheck).TotalSeconds < 60) return;

        try
        {
            using var webRequest = new UnityWebRequest($"{RemoteUrl}/v1/health", "GET");
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            webRequest.SetRequestHeader("X-Game-Client-Id", GameClientId);

            var asyncOp = webRequest.SendWebRequest();
            while (!asyncOp.isDone)
            {
                if (Current.Game == null) return;
                await Task.Delay(100);
            }

            _lastHealthCheck = DateTime.Now;
            if (webRequest.responseCode == 200)
                Logger.Debug("Player2 health check successful");
            else
                Logger.Warning($"Player2 health check failed: {webRequest.responseCode} - {webRequest.error}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Player2 health check exception: {ex.Message}");
        }
    }

    public static void StopHealthCheck() => _healthCheckActive = false;

    public static void CheckPlayer2StatusAndNotify()
    {
        Task.Run(async () =>
        {
            bool isAvailable = await IsPlayer2LocalAppAvailableAsync();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (isAvailable)
                    Messages.Message("RimTalk: Player2 desktop app detected!", MessageTypeDefOf.PositiveEvent);
                else
                    Messages.Message("RimTalk: Player2 desktop app not detected.", MessageTypeDefOf.CautionInput);
            });
        });
    }

    private static async Task<bool> IsPlayer2LocalAppAvailableAsync()
    {
        try
        {
            using var webRequest = UnityWebRequest.Get($"{LocalUrl}/v1/health");
            webRequest.timeout = 2;
            await SendWebRequestAsync(webRequest);
            return webRequest.responseCode == 200;
        }
        catch
        {
            {
                return false;
            }
        }
    }
}

[System.Runtime.Serialization.DataContract]
public class LocalPlayer2Response
{
    [System.Runtime.Serialization.DataMember(Name = "p2Key")]
    public string P2Key { get; set; }
}