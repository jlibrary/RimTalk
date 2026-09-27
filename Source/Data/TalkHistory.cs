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
    private static readonly ConcurrentDictionary<int, List<(Role role, string message, int tick)>> MessageHistory = new();
    private static readonly ConcurrentDictionary<Guid, int> SpokenTickCache = new() { [Guid.Empty] = 0 };
    private static readonly ConcurrentDictionary<Guid, byte> IgnoredCache = new();
    private static readonly ConcurrentDictionary<string, byte> IgnoredTexts = new();
    
    // Add a new talk with the current game tick
    public static void AddSpoken(Guid id)
    {
        SpokenTickCache.TryAdd(id, GenTicks.TicksGame);
    }
    
    public static void AddIgnored(Guid id)
    {
        IgnoredCache.TryAdd(id, 0);
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
        return IgnoredCache.ContainsKey(id);
    }

    public static void AddMessageHistory(Pawn pawn, string request, string response)
    {
        var messages = MessageHistory.GetOrAdd(pawn.thingIDNumber, _ => []);
        int tick = Current.ProgramState == ProgramState.Playing ? GenTicks.TicksGame : 0;

        lock (messages)
        {
            if (!string.IsNullOrWhiteSpace(request))
                messages.Add((Role.User, request, tick));
            if (!string.IsNullOrWhiteSpace(response))
                messages.Add((Role.AI, response, tick));
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
        var merged = new List<(Role role, string message, int tick)>();
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

        List<(Role role, string message, int tick)> snapshot;
        lock (history)
        {
            if (history.Count == 0) return [];
            snapshot = new List<(Role role, string message, int tick)>(history.Count);
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
        List<(Role role, string message, int tick)> rawMessages,
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

        // For events and quests, record only the compact incident label in history (avoids bloating multi-turn context)
        if (talkRequest.TalkType is TalkType.Event or TalkType.QuestOffer or TalkType.QuestEnd)
        {
            return ExtractEventLabel(talkRequest.Prompt ?? topic);
        }

        if (!string.IsNullOrWhiteSpace(topic))
            return topic.Trim();

        if (!string.IsNullOrWhiteSpace(intent))
            return intent.Trim();

        return (talkRequest.RawPrompt ?? "").Trim();
    }

    private static string ExtractEventLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Incident";

        var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        if (firstLine.StartsWith("(") && firstLine.Contains(":"))
        {
            int colonIdx = firstLine.IndexOf(':');
            int closeParen = firstLine.LastIndexOf(')');
            if (colonIdx >= 0 && closeParen > colonIdx)
            {
                string extracted = firstLine.Substring(colonIdx + 1, closeParen - colonIdx - 1).Trim();
                if (!string.IsNullOrEmpty(extracted))
                {
                    string target = ExtractTargetSuffix(text);
                    return !string.IsNullOrEmpty(target) && !extracted.Contains(target)
                        ? $"{extracted} {target}"
                        : extracted;
                }
            }
        }

        var cleaned = text.Trim();
        int openBracket = cleaned.IndexOf('[');
        int closeBracket = cleaned.IndexOf(']');
        if (openBracket >= 0)
        {
            int end = closeBracket > openBracket ? closeBracket : cleaned.Length;
            cleaned = cleaned.Substring(openBracket + 1, end - openBracket - 1).Trim();
        }
        var bodyLine = cleaned.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(bodyLine)) return "Incident";
        if (bodyLine.Length > 60) bodyLine = bodyLine.Substring(0, 57) + "...";
        return bodyLine;
    }

    private static string ExtractTargetSuffix(string text)
    {
        int targetIdx = text.IndexOf("(Target:", StringComparison.OrdinalIgnoreCase);
        if (targetIdx >= 0)
        {
            int endParen = text.IndexOf(')', targetIdx);
            if (endParen > targetIdx)
            {
                return text.Substring(targetIdx, endParen - targetIdx + 1).Trim();
            }
        }
        return null;
    }

    private static void EnsureMessageLimit(List<(Role role, string message, int tick)> messages)
    {
        int maxMessages = Settings.Get()?.Context?.ConversationHistoryCount ?? 2;
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
        IgnoredCache.Clear();
        IgnoredTexts.Clear();
        // clearing spokenCache may block child talks waiting to display
    }
}
