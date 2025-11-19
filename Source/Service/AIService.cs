using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Source.Data;
using RimTalk.Util;

namespace RimTalk.Service;

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
            bool isUserTalk = request?.TalkType == TalkType.User
                              && request?.Initiator != null
                              && request?.Recipient != null;
            bool firstAiLine = true;
            var payload = await AIErrorHandler.HandleWithRetry(() =>
            {
                var client = AIClientFactory.GetAIClient();
                return client.GetStreamingChatCompletionAsync<TalkResponse>(_instruction, currentMessages,
                    talkResponse =>
                    {
                        if (isUserTalk && firstAiLine)
                        {
                           var name = talkResponse.Name;
                            var aName = request.Initiator.LabelShort;
                            var bName = request.Recipient.LabelShort;
                            bool invalid = string.IsNullOrEmpty(name)
                                           || (!name.Equals(aName, StringComparison.OrdinalIgnoreCase)
                                               && !name.Equals(bName, StringComparison.OrdinalIgnoreCase));
                            if (invalid)
                                talkResponse.Name = bName;
                        }

                        if (!players.TryGetValue(talkResponse.Name, out var player))
                        {
                            // Origin fallback
                            return;
                        }

                        talkResponse.TalkType = request.TalkType;

                        // Add logs
                        int elapsedMs = (int)(DateTime.Now - lastApiLog.Timestamp).TotalMilliseconds;
                        if (lastApiLog == initApiLog)
                            elapsedMs -= lastApiLog.ElapsedMs;
                        
                        var newApiLog = ApiHistory.AddResponse(initApiLog.Id, talkResponse.Text, talkResponse.Name, elapsedMs:elapsedMs);
                        talkResponse.Id = newApiLog.Id;
                        
                        lastApiLog = newApiLog;

                        onPlayerResponseReceived?.Invoke(player, talkResponse);
                        firstAiLine = false;
                    });
            });

            // Try deserializing once again with all streaming chunks to make sure a correct format was returned
            try
            {
                if (payload == null) throw new Exception();
                JsonUtil.DeserializeFromJson<List<TalkResponse>>(payload.Response);
            }
            catch
            {
                initApiLog.Response = "Failed";
                return;
            }
            
            ApiHistory.UpdatePayload(initApiLog.Id, payload);

            Stats.IncrementCalls();
            Stats.IncrementTokens(payload.TokenCount);

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

        var talkResponses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(payload.Response);

        if (talkResponses != null)
        {
            foreach (var talkResponse in talkResponses)
            {
                apiLog = ApiHistory.AddResponse(apiLog.Id, talkResponse.Text, talkResponse.Name, payload);
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

        ApiHistory.AddResponse(apiLog.Id, jsonData.ToString(), payload: payload);

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