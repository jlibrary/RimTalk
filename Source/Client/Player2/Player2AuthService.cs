using System;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Client.Player2;

[DataContract]
internal class Player2DeviceCodeResponse
{
    [DataMember(Name = "deviceCode")]
    public string DeviceCode { get; set; }

    [DataMember(Name = "expiresIn")]
    public int ExpiresIn { get; set; }

    [DataMember(Name = "interval")]
    public int Interval { get; set; } = 5;

    [DataMember(Name = "userCode")]
    public string UserCode { get; set; }

    [DataMember(Name = "verificationUri")]
    public string VerificationUri { get; set; }

    [DataMember(Name = "verificationUriComplete")]
    public string VerificationUriComplete { get; set; }
}

[DataContract]
internal class Player2TokenResponse
{
    [DataMember(Name = "p2Key")]
    public string P2Key { get; set; }
}

public static class Player2AuthService
{
    private const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb";
    private const string DeviceNewEndpoint = "https://api.player2.game/v1/login/device/new";
    private const string DeviceTokenEndpoint = "https://api.player2.game/v1/login/device/token";

    public static bool IsAuthenticating { get; private set; }
    public static string ApprovalUrl { get; private set; }

    private static CancellationTokenSource _authCts;

    public static void StartAuth(Action<string> onKeyReceived = null)
    {
        if (IsAuthenticating)
        {
            if (!string.IsNullOrEmpty(ApprovalUrl))
            {
                Application.OpenURL(ApprovalUrl);
            }
            return;
        }

        Cancel();
        _authCts = new CancellationTokenSource();
        IsAuthenticating = true;

        Task.Run(async () =>
        {
            try
            {
                await RunDeviceAuthFlowAsync(_authCts.Token, onKeyReceived);
            }
            catch (OperationCanceledException)
            {
                Logger.Message("[Player2] Device authorization cancelled.");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Player2] Device authorization failed: {ex.Message}");
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    Messages.Message("RimTalk.Settings.Player2AuthFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                });
            }
            finally
            {
                IsAuthenticating = false;
                ApprovalUrl = null;
            }
        });
    }

    public static void Cancel()
    {
        if (_authCts != null)
        {
            try
            {
                _authCts.Cancel();
                _authCts.Dispose();
            }
            catch
            {
                // Ignored
            }
            _authCts = null;
        }
        IsAuthenticating = false;
        ApprovalUrl = null;
    }

    private static async Task RunDeviceAuthFlowAsync(CancellationToken cancellationToken, Action<string> onKeyReceived = null)
    {
        Logger.Message("[Player2] Requesting device authorization code...");

        string newReqBody = $"{{\"client_id\":\"{GameClientId}\"}}";
        var (codeStatus, codeText) = await SendPostAsync(DeviceNewEndpoint, newReqBody, 10);

        if (codeStatus != 200 || string.IsNullOrEmpty(codeText))
        {
            throw new Exception($"Failed to obtain device code (HTTP {codeStatus})");
        }

        var deviceCodeResp = JsonUtil.DeserializeFromJson<Player2DeviceCodeResponse>(codeText);
        if (string.IsNullOrEmpty(deviceCodeResp?.DeviceCode) || string.IsNullOrEmpty(deviceCodeResp.VerificationUriComplete))
        {
            throw new Exception("Invalid response from Player2 device code endpoint");
        }

        ApprovalUrl = deviceCodeResp.VerificationUriComplete;

        LongEventHandler.ExecuteWhenFinished(() =>
        {
            Application.OpenURL(ApprovalUrl);
            Messages.Message("RimTalk.Settings.Player2AuthWaitingNotice".Translate(), MessageTypeDefOf.NeutralEvent, false);
        });

        int pollIntervalSeconds = Math.Max(3, deviceCodeResp.Interval);
        int maxPollSeconds = deviceCodeResp.ExpiresIn > 0 ? deviceCodeResp.ExpiresIn : 600;
        int elapsedSeconds = 0;

        string tokenReqBody = $"{{\"client_id\":\"{GameClientId}\",\"device_code\":\"{deviceCodeResp.DeviceCode}\",\"grant_type\":\"urn:ietf:params:oauth:grant-type:device_code\"}}";

        while (!cancellationToken.IsCancellationRequested && elapsedSeconds < maxPollSeconds)
        {
            await Task.Delay(pollIntervalSeconds * 1000, cancellationToken);
            elapsedSeconds += pollIntervalSeconds;

            if (cancellationToken.IsCancellationRequested) break;

            var (tokenStatus, tokenText) = await SendPostAsync(DeviceTokenEndpoint, tokenReqBody, 10);

            if (tokenStatus == 200 && !string.IsNullOrEmpty(tokenText))
            {
                var tokenResp = JsonUtil.DeserializeFromJson<Player2TokenResponse>(tokenText);
                if (!string.IsNullOrEmpty(tokenResp?.P2Key))
                {
                    string key = tokenResp.P2Key;
                    Logger.Message("[Player2] ✓ API key successfully obtained");
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (onKeyReceived != null)
                        {
                            onKeyReceived(key);
                        }
                        else
                        {
                            var settings = global::RimTalk.Settings.Get();
                            settings.SimplePlayer2ApiKey = key;
                            settings.Write();
                        }
                        Messages.Message("RimTalk.Settings.Player2AuthSuccess".Translate(), MessageTypeDefOf.PositiveEvent, false);
                    });
                    return;
                }
            }
            else if (tokenStatus == 400)
            {
                Logger.Debug("[Player2] Waiting for user approval in browser...");
            }
            else
            {
                throw new Exception($"Token polling failed with status {tokenStatus}");
            }
        }

        if (elapsedSeconds >= maxPollSeconds)
        {
            throw new Exception("Authentication timed out. Please try again.");
        }
    }

    private static async Task<(long statusCode, string text)> SendPostAsync(string url, string jsonBody, int timeoutSeconds)
    {
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = timeoutSeconds;

        var tcs = new TaskCompletionSource<bool>();
        request.SendWebRequest().completed += _ => tcs.SetResult(true);
        await tcs.Task;

        return (request.responseCode, request.downloadHandler.text);
    }
}
