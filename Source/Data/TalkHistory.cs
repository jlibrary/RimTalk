using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data;

public static class TalkHistory
{
    private static readonly ConcurrentDictionary<int, List<(Role role, string message, int tick, bool isUserSpeech)>> MessageHistory = new();
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentBag<Guid> IgnoredCache = [];
    private static readonly ConcurrentDictionary<string, byte> IgnoredTexts = new();
    
    // Add a new talk with the current game tick
    public static void AddSpoken(Guid id)
    {
        SpokenTickCache.TryAdd(id, GenTicks.TicksGame);
    }
    
    public static void AddIgnored(Guid id)
    {
        IgnoredCache.Add(id);
        var log = ApiHistory.GetApiLog(id);
        if (log == null) return;
        log.SpokenTick = -1;
        if (!string.IsNullOrWhiteSpace(log.Response))
            IgnoredTexts.TryAdd(log.Response, 0);
    }

    public static int GetSpokenTick(Guid id)
    {
        return SpokenTickCache.TryGetValue(id, out var tick) ? tick : -1;
    }
    
    public static bool IsTalkIgnored(Guid id)
    {
        return IgnoredCache.Contains(id);
    }

    public static void AddMessageHistory(Pawn pawn, string request, string response)
    {
        AddMessageHistory(pawn, request, response, isUserSpeech: false);
    }

    public static void AddMessageHistory(Pawn pawn, string request, string response, bool isUserSpeech)
    {
        var messages = MessageHistory.GetOrAdd(pawn.thingIDNumber, _ => []);
        int tick = Current.ProgramState == ProgramState.Playing ? GenTicks.TicksGame : 0;

        lock (messages)
        {
            if (!string.IsNullOrWhiteSpace(request))
                messages.Add((Role.User, request, tick, isUserSpeech));
            if (!string.IsNullOrWhiteSpace(response))
                messages.Add((Role.AI, response, tick, true));
            EnsureMessageLimit(messages);
        }
    }

