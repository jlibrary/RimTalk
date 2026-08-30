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
            log.Payload = payload;
        }
    }

    public static Payload GetPayload(ApiLog log)
    {
        if (log == null) return null;
        if (log.Payload != null) return log.Payload;
        if (log.ConversationId >= 0)
        {
            foreach (var item in History.Values)
            {
                if (item.ConversationId == log.ConversationId && item.Payload != null)
                    return item.Payload;
            }
        }
        return null;
    }

    public static ApiLog AddResponse(Guid id, string response, string name, string interactionType, Payload payload = null, int elapsedMs = 0)
    {
        return AddResponse(id, response, name, interactionType, payload, elapsedMs, null);
    }

    public static ApiLog AddResponse(Guid id, string response, string name, string interactionType, Payload payload,
        int elapsedMs, string targetName)
    {
        if (!History.TryGetValue(id, out var originalLog)) return null;

        // first message
        if (originalLog.Response == null)
        {
            originalLog.Name = name ?? originalLog.Name;
            originalLog.TargetName = targetName;
            originalLog.Response = response;
            originalLog.InteractionType = interactionType;
            if (payload != null)
                originalLog.Payload = payload;
            originalLog.ElapsedMs = (int)(DateTime.Now - originalLog.Timestamp).TotalMilliseconds;
            return originalLog;
        }
        
        // multi-turn messages
        var newLog = new ApiLog(name, originalLog.TalkRequest, response, payload, DateTime.Now, originalLog.Channel)
        {
            TargetName = targetName
        };
        History[newLog.Id] = newLog;
        newLog.InteractionType = interactionType;
        newLog.ElapsedMs = elapsedMs;
        newLog.ConversationId = originalLog.ConversationId;
        return newLog;
    }
    
    public static ApiLog AddUserHistory(Pawn initiator, Pawn recipient, string text, TalkType talkType = TalkType.User)
    {
        var prompt = talkType == TalkType.Announcement
            ? $"{initiator.LabelShort} announced"
            : $"{initiator.LabelShort} talked to {recipient?.LabelShort}"; 
        TalkRequest talkRequest = new(prompt, initiator, recipient, talkType);
        var log = new ApiLog(initiator.LabelShort, talkRequest, text, null, DateTime.Now, Channel.User)
        {
            TargetName = recipient?.LabelShort
        };
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
