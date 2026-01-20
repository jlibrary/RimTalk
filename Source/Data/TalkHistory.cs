using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data;

public static class TalkHistory
{
    private static readonly ConcurrentDictionary<int, List<(Role role, string message)>> MessageHistory = new();
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentBag<Guid> IgnoredCache = [];
    
    // Add a new talk with the current game tick
    public static void AddSpoken(Guid id)
    {
        SpokenTickCache.TryAdd(id, GenTicks.TicksGame);
    }
    
    public static void AddIgnored(Guid id)
    {
        IgnoredCache.Add(id);
    }

    public static int GetSpokenTick(Guid id)
    {
        return SpokenTickCache.TryGetValue(id, out var tick) ? tick : -1;
    }
    
    public static bool IsTalkIgnored(Guid id)
    {
        return IgnoredCache.Contains(id);
    }

    public static void AddMessageHistory(Pawn pawn, TalkRequest request, string response)
    {
        var messages = MessageHistory.GetOrAdd(pawn.thingIDNumber, _ => []);

        lock (messages)
        {
            if (request != null && request.TalkType.IsFromUser())
            {
                var userPrompt = CleanHistoryText(request.RawPrompt);
                if (!string.IsNullOrWhiteSpace(userPrompt))
                    messages.Add((Role.User, userPrompt));
            }

            var aiText = BuildAssistantHistoryText(response);
            if (!string.IsNullOrWhiteSpace(aiText))
                messages.Add((Role.AI, aiText));

            EnsureMessageLimit(messages);
        }
    }

    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn)
    {
        if (!MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return [];
            
        lock (history)
        {
            int maxAiResponses = Settings.Get().Context.ConversationHistoryCount;
            int aiCount = history.Count(m => m.role == Role.AI);
            
            if (aiCount <= maxAiResponses)
                return [..history];
            
            var result = new List<(Role role, string message)>();
            int skippedAi = 0;
            int toSkip = aiCount - maxAiResponses;
            
            foreach (var msg in history)
            {
                if (msg.role == Role.AI && skippedAi < toSkip)
                {
                    skippedAi++;
                    continue;
                }
                result.Add(msg);
            }
            
            return result;
        }
    }

    private static void EnsureMessageLimit(List<(Role role, string message)> messages)
    {
        int maxAiResponses = Settings.Get().Context.ConversationHistoryCount;
        
        int aiCount = messages.Count(m => m.role == Role.AI);
        while (aiCount > maxAiResponses && messages.Count > 0)
        {
            if (messages[0].role == Role.AI) aiCount--;
            messages.RemoveAt(0);
        }
    }

    private static string CleanHistoryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var cleaned = CommonUtil.StripFormattingTags(text);
        return cleaned.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    private static string BuildAssistantHistoryText(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "";

        var lines = new List<string>();
        var trimmed = response.Trim();
        if (trimmed.StartsWith("[") || trimmed.StartsWith("{"))
        {
            try
            {
                var parsed = JsonUtil.DeserializeFromJson<List<TalkResponse>>(trimmed);
                if (parsed != null)
                {
                    foreach (var r in parsed)
                    {
                        if (r == null) continue;
                        var text = CleanHistoryText(r.Text);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var name = CleanHistoryText(r.Name);
                        lines.Add(string.IsNullOrWhiteSpace(name) ? text : $"{name}: {text}");
                    }
                }
            }
            catch
            {
                lines.Clear();
            }
        }

        if (lines.Count == 0)
        {
            var cleaned = CleanHistoryText(response);
            if (!string.IsNullOrWhiteSpace(cleaned))
                lines.Add(cleaned);
        }

        return string.Join("\n", lines);
    }

    public static void Clear()
    {
        MessageHistory.Clear();
        // clearing spokenCache may block child talks waiting to display
    }
}
