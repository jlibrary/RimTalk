using System;
using System.Collections.Generic;
using RimTalk.Client;
using RimTalk.Source.Data;
using Verse;

namespace RimTalk.Data;

public static class ApiHistory
{
    private static readonly Dictionary<Guid, ApiLog> History = new();
    private static int _conversationIdIndex = 0;
    
    public static ApiLog GetApiLog(Guid id) => History.TryGetValue(id, out var apiLog) ? apiLog : null;

    public static ApiLog AddRequest(TalkRequest request, Channel channel)
    {
        var log = new ApiLog(request.Initiator.LabelShort, request, null, null, DateTime.Now, channel)
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
        var newLog = new ApiLog(name, originalLog.TalkRequest, response, payload, DateTime.Now, originalLog.Channel);
        History[newLog.Id] = newLog;
        newLog.InteractionType = interactionType;
        newLog.ElapsedMs = elapsedMs;
        newLog.ConversationId = originalLog.ConversationId;
        return newLog;
    }
    
    public static ApiLog AddUserHistory(Pawn initiator, Pawn recipient, string text)
    {
        var prompt = $"{initiator.LabelShort} talked to {recipient.LabelShort}"; 
        TalkRequest talkRequest = new(prompt, initiator, recipient, TalkType.User);
        var log = new ApiLog(initiator.LabelShort, talkRequest, text, null, DateTime.Now, Channel.User);
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
