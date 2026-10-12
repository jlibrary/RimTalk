using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
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

        if (!_healthCheckActive)
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
        return await GetChatCompletionAsync(prefixMessages, messages, null, onRequestPrepared);
    }

    public async Task<Payload> GetChatCompletionAsync(List<(Role role, string message)> prefixMessages, 
        List<(Role role, string message)> messages, 
        string imageBase64,
        Action<Payload> onRequestPrepared = null)
    {
        string jsonContent = BuildRequestJson(prefixMessages, messages, stream: false, imageBase64: imageBase64);
        onRequestPrepared?.Invoke(new Payload(CurrentApiUrl, CurrentModelName, jsonContent, null, 0));
        string responseText = await SendRequestAsync($"{CurrentApiUrl}/v1/chat/completions", jsonContent,
            () => new DownloadHandlerBuffer());

        var response = JsonUtil.DeserializeFromJson<Player2Response>(responseText);
        var content = response?.Choices?[0]?.Message?.Content;
        var tokens = response?.Usage?.TotalTokens ?? 0;

        return new Payload(CurrentApiUrl, CurrentModelName, jsonContent, content, tokens)
        {
            StatusCode = 200
        };
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
        onRequestPrepared?.Invoke(new Payload(CurrentApiUrl, CurrentModelName, jsonContent, null, 0));
        var jsonParser = new JsonStreamParser<T>();
        var streamHandler = new Player2StreamHandler(chunk =>
        {
            foreach (var item in jsonParser.Parse(chunk))
                onResponseParsed?.Invoke(item);
        });

        await SendHttpAsync($"{CurrentApiUrl}/v1/chat/completions", jsonContent, streamHandler);

        return new Payload(CurrentApiUrl, CurrentModelName, jsonContent, streamHandler.GetFullText(),
            streamHandler.GetTotalTokens())
        {
            StatusCode = 200
        };
    }

    private string BuildRequestJson(List<(Role role, string message)> prefixMessages, List<(Role role, string message)> messages, bool stream, string imageBase64 = null)
    {
        if (!string.IsNullOrEmpty(AIService.CurrentRequest?.RawJsonOverride))
            return AIService.CurrentRequest.RawJsonOverride;

        var request = new ChatRequest
        {
            Stream = stream,
            Messages = BuildMessages(prefixMessages, messages, imageBase64)
        };

        string baseJson = JsonUtil.SerializeJsonValue(request.ToPayload());

        if (!string.IsNullOrWhiteSpace(_customRequestJson))
        {
            return JsonUtil.MergeJson(baseJson, _customRequestJson);
        }

        return baseJson;
    }

    private static List<ChatMessage> BuildMessages(List<(Role role, string message)> prefixMessages, List<(Role role, string message)> messages, string imageBase64)
    {
        var rawMessages = new List<(Role role, string message)>();
        if (prefixMessages != null) rawMessages.AddRange(prefixMessages);
        if (messages != null) rawMessages.AddRange(messages);

        var merged = new List<ChatMessage>();
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

    // handlerFactory is unused; the parameter stays so addon patches on this signature keep working.
    private Task<string> SendRequestAsync(string url, string jsonContent, Func<DownloadHandler> handlerFactory) =>
        SendHttpAsync(url, jsonContent, null);

    private async Task<string> SendHttpAsync(string url, string jsonContent, Player2StreamHandler streamHandler)
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

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {CurrentApiKey}");
        request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);

        const float connectTimeout = 60f;
        const float readTimeout = 60f;

        using var timeoutCts = new CancellationTokenSource();
        HttpResponseMessage response;
        try
        {
            response = await HttpUtil.SendWithTimeoutAsync(request, connectTimeout, timeoutCts,
                streamHandler != null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
        }
        catch (HttpRequestException) when (_isLocalConnection && !string.IsNullOrEmpty(_fallbackApiKey))
        {
            return await FallbackToRemoteAsync(jsonContent, streamHandler);
        }
        catch (HttpRequestException) when (_isLocalConnection)
        {
            const string errorMsg = "Player2 desktop app is not running.";
            throw new AIRequestException(errorMsg,
                new Payload(url, CurrentModelName, jsonContent, null, 0, errorMsg));
        }

        if (response == null) return null;
        using (response)
        {
            int statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                string errorText = await response.Content.ReadAsStringAsync();
                string errorMsg = ErrorUtil.ExtractErrorMessage(errorText) ?? response.ReasonPhrase;
                Logger.Warning($"Player2 failed: {statusCode} - {errorMsg}");
                throw new AIRequestException(errorMsg,
                    new Payload(url, CurrentModelName, jsonContent, errorText, 0, errorMsg) { StatusCode = statusCode });
            }

            if (streamHandler == null)
            {
                string responseText = await response.Content.ReadAsStringAsync();
                Logger.Debug($"Player2 Response: \n{responseText}");
                return responseText;
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            await HttpUtil.ReadStreamWithTimeoutAsync(stream, readTimeout, timeoutCts, (buf, len) =>
            {
                streamHandler.FeedBytes(buf, len);
                ThrowIfStreamError(streamHandler, url, jsonContent, statusCode);
            });

            streamHandler.Flush();
            ThrowIfStreamError(streamHandler, url, jsonContent, statusCode);
            Logger.Debug($"Player2 Streaming complete. Tokens: {streamHandler.GetTotalTokens()}");
            return streamHandler.GetFullText();
        }
    }

    private Task<string> FallbackToRemoteAsync(string jsonContent, Player2StreamHandler streamHandler)
    {
        Logger.Message("[Player2] Local app disconnected. Falling back to Web API.");
        _isLocalConnection = false;
        _localApiKey = null;
        return SendHttpAsync($"{RemoteUrl}/v1/chat/completions", jsonContent, streamHandler);
    }

    private void ThrowIfStreamError(Player2StreamHandler streamHandler, string url, string jsonContent, int statusCode)
    {
        if (string.IsNullOrEmpty(streamHandler.DetectedError)) return;

        string errorMsg = streamHandler.DetectedError;
        var payload = new Payload(url, CurrentModelName, jsonContent, streamHandler.GetAllReceivedText(), 0, errorMsg) { StatusCode = statusCode };
        if (errorMsg.Contains("ResourceExhausted") || errorMsg.Contains("Insufficient"))
            throw new QuotaExceededException("Player2 quota exceeded", payload);
        throw new AIRequestException(errorMsg, payload);
    }

    // --- Static / Connection Helpers ---

    private static async Task<string> TryGetLocalPlayer2Key()
    {
        try
        {
            Logger.Debug("Checking for local Player2 app...");
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            using (var health = await HttpUtil.Client.GetAsync($"{LocalUrl}/v1/health", cts.Token))
            {
                if (!health.IsSuccessStatusCode)
                {
                    Logger.Debug($"Player2 local app health check failed: {(int)health.StatusCode}");
                    return null;
                }

                Logger.Debug("Player2 local app health check passed");
            }

            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"{LocalUrl}/v1/login/web/{GameClientId}");
            loginRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var loginCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var login = await HttpUtil.Client.SendAsync(loginRequest, loginCts.Token);
            if (!login.IsSuccessStatusCode)
            {
                Logger.Debug($"Player2 local login failed: {(int)login.StatusCode} - {login.ReasonPhrase}");
                return null;
            }

            var response = JsonUtil.DeserializeFromJson<LocalPlayer2Response>(await login.Content.ReadAsStringAsync());
            if (!string.IsNullOrEmpty(response?.P2Key))
            {
                Logger.Message("[Player2] ✓ Local app authenticated successfully");
                return response.P2Key;
            }

            Logger.Warning("Player2 local app responded but no API key in response");
            return null;
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
        AIErrorHandler.EnqueueMessage(() =>
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
        while (_healthCheckActive)
        {
            if (Current.Game != null)
            {
                await SendHealthCheckAsync();
                await Task.Delay(60000);
            }
            else
            {
                await Task.Delay(5000);
            }
        }
    }

    private async Task SendHealthCheckAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{CurrentApiUrl}/v1/health");
            request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);
            if (!string.IsNullOrEmpty(CurrentApiKey))
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {CurrentApiKey}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await HttpUtil.Client.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
                Logger.Debug("Player2 health check successful");
            else
                Logger.Warning($"Player2 health check failed: {(int)response.StatusCode} - {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Player2 health check exception: {ex.Message}");
        }
    }

    public static void StopHealthCheck() => _healthCheckActive = false;
    
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await HttpUtil.Client.GetAsync($"{LocalUrl}/v1/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

[DataContract]
public class LocalPlayer2Response
{
    [DataMember(Name = "p2Key")]
    public string P2Key { get; set; }
}
