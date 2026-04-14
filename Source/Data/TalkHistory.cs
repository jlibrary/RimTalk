using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Data;

public static class TalkHistory
{
    private static readonly ConcurrentDictionary<int, List<(Role role, string message)>> MessageHistory = new();
    private static readonly ConcurrentDictionary<int, List<(Role role, string message)>> DialogueHistory = new();
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

    public static void AddMessageHistory(Pawn pawn, string request, string response)
    {
        if (pawn == null) return;

        var messages = MessageHistory.GetOrAdd(pawn.thingIDNumber, _ => []);

        lock (messages)
        {
            messages.Add((Role.User, request ?? ""));
            messages.Add((Role.AI, response ?? ""));
            EnsureRawMessageLimit(messages);
        }
    }

    public static void AddConversationHistory(TalkRequest request, List<TalkResponse> responses)
    {
        if (responses == null || responses.Count == 0) return;

        var historyPawns = new List<Pawn>();
        string prompt = request?.Prompt ?? "";
        string serializedResponses = JsonUtil.SerializeToJson(responses);

        void AddPawn(Pawn pawn)
        {
            if (pawn != null && !historyPawns.Contains(pawn))
                historyPawns.Add(pawn);
        }

        AddPawn(request?.Initiator);
        AddPawn(request?.Recipient);

        foreach (var response in responses)
        {
            AddPawn(Cache.GetByName(response?.Name)?.Pawn);
            AddPawn(Cache.GetByName(response?.TargetName)?.Pawn);
        }

        foreach (var pawn in historyPawns)
        {
            AddMessageHistory(pawn, prompt, serializedResponses);

            var conversationSlice = BuildConversationSliceForPawn(pawn, request, responses);
            AddDialogueHistory(pawn, conversationSlice);
        }

        Logger.Debug(
            $"History saved for {historyPawns.Count} pawns; " +
            $"initiator={request?.Initiator?.LabelShort ?? "null"}, " +
            $"recipient={request?.Recipient?.LabelShort ?? "null"}, " +
            $"responses={responses.Count}");
    }

    public static List<(Role role, string message)> GetDialogueHistory(Pawn pawn)
    {
        if (pawn == null || !DialogueHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return [];

        lock (history)
        {
            return history
                .Where(msg => !string.IsNullOrWhiteSpace(msg.message))
                .ToList();
        }
    }

    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn, bool simplified = false)
    {
        if (!MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return [];
            
        lock (history)
        {
            var result = new List<(Role role, string message)>();
            foreach (var msg in history)
            {
                var content = msg.message;
                if (simplified)
                {
                    if (msg.role == Role.AI)
                        content = BuildAssistantHistoryText(content);
                    
                    content = CleanHistoryText(content);
                }
                
                if (!string.IsNullOrWhiteSpace(content))
                    result.Add((msg.role, content));
            }
            return result;
        }
    }

    private static void EnsureRawMessageLimit(List<(Role role, string message)> messages)
    {
        for (int i = messages.Count - 1; i > 0; i--)
        {
            if (messages[i].role == messages[i - 1].role)
            {
                messages.RemoveAt(i - 1);
            }
        }

        int maxMessages = Settings.Get().Context.ConversationHistoryCount;
        while (messages.Count > maxMessages * 2)
        {
            messages.RemoveAt(0);
        }
    }

    private static void EnsureDialogueMessageLimit(List<(Role role, string message)> messages)
    {
        int maxMessages = Settings.Get().Context.ConversationHistoryCount;
        while (messages.Count > maxMessages)
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
                        var text = r.Text;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var name = r.Name;
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
            lines.Add(response);
        }

        return string.Join("\n", lines);
    }

    private static void AddDialogueHistory(Pawn pawn, IEnumerable<(Role role, string message)> messagesToAdd)
    {
        if (pawn == null || messagesToAdd == null) return;

        var normalizedMessages = NormalizeDialogueMessages(messagesToAdd).ToList();
        if (normalizedMessages.Count == 0) return;

        var messages = DialogueHistory.GetOrAdd(pawn.thingIDNumber, _ => []);

        lock (messages)
        {
            messages.AddRange(normalizedMessages);
            EnsureDialogueMessageLimit(messages);
        }
    }

    private static IEnumerable<(Role role, string message)> BuildConversationSliceForPawn(
        Pawn pawn,
        TalkRequest request,
        List<TalkResponse> responses)
    {
        if (pawn == null || responses == null || responses.Count == 0)
            yield break;

        var lines = new List<string>();

        var userInputMessage = BuildUserInputHistoryMessage(pawn, request);
        if (!string.IsNullOrWhiteSpace(userInputMessage))
            lines.Add(userInputMessage);

        foreach (var response in responses)
        {
            if (response == null) continue;

            string content = FormatDialogueTranscriptLine(response);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            lines.Add(content);
        }

        if (lines.Count > 0)
            yield return (Role.User, string.Join("\n", lines));
    }

