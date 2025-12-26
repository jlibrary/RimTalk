using System;
using System.Collections.Generic;
using RimTalk.Client;

namespace RimTalk.Data;

public class ApiLog(string name, TalkRequest talkRequest, string response, Payload payload, DateTime timestamp)
{
    public Guid Id { get; } = Guid.NewGuid();
    public int ConversationId { get; set; }
    public TalkRequest TalkRequest { get; set; } = talkRequest;
    public string Name { get; set; } = name;
    public string Response { get; set; } = response;
    public string InteractionType;
    public bool IsFirstDialogue;
    public string RequestPayload { get; set; } = payload?.Request;
    public string ResponsePayload { get; set; } = payload?.Response;
    public int TokenCount { get; set; } = payload?.TokenCount ?? 0;
    public DateTime Timestamp { get; } = timestamp;
    public int ElapsedMs;
    public int SpokenTick { get; set; } = 0;
}

public static class ApiHistory
{
    private static readonly Dictionary<Guid, ApiLog> History = new();
    private static int _conversationIdIndex = 0;
    
    public static ApiLog GetApiLog(Guid id) => History.TryGetValue(id, out var apiLog) ? apiLog : null;

    public static ApiLog AddRequest(TalkRequest request)
    {
        var log = new ApiLog(request.Initiator.LabelShort, request, null, null, DateTime.Now)
            {
                IsFirstDialogue = true,
                ConversationId = request.IsMonologue ? -1 : _conversationIdIndex++
            };
        History[log.Id] = log;
        return log;
    }

    public static void UpdatePayload(Guid id, Payload payload)
    {
        if (History.TryGetValue(id, out var log))
        {
            log.RequestPayload = payload?.Request;
            log.ResponsePayload = payload?.Response;
            log.TokenCount = payload?.TokenCount ?? 0;
        }
    }

    public static ApiLog AddResponse(Guid id, string response, string name, string interactionType, Payload payload = null, int elapsedMs = 0)
    {
        if (!History.TryGetValue(id, out var originalLog)) return null;

        // first message
        if (originalLog.Response == null)
        {
            originalLog.Name = name ?? originalLog.Name;
            originalLog.Response = response;
            originalLog.InteractionType = interactionType;
            originalLog.RequestPayload = payload?.Request;
            originalLog.ResponsePayload = payload?.Response;
            originalLog.TokenCount = payload?.TokenCount ?? 0;
            originalLog.ElapsedMs = (int)(DateTime.Now - originalLog.Timestamp).TotalMilliseconds;
            return originalLog;
        }
        
        // multi-turn messages
        var newLog = new ApiLog(name, originalLog.TalkRequest, response, payload, DateTime.Now);
        History[newLog.Id] = newLog;
        newLog.InteractionType = interactionType;
        newLog.ElapsedMs = elapsedMs;
        newLog.ConversationId = originalLog.ConversationId;
        return newLog;
    }
    
    public static ApiLog AddUserHistory(string name, string text)
    {
        var log = new ApiLog(name, null, text, null, DateTime.Now);
        History[log.Id] = log;
        return log;
    }

    public static IEnumerable<ApiLog> GetAll()
    {
        foreach (var log in History)
        {
            yield return log.Value;
        }
    }

    public static void Clear()
    {
        History.Clear();
    }
}
