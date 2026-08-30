using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Service;

// WARNING:
// This class defines core logic and has a significant impact on system behavior.
// In most cases, you should NOT modify this file.
public static class AIService
{
    // volatile: written in an async finally on a threadpool thread, read on the main thread every
    // tick. Without it the main thread can cache a stale `true` and dialogue stops silently for good.
    private static volatile bool _busy;
    private static DateTime? _busySince;
    private static bool _firstInstruction = true;
    private static System.Threading.CancellationTokenSource _currentCts;
    private static TalkRequest _currentRequest;

    /// <summary>
    /// Streaming chat that invokes callback as each player's dialogue is parsed
    /// </summary>
    public static async Task ChatStreaming(TalkRequest request, Action<TalkResponse> onPlayerResponseReceived)
    {
        _currentRequest = request;
        var prefixMessages = request.PromptMessages ?? [];
        var apiLog = ApiHistory.AddRequest(request, Channel.Stream);
        var lastApiLog = apiLog;

        var payload = await ExecuteWithRetry(apiLog, async client =>
        {
            // All prompt messages are already in prefixMessages, pass empty list for messages
            return await client.GetStreamingChatCompletionAsync<TalkResponse>(prefixMessages, [],
                response =>
                {
                    if (IsCancellationRequested()) return;
                    var pawnState = request.ResolvePawnState(response.Name);
                    if (pawnState == null) return; 
                    
                    response.TalkType = request.TalkType;

                    // Calculate timing relative to the correct previous log
                    int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                    if (lastApiLog == apiLog) elapsedMs -= lastApiLog.ElapsedMs;

                    var newLog = ApiHistory.AddResponse(apiLog.Id, response.Text, response.Name,
                        response.InteractionRaw, payload: null, elapsedMs: elapsedMs,
                        targetName: response.TargetName);
                    
                    response.Id = newLog.Id;
                    lastApiLog = newLog;

                    onPlayerResponseReceived?.Invoke(response);
                },
                prep => ApiHistory.UpdatePayload(apiLog.Id, prep));
        });

        HandleFinalStatus(apiLog, payload);
        _firstInstruction = false;
    }

    // One time query - used for generating persona, etc
    public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
    {
        _currentRequest = request;
        var messages = new List<(Role role, string message)> { (Role.User, request.Prompt) };
        var prefixMessages = new List<(Role role, string message)> { (Role.System, request.Context) };
        var apiLog = ApiHistory.AddRequest(request, Channel.Query);

        var payload = await ExecuteWithRetry(apiLog, async client =>
            await client.GetChatCompletionAsync(prefixMessages, messages, prep => ApiHistory.UpdatePayload(apiLog.Id, prep)));

        if (string.IsNullOrEmpty(payload.Response) || !string.IsNullOrEmpty(payload.ErrorMessage))
        {
            ApiHistory.UpdatePayload(apiLog.Id, payload);
            return null;
        }

        try
        {
            var data = JsonUtil.DeserializeFromJson<T>(payload.Response);
            ApiHistory.AddResponse(apiLog.Id, data.GetText(), null, null, payload: payload);
            return data;
        }
        catch (Exception)
        {
            ReportError(apiLog, payload, "Json Deserialization Failed");
            return null;
        }
    }

    private static async Task<Payload> ExecuteWithRetry(ApiLog apiLog, Func<IAIClient, Task<Payload>> action)
    {
        _busy = true;
        _busySince = DateTime.Now;
        _currentCts = new System.Threading.CancellationTokenSource();
        try
        {
            Exception capturedEx = null;
            
            var payload = await AIErrorHandler.HandleWithRetry(async () =>
            {
                var client = await AIClientFactory.GetAIClientAsync();
                return await action(client);
            }, ex =>
            {
                capturedEx = ex;
                apiLog.Response = ex.Message;
                apiLog.IsError = true;
            });

            // Handle failure case where we need to reconstruct a payload from the exception
            if (payload == null)
            {
                payload = capturedEx is AIRequestException { Payload: not null } rex 
                    ? rex.Payload 
                    : new Payload("Unknown", "Unknown", "", null, 0, capturedEx?.Message ?? "Unknown Error");
            }
            else
            {
                Stats.IncrementCalls();
                Stats.IncrementTokens(payload.TokenCount);
            }

            return payload;
        }
        catch (OperationCanceledException)
        {
            apiLog.Response = "RimTalk.DebugWindow.Canceled".Translate();
            apiLog.SpokenTick = -1;
            return new Payload("Canceled", "Canceled", "", null, 0, "Canceled");
        }
        finally
        {
            _busy = false;
            _busySince = null;
            _currentRequest = null;
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    private static void HandleFinalStatus(ApiLog apiLog, Payload payload)
    {
        // If response is empty but no explicit error yet, mark as deserialization failure (or empty response)
        if (string.IsNullOrEmpty(apiLog.Response) && !apiLog.IsError && string.IsNullOrEmpty(payload.ErrorMessage))
        {
            ReportError(apiLog, payload, "Json Deserialization Failed");
            return;
        }
        
        ApiHistory.UpdatePayload(apiLog.Id, payload);
    }

    private static void ReportError(ApiLog apiLog, Payload payload, string errorMsg)
    {
        apiLog.Response = $"{errorMsg}\n\nRaw Response:\n{payload.Response}";
        apiLog.IsError = true;
        payload.ErrorMessage = errorMsg;
        ApiHistory.UpdatePayload(apiLog.Id, payload);
    }

    public static bool IsCancellationRequested() => _currentCts != null && _currentCts.IsCancellationRequested;

    public static bool CanCancelCurrent() => _busy && _currentRequest != null && !_currentRequest.TalkType.IsFromUser();

    public static bool CanCancelFor(TalkRequest incomingRequest)
    {
        if (!_busy || _currentRequest == null || incomingRequest == null) return false;

        // User talks and announcements always preempt any ongoing generation
        if (incomingRequest.TalkType.IsFromUser())
            return true;

        // Interactions and Urgent can cancel low-priority background talks (Other, Sleep, Thought, etc.)
        if (incomingRequest.TalkType is TalkType.Interaction or TalkType.Urgent)
            return !_currentRequest.TalkType.IsFastTrack();

        return false;
    }

    public static void CancelCurrent()
    {
        if (_currentCts != null && !_currentCts.IsCancellationRequested)
        {
            _currentCts.Cancel();
        }
    }

    public static bool IsFirstInstruction() => _firstInstruction;
    public static bool IsBusy()
    {
        if (!BusyGate.IsStuck(_busy, _busySince, DateTime.Now)) return _busy;

        Logger.Warning($"The AI slot has been held for over {BusyGate.StuckAfterSeconds}s. " +
                       "Releasing it - no request can legitimately take that long, and while it " +
                       "is held nobody in the colony can speak.");
        _busy = false;
        _busySince = null;
        return false;
    }
    public static void Clear()
    {
        CancelCurrent();
        _busy = false;
        _busySince = null;
        _firstInstruction = true;
        _currentCts = null;
        _currentRequest = null;
    }
}