    private static string BuildUserInputHistoryMessage(Pawn pawn, TalkRequest request)
    {
        if (pawn == null || request == null || !request.TalkType.IsFromUser())
            return "";

        var text = CleanHistoryText(request.RawPrompt);
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var speakerName = request.Recipient?.LabelShort ??
                          request.Recipient?.Name?.ToStringShort ??
                          "Player";
        return $"{speakerName}: {text}";
    }

    private static IEnumerable<(Role role, string message)> BuildLooseConversationSliceForPawn(
        Pawn pawn,
        List<TalkResponse> responses)
    {
        foreach (var response in responses)
        {
            if (response == null) continue;

            bool spokenByPawn = MatchesPawnName(response.Name, pawn);
            string content = FormatConversationMessageForPawn(pawn, response, spokenByPawn);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            yield return (spokenByPawn ? Role.User : Role.AI, content);
        }
    }

    private static IEnumerable<(Role role, string message)> NormalizeMessages(IEnumerable<(Role role, string message)> messages)
    {
        Role? lastRole = null;
        string lastMessage = null;

        foreach (var (role, message) in messages)
        {
            string cleaned = CleanHistoryText(message);
            if (string.IsNullOrWhiteSpace(cleaned))
                continue;

            if (lastRole == role)
            {
                lastMessage += "\n" + cleaned;
                continue;
            }

            if (lastRole.HasValue && !string.IsNullOrWhiteSpace(lastMessage))
                yield return (lastRole.Value, lastMessage);

            lastRole = role;
            lastMessage = cleaned;
        }

        if (lastRole.HasValue && !string.IsNullOrWhiteSpace(lastMessage))
            yield return (lastRole.Value, lastMessage);
    }

    private static IEnumerable<(Role role, string message)> NormalizeDialogueMessages(IEnumerable<(Role role, string message)> messages)
    {
        foreach (var (role, message) in messages)
        {
            string cleaned = CleanDialogueHistoryText(message);
            if (!string.IsNullOrWhiteSpace(cleaned))
                yield return (role, cleaned);
        }
    }

    private static string CleanDialogueHistoryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var cleaned = CommonUtil.StripFormattingTags(text)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        var lines = cleaned
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join("\n", lines).Trim();
    }

    private static HashSet<string> GetLikelyPartnerNames(Pawn pawn, TalkRequest request, List<TalkResponse> responses)
    {
        var partners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPartner(Pawn otherPawn)
        {
            if (otherPawn != null && otherPawn != pawn)
                partners.Add(otherPawn.LabelShort);
        }

        if (pawn == request?.Initiator)
            AddPartner(request.Recipient);

        if (pawn == request?.Recipient)
            AddPartner(request.Initiator);

        foreach (var response in responses)
        {
            if (response == null) continue;

            if (MatchesPawnName(response.TargetName, pawn) && !string.IsNullOrWhiteSpace(response.Name))
                partners.Add(response.Name);

            if (MatchesPawnName(response.Name, pawn) && !string.IsNullOrWhiteSpace(response.TargetName))
                partners.Add(response.TargetName);
        }

        if (partners.Count == 0)
        {
            var otherSpeakers = responses
                .Select(r => r?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !MatchesPawnName(name, pawn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (otherSpeakers.Count == 1)
                partners.Add(otherSpeakers[0]);
        }

        return partners;
    }

    private static string FormatConversationMessageForPawn(Pawn pawn, TalkResponse response, bool spokenByPawn)
    {
        string text = CleanHistoryText(response.Text);
        if (string.IsNullOrWhiteSpace(text))
            return "";

        if (spokenByPawn)
        {
            if (!string.IsNullOrWhiteSpace(response.TargetName) && !MatchesPawnName(response.TargetName, pawn))
                return $"To {response.TargetName}: {text}";

            return text;
        }

        return string.IsNullOrWhiteSpace(response.Name) ? text : $"{response.Name}: {text}";
    }

    private static string FormatDialogueTranscriptLine(TalkResponse response)
    {
        string text = CleanHistoryText(response?.Text);
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string speaker = response?.Name;
        if (string.IsNullOrWhiteSpace(speaker))
            speaker = "Unknown";

        return $"{speaker}: {text}";
    }

    private static bool MatchesPawnName(string name, Pawn pawn)
    {
        if (pawn == null || string.IsNullOrWhiteSpace(name))
            return false;

        return string.Equals(name, pawn.LabelShort, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, pawn.Name?.ToStringShort, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, pawn.Name?.ToStringFull, StringComparison.OrdinalIgnoreCase);
    }

    public static void Clear()
    {
        MessageHistory.Clear();
        DialogueHistory.Clear();
        // clearing spokenCache may block child talks waiting to display
    }
}
