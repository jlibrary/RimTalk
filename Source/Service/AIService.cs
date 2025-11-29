using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;

namespace RimTalk.Service;

// WARNING:
// This class defines core logic and has a significant impact on system behavior.
// In most cases, you should NOT modify this file.
public static class AIService
{
    private static string _instruction = "";
    private static bool _busy;
    private static bool _contextUpdating;
    private static bool _firstInstruction = true;

    /// <summary>
    /// Streaming chat that invokes callback as each player's dialogue is parsed
    /// </summary>
    public static async Task ChatStreaming<T>(TalkRequest request,
        List<(Role role, string message)> messages,
        Dictionary<string, T> players,
        Action<T, TalkResponse> onPlayerResponseReceived)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };
        var initApiLog = ApiHistory.AddRequest(request, _instruction);
        var lastApiLog = initApiLog;

        _busy = true;
        try
        {
            var payload = await AIErrorHandler.HandleWithRetry(() =>
            {
                var client = AIClientFactory.GetAIClient();
                return client.GetStreamingChatCompletionAsync<TalkResponse>(_instruction, currentMessages,
                    talkResponse =>
                    {
                        if (!players.TryGetValue(talkResponse.Name, out var player))
                            return;

                        talkResponse.TalkType = request.TalkType;

                        // Add logs
                        int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                        if (lastApiLog == initApiLog)
                            elapsedMs -= lastApiLog.ElapsedMs;
                        
                        var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name, talkResponse.InteractionRaw, elapsedMs:elapsedMs);
                        talkResponse.Id = newApiLog.Id;
                        
                        lastApiLog = newApiLog;

                        onPlayerResponseReceived?.Invoke(player, talkResponse);
                    });
            });

            if (payload == null || string.IsNullOrEmpty(initApiLog.Response))
            {
                initApiLog.Response = "Failed";
            }
            
            ApiHistory.UpdatePayload(initApiLog.Id, payload);

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload?.TokenCount ?? 0);

            _firstInstruction = false;
        }
        finally
        {
            _busy = false;
        }
    }

    // Original non-streaming method
    public static async Task<List<TalkResponse>> Chat(TalkRequest request,
        List<(Role role, string message)> messages)
    {
        var currentMessages = new List<(Role role, string message)>(messages) { (Role.User, request.Prompt) };

        var apiLog = ApiHistory.AddRequest(request, _instruction);

        var payload = await ExecuteAIRequest(_instruction, currentMessages);

        if (payload == null)
        {
            apiLog.Response = "Failed";
            return null;
        }

        // This needs to be changed if JSONL is used
        var talkResponses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(payload.Response);

        if (talkResponses != null)
        {
            foreach (var talkResponse in talkResponses)
            {
                apiLog = ApiHistory.AddResponse(apiLog.Id, talkResponse.Text, talkResponse.Name, talkResponse.InteractionRaw, payload);
                talkResponse.Id = apiLog.Id;
            }
        }

        _firstInstruction = false;

        return talkResponses;
    }

    // One time query - used for generating persona, etc
    public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
    {
        List<(Role role, string message)> message = [(Role.User, request.Prompt)];

        var apiLog = ApiHistory.AddRequest(request, _instruction);

        var payload = await ExecuteAIRequest(_instruction, message);

        if (payload == null)
        {
            apiLog.Response = "Failed";
            return null;
        }

        var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);

        ApiHistory.AddResponse(apiLog.Id, jsonData.GetText(), null, null, payload: payload);

        return jsonData;
    }

    private static async Task<Payload> ExecuteAIRequest(string instruction,
        List<(Role role, string message)> messages)
    {
        _busy = true;
        try
        {
            var payload = await AIErrorHandler.HandleWithRetry(() =>
                AIClientFactory.GetAIClient().GetChatCompletionAsync(instruction, messages)
            );

            if (payload == null)
                return null;

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload.TokenCount);

            return payload;
        }
        finally
        {
            _busy = false;
        }
    }

    public static void UpdateContext(string context)
    {
        Logger.Debug($"UpdateContext:\n{context}");
        _instruction = context;
    }

    public static string GetContext()
    {
        return _instruction;
    }

    public static bool IsFirstInstruction()
    {
        return _firstInstruction;
    }

    public static bool IsBusy()
    {
        return _busy || _contextUpdating;
    }

    public static bool IsContextUpdating()
    {
        return _contextUpdating;
    }

    public static void Clear()
    {
        _busy = false;
        _contextUpdating = false;
        _firstInstruction = true;
        _instruction = "";
    }
}