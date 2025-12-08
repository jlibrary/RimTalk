using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    public static void AddMessageHistory(Pawn pawn, string request, string response)
    {
        var messages = MessageHistory.GetOrAdd(pawn.thingIDNumber, _ => []);

        lock (messages)
        {
            messages.Add((Role.User, request));
            messages.Add((Role.AI, response));
            EnsureMessageLimit(messages);
        }
    }

    public static List<(Role role, string message)> GetMessageHistory(Pawn pawn)
    {
        if (!MessageHistory.TryGetValue(pawn.thingIDNumber, out var history))
            return [];
            
        lock (history)
        {
            return [..history];
        }
    }

    private static void EnsureMessageLimit(List<(Role role, string message)> messages)
    {
        // First, ensure alternating pattern by removing consecutive duplicates from the end
        for (int i = messages.Count - 1; i > 0; i--)
        {
            if (messages[i].role == messages[i - 1].role)
            {
                // Remove the earlier message of the consecutive pair
                messages.RemoveAt(i - 1);
            }
        }

        // Then, enforce the maximum message limit by removing the oldest messages
        int maxMessages = Settings.Get().Context.ConversationHistoryCount;
        while (messages.Count > maxMessages * 2)
        {
            messages.RemoveAt(0);
        }
    }

    public static void Clear()
    {
        MessageHistory.Clear();
        // clearing spokenCache may block child talks waiting to display
    }
}