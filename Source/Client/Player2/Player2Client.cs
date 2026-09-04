using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Service;
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

    private string _fallbackApiKey;
    private string _localApiKey;
    private bool _isLocalConnection;
    private readonly string _customRequestJson;
    private static DateTime _lastHealthCheck = DateTime.MinValue;
    private static bool _healthCheckActive;
    private DateTime _lastLocalProbe = DateTime.MinValue;

    private string CurrentApiUrl => _isLocalConnection ? LocalUrl : RemoteUrl;
    private string CurrentApiKey => _isLocalConnection ? _localApiKey : _fallbackApiKey;
    private string CurrentModelName => _isLocalConnection ? "Player2 Desktop App" : "Player2 Web API";

    public void SetFallbackApiKey(string key)
    {
        _fallbackApiKey = key;
    }

    private Player2Client(string localKey, string fallbackApiKey, bool isLocal, string customRequestJson = null)
    {
        _localApiKey = localKey;
        _fallbackApiKey = fallbackApiKey;
        _isLocalConnection = isLocal;
        _customRequestJson = customRequestJson;

        if (!_healthCheckActive && !string.IsNullOrEmpty(fallbackApiKey))
        {
            _healthCheckActive = true;
            StartHealthCheckLoop();
        }
    }

    public static async Task<Player2Client> CreateAsync(string fallbackApiKey = null, string customRequestJson = null)
    {
        try
        {
            string localKey = await TryGetLocalPlayer2Key();
            bool hasLocal = !string.IsNullOrEmpty(localKey);
            bool hasFallback = !string.IsNullOrEmpty(fallbackApiKey);

            if (!hasLocal && !hasFallback)
            {
                throw new Exception("Player2 desktop app not found. Please start the app or enter an API key.");
            }

            if (hasLocal)
            {
                Logger.Debug("Player2 local app detected.");
                ShowNotification("RimTalk.Player2.LocalDetected", MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Logger.Debug("Using manual Player2 API key.");
            }

            return new Player2Client(localKey, fallbackApiKey, isLocal: hasLocal, customRequestJson: customRequestJson);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to create Player2 client: {ex.Message}");
            throw;
        }
    }

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages, 
        List<(Role role, string message)> messages, 
        Action<Payload> onRequestPrepared = null)
    {
        await EnsureHealthCheck();

        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: false);
        onRequestPrepared?.Invoke(new Payload(CurrentApiUrl, CurrentModelName, jsonContent, null, 0));
        string responseText = await SendRequestAsync($"{CurrentApiUrl}/v1/chat/completions", jsonContent,
            () => new DownloadHandlerBuffer());

        var response = JsonUtil.DeserializeFromJson<Player2Response>(responseText);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;

        return new Payload(CurrentApiUrl, CurrentModelName, jsonContent, content, tokens);
    }

    public async Task<Payload> GetStreamingChatCompletionAsync<T>(List<(Role role, string message)> prefixMessages,
        List<(Role role, string message)> messages, 
        Action<T> onResponseParsed,
        Action<Payload> onRequestPrepared = null) where T : class
    {
        await EnsureHealthCheck();

        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: true);
        onRequestPrepared?.Invoke(new Payload(CurrentApiUrl, CurrentModelName, jsonContent, null, 0));
        var jsonParser = new JsonStreamParser<T>();
        Player2StreamHandler streamHandler = null;

        await SendRequestAsync($"{CurrentApiUrl}/v1/chat/completions", jsonContent, () =>
        {
            streamHandler = new Player2StreamHandler(chunk =>
            {
                foreach (var item in jsonParser.Parse(chunk))
                    onResponseParsed?.Invoke(item);
            });
            return streamHandler;
        });

        return new Payload(CurrentApiUrl, CurrentModelName, jsonContent, streamHandler?.GetFullText(),
            streamHandler?.GetTotalTokens() ?? 0);
    }

    private string BuildRequestJson(List<(Role role, string message)> prefixMessages, List<(Role role, string message)> messages, bool stream)
    {
        var rawMessages = new List<(Role role, string message)>();
        if (prefixMessages != null) rawMessages.AddRange(prefixMessages);
        if (messages != null) rawMessages.AddRange(messages);

        var mergedMessages = new List<Message>();
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

        string baseJson = JsonUtil.SerializeToJson(new Player2Request
        {
            Messages = mergedMessages,
            Stream = stream
        });

        if (!string.IsNullOrWhiteSpace(_customRequestJson))
        {
            return JsonUtil.MergeJson(baseJson, _customRequestJson);
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

    private async Task<string> SendRequestAsync(string url, string jsonContent, Func<DownloadHandler> handlerFactory)
    {
        if (!_isLocalConnection && (DateTime.Now - _lastLocalProbe).TotalSeconds > 2)
        {
            _lastLocalProbe = DateTime.Now;
            string key = await TryGetLocalPlayer2Key();
            if (!string.IsNullOrEmpty(key))
            {
                _localApiKey = key;
                _isLocalConnection = true;
                url = $"{LocalUrl}/v1/chat/completions";
            }
        }

        Logger.Debug($"Player2 Request ({(_isLocalConnection ? "local" : "remote")}): {url}\n{jsonContent}");

        using var downloadHandler = handlerFactory();
        using var webRequest = new UnityWebRequest(url, "POST");
        webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonContent));
        webRequest.downloadHandler = downloadHandler;
        webRequest.SetRequestHeader("Content-Type", "application/json");
        webRequest.SetRequestHeader("Authorization", $"Bearer {CurrentApiKey}");
        webRequest.SetRequestHeader("player2-game-key", GameClientId);

        var asyncOp = webRequest.SendWebRequest();

        float inactivityTimer = 0f;
        ulong lastBytes = 0;
        const float connectTimeout = 60f;
        const float readTimeout = 60f;

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
                        new Payload(url, CurrentModelName, jsonContent, allText, 0, errorMsg));
                }

                throw new AIRequestException(errorMsg, new Payload(url, CurrentModelName, jsonContent, allText, 0, errorMsg));
            }
        }

        string responseText = downloadHandler.text;

        if (webRequest.isNetworkError || webRequest.isHttpError)
        {
            if (_isLocalConnection && !string.IsNullOrEmpty(_fallbackApiKey) && (webRequest.isNetworkError || webRequest.responseCode == 0))
            {
                Logger.Message("[Player2] Local app disconnected. Falling back to Web API.");
                _isLocalConnection = false;
                _localApiKey = null;
                return await SendRequestAsync($"{RemoteUrl}/v1/chat/completions", jsonContent, handlerFactory);
            }

            string errorMsg = ErrorUtil.ExtractErrorMessage(responseText) ?? webRequest.error;
            if (_isLocalConnection && (webRequest.isNetworkError || webRequest.responseCode == 0))
            {
                errorMsg = "Player2 desktop app is not running.";
            }

            Logger.Warning($"Player2 failed: {webRequest.responseCode} - {errorMsg}");
            throw new AIRequestException(errorMsg, new Payload(url, CurrentModelName, jsonContent, responseText, 0, errorMsg));
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
                if (!isDetected)
                {
                    Messages.Message("RimTalk: Player2 desktop app not found. Please start app or add API key manually.", type);
                }

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
        if (_isLocalConnection || string.IsNullOrEmpty(_fallbackApiKey)) return;
        if (!force && (DateTime.Now - _lastHealthCheck).TotalSeconds < 60) return;

        try
        {
            using var webRequest = new UnityWebRequest($"{RemoteUrl}/v1/health", "GET");
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Authorization", $"Bearer {_fallbackApiKey}");
            webRequest.SetRequestHeader("player2-game-key", GameClientId);

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

    private static bool? _lastLocalAppDetected;
    private static DateTime _lastLocalAppCheckTime = DateTime.MinValue;
    private static bool _isCheckingLocalApp;

    public static bool? GetLocalAppStatusCached()
    {
        if (!_isCheckingLocalApp && (DateTime.Now - _lastLocalAppCheckTime).TotalSeconds > 3)
        {
            _isCheckingLocalApp = true;
            Task.Run(async () =>
            {
                try
                {
                    bool available = await IsPlayer2LocalAppAvailableAsync();
                    _lastLocalAppDetected = available;
                }
                catch
                {
                    _lastLocalAppDetected = false;
                }
                finally
                {
                    _lastLocalAppCheckTime = DateTime.Now;
                    _isCheckingLocalApp = false;
                }
            });
        }
        return _lastLocalAppDetected;
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
            return false;
        }
    }
}

[System.Runtime.Serialization.DataContract]
public class LocalPlayer2Response
{
    [System.Runtime.Serialization.DataMember(Name = "p2Key")]
    public string P2Key { get; set; }
}