    public static int GetLastMessageTick(Pawn pawn)
    {
        if (pawn == null || !MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return -1;

        lock (history)
        {
            return history.Count > 0 ? history[^1].tick : -1;
        }
    }

    public static int GetHistoryCount(Pawn pawn)
    {
        if (pawn == null || !MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return 0;

        lock (history)
        {
            return history.Count;
        }
    }

    public static List<(Role role, string message)> GetMessageHistory(List<Pawn> pawns, bool simplified = false)
    {
        if (pawns == null || pawns.Count == 0) return [];
        if (pawns.Count == 1) return GetMessageHistory(pawns[0], simplified);

        int currentTick = Current.ProgramState == ProgramState.Playing ? GenTicks.TicksGame : 0;
        const int cutoffTicks = 20000; // 8 in-game hours (2500 ticks/hr * 8)

        // 1. Gather all conversation entries across participating pawns
        var merged = new List<(Role role, string message, int tick, bool isUserSpeech)>();
        var seen = new HashSet<string>();

        for (int i = 0; i < pawns.Count; i++)
        {
            var p = pawns[i];
            if (p == null || !MessageHistory.TryGetValue(p.thingIDNumber, out var history))
                continue;

            lock (history)
            {
                for (int j = 0; j < history.Count; j++)
                {
                    var msg = history[j];
                    if (currentTick > 0 && msg.tick > 0 && (currentTick - msg.tick) > cutoffTicks)
                        continue;

                    // Deduplicate identical message entries (e.g. shared across conversation participants)
                    string key = $"{msg.tick}_{(int)msg.role}_{msg.message}";
                    if (seen.Add(key))
                    {
                        merged.Add(msg);
                    }
                }
            }
        }

        if (merged.Count == 0) return [];

        // 2. Sort chronologically (earlier tick first; User prompt before AI response if same tick)
        merged.Sort((a, b) =>
        {
            int tickCmp = a.tick.CompareTo(b.tick);
            if (tickCmp != 0) return tickCmp;
            if (a.role == Role.User && b.role != Role.User) return -1;
            if (a.role != Role.User && b.role == Role.User) return 1;
            return 0;
        });

        // 3. Ensure limit (conversation groups) and alternating structure
        EnsureMessageLimit(merged);

        // 4. Build output formatted list
        return FormatHistoryMessages(merged, currentTick, simplified);
    }

    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn, bool simplified = false)
    {
        if (pawn == null || !MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return [];
            
        int currentTick = Current.ProgramState == ProgramState.Playing ? GenTicks.TicksGame : 0;
        const int cutoffTicks = 20000; // 8 in-game hours (2500 ticks/hr * 8)

        List<(Role role, string message, int tick, bool isUserSpeech)> snapshot;
        lock (history)
        {
            if (history.Count == 0) return [];
            snapshot = new List<(Role role, string message, int tick, bool isUserSpeech)>(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                var msg = history[i];
                if (currentTick > 0 && msg.tick > 0 && (currentTick - msg.tick) > cutoffTicks)
                    continue;

                snapshot.Add(msg);
            }
        }

        return FormatHistoryMessages(snapshot, currentTick, simplified);
    }

    private static List<(Role role, string message)> FormatHistoryMessages(
        List<(Role role, string message, int tick, bool isUserSpeech)> rawMessages,
        int currentTick,
        bool simplified)
    {
        if (rawMessages == null || rawMessages.Count == 0) return [];

        var result = new List<(Role role, string message)>(rawMessages.Count);
        for (int i = 0; i < rawMessages.Count; i++)
        {
            var msg = rawMessages[i];
            if (simplified)
            {
                if (msg.role == Role.AI)
                {
                    int elapsed = (currentTick > 0 && msg.tick > 0) ? (currentTick - msg.tick) : -1;
                    var content = BuildAssistantHistoryText(msg.message, elapsed);
                    if (!string.IsNullOrWhiteSpace(content))
                        result.Add((msg.role, content));
                }
                else
                {
                    // Retain causal triggers (prompt/topic/intent) to prevent context amnesia during emotional escalation (Approach B').
                    // See: Docs/dialogue-history-architecture.md
                    var trigger = msg.message?.Trim();
                    if (!string.IsNullOrWhiteSpace(trigger))
                    {
                        var formatted = trigger.StartsWith("prompt:", StringComparison.OrdinalIgnoreCase) ? trigger : $"prompt: {trigger}";
                        result.Add((msg.role, formatted));
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(msg.message))
            {
                result.Add((msg.role, msg.message));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a compact causal prompt string from the dialogue request without environment/nearby/meta bloat.
    /// Strips transient ambient data (nearby, wealth, room) to reduce tokens and prevent hallucinating outdated items.
    /// See: Docs/dialogue-history-architecture.md
    /// </summary>
    public static string BuildCausalSummary(TalkRequest talkRequest, string intent, string topic)
    {
        if (talkRequest == null) return "";

        if (talkRequest.TalkType.IsFromUser() || talkRequest.IsAnnouncement)
            return (talkRequest.RawPrompt ?? topic ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(topic))
            return topic.Trim();

        return (talkRequest.RawPrompt ?? "").Trim();
    }

    private static void EnsureMessageLimit(List<(Role role, string message, int tick, bool isUserSpeech)> messages)
    {
        int maxMessages = Settings.Get()?.Context?.ConversationHistoryCount ?? 5;
        while (messages.Count > maxMessages * 2)
        {
            messages.RemoveAt(0);
        }
    }

    private static string CleanHistoryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var cleaned = CommonUtil.StripFormattingTags(text);
        return cleaned.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    private static string BuildAssistantHistoryText(string response, int elapsedTicks = -1)
    {
        if (string.IsNullOrWhiteSpace(response)) return "";

        var lines = new List<string>();
        var trimmed = response.Trim();

        if (trimmed.StartsWith("["))
        {
            try
            {
                var parsed = JsonUtil.DeserializeFromJson<List<TalkResponse>>(trimmed);
                if (parsed != null)
                {
                    foreach (var r in parsed)
                    {
                        if (r == null || (!string.IsNullOrWhiteSpace(r.Text) && IgnoredTexts.ContainsKey(r.Text))) continue;
                        var text = r.Text;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var name = r.Name;
                        lines.Add(string.IsNullOrWhiteSpace(name) ? text : $"{name}: {text}");
                    }
                    return lines.Count > 0 ? string.Join("\n", lines) : "";
                }
            }
            catch
            {
                lines.Clear();
            }
        }
        else if (trimmed.StartsWith("{"))
        {
            try
            {
                var single = JsonUtil.DeserializeFromJson<TalkResponse>(trimmed);
                if (single != null)
                {
                    if (!string.IsNullOrWhiteSpace(single.Text) && IgnoredTexts.ContainsKey(single.Text))
                        return "";
                    if (!string.IsNullOrWhiteSpace(single.Text))
                    {
                        var name = single.Name;
                        return string.IsNullOrWhiteSpace(name) ? single.Text : $"{name}: {single.Text}";
                    }
                    return "";
                }
            }
            catch
            {
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(response);
        }

        return string.Join("\n", lines);
    }

    public static void Clear()
    {
        MessageHistory.Clear();
        IgnoredTexts.Clear();
        // clearing spokenCache may block child talks waiting to display
    }
}
