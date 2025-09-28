using System;
using System.Collections.Generic;
using RimTalk.Client;

namespace RimTalk.Data;

public class ApiLog(string name, string prompt, string response, Payload payload, DateTime timestamp)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = name;
    public string Prompt { get; set; } = prompt;
    public string Response { get; set; } = response;
    public string RequestPayload { get; set; } = payload?.Request;
    public string ResponsePayload { get; set; } = payload?.Response;
    public int TokenCount { get; set; } = payload?.TokenCount ?? 0;
    public DateTime Timestamp { get; } = timestamp;
    public int ElapsedMs;
    public bool IsSpoken;
}

public static class ApiHistory
{
    private static readonly Dictionary<Guid, ApiLog> History = new();
    
    public static ApiLog GetApiLog(Guid id) => History.TryGetValue(id, out var apiLog) ? apiLog : null;

    public static Guid AddRequest(TalkRequest request)
    {
        var log = new ApiLog(request.Initiator.LabelShort, request.Prompt, null, null, DateTime.Now);
        History[log.Id] = log;
        return log.Id;
    }

    public static void RemoveRequest(Guid id)
    {
        History.Remove(id);
    }

    public static Guid AddResponse(Guid id, string response, Payload payload, string name = null)
    {
        if (History.TryGetValue(id, out var log))
        {
            // first message
            if (log.Response == null)
            {
                log.Name = name ?? log.Name;
                log.Response = response;
                log.RequestPayload = payload?.Request;
                log.ResponsePayload = payload?.Response;
                log.TokenCount = payload?.TokenCount ?? 0;
                log.ElapsedMs = (int)(DateTime.Now - log.Timestamp).TotalMilliseconds;
            }
            // rest of multi-turn messages
            else
            {
                log = new ApiLog(name, log.Prompt, response, payload, log.Timestamp);
                History[log.Id] = log;
                log.TokenCount = 0;
                log.ElapsedMs = 0;
            }
            return log.Id;
        }
        return Guid.Empty;
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