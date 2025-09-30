using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimTalk.Client;

namespace RimTalk.Data;

public class ApiLog(string name, string prompt, string response, Payload payload, DateTime timestamp, List<string> contexts = null)
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = name;
    public List<string> Contexts { get; set; } = contexts ?? new List<string>();
    public string Prompt { get; set; } = prompt;
    public string Response { get; set; } = response;
    public string RequestPayload { get; set; } = payload?.Request;
    public string ResponsePayload { get; set; } = payload?.Response;
    public int TokenCount { get; set; } = payload?.TokenCount ?? 0;
    public DateTime Timestamp { get; } = timestamp;
    public int ElapsedMs;
    public int SpokenTick { get; set; } = 0;
    
    public static List<string> ExtractContextBlocks(string context)
    {
        var blocks = new List<string>();
        if (string.IsNullOrEmpty(context)) return blocks;
        
        // Regex to match everything from [Person ... START] to [Person ... END], including newlines
        string pattern = @"\[Person\s+\d+\s+START\](.*?)\[Person\s+\d+\s+END\]";
        var matches = Regex.Matches(context, pattern, RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            blocks.Add(match.Groups[1].Value.Trim());
        }

        return blocks;
    }
}

public static class ApiHistory
{
    private static readonly Dictionary<Guid, ApiLog> History = new();
    
    public static ApiLog GetApiLog(Guid id) => History.TryGetValue(id, out var apiLog) ? apiLog : null;

    public static ApiLog AddRequest(TalkRequest request, string context)
    {
        var log = new ApiLog(request.Initiator.LabelShort, request.Prompt, null, null, DateTime.Now, ApiLog.ExtractContextBlocks(context));
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

    public static ApiLog AddResponse(Guid id, string response, string name = null, Payload payload = null, int elapsedMs = 0)
    {
        if (!History.TryGetValue(id, out var originalLog)) return null;

        // first message
        if (originalLog.Response == null)
        {
            originalLog.Name = name ?? originalLog.Name;
            originalLog.Response = response;
            originalLog.RequestPayload = payload?.Request;
            originalLog.ResponsePayload = payload?.Response;
            originalLog.TokenCount = payload?.TokenCount ?? 0;
            originalLog.ElapsedMs = (int)(DateTime.Now - originalLog.Timestamp).TotalMilliseconds;
            return originalLog;
        }
        
        // multi-turn messages
        var newLog = new ApiLog(name, originalLog.Prompt, response, payload, DateTime.Now, originalLog.Contexts);
        History[newLog.Id] = newLog;
        newLog.ElapsedMs = elapsedMs;
        return newLog;
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
