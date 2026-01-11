using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Source.Data;
using RimTalk.Util;

namespace RimTalk.Service;

// WARNING:
// This class defines core logic and has a significant impact on system behavior.
// In most cases, you should NOT modify this file.
public static class AIService
{
    private static bool _busy;
    private static bool _firstInstruction = true;

    /// <summary>
    /// Streaming chat that invokes callback as each player's dialogue is parsed
    /// </summary>
    public static async Task ChatStreaming(TalkRequest request, Action<TalkResponse> onPlayerResponseReceived)
    {
        var prefixMessages = request.PromptMessages ?? new List<(Role role, string message)>();
        var apiLog = ApiHistory.AddRequest(request, Channel.Stream);
        var lastApiLog = apiLog;

        var payload = await ExecuteWithRetry(apiLog, async client =>
        {
            // All prompt messages are already in prefixMessages, pass empty list for messages
            return await client.GetStreamingChatCompletionAsync<TalkResponse>(prefixMessages, new List<(Role, string)>(),
                response =>
                {
                    if (Cache.GetByName(response.Name) == null) return;
                    
                    response.TalkType = request.TalkType;

                    // Calculate timing relative to the correct previous log
                    int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                    if (lastApiLog == apiLog) elapsedMs -= lastApiLog.ElapsedMs;

                    var newLog = ApiHistory.AddResponse(apiLog.Id, response.Text, response.Name,
                        response.InteractionRaw, elapsedMs: elapsedMs);
                    
                    response.Id = newLog.Id;
                    lastApiLog = newLog;

                    onPlayerResponseReceived?.Invoke(response);
                });
        });

        HandleFinalStatus(apiLog, payload);
        _firstInstruction = false;
    }

    // One time query - used for generating persona, etc
    public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
    {
        var messages = new List<(Role role, string message)> { (Role.User, request.Prompt) };
        var prefixMessages = new List<(Role role, string message)> { (Role.System, request.Context) };
        var apiLog = ApiHistory.AddRequest(request, Channel.Query);

        var payload = await ExecuteWithRetry(apiLog, async client =>
            await client.GetChatCompletionAsync(prefixMessages, messages));

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
        finally
        {
            _busy = false;
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

    public static bool IsFirstInstruction() => _firstInstruction;
    public static bool IsBusy() => _busy;
    public static void Clear()
    {
        _busy = false;
        _firstInstruction = true;
    }
}
