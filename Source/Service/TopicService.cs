using System;
using System.Collections.Generic;
using RimTalk.Data;
using Verse;

namespace RimTalk.Service;

/// <summary>
/// Combines an Approach (conversational angle) and a Subject (discussion topic) from TopicKeywordPool.
/// Uses independent shuffled decks for both to guarantee non-repeating combinations
/// while staying fully compatible with any pawn persona.
/// </summary>
public static class TopicService
{
    private static readonly object Lock = new();
    private static readonly Random Rng = new();

    private static Queue<string> _approachDeck = new();
    private static Queue<string> _subjectDeck = new();

    /// <summary>
    /// Generates a composite topic with an Approach keyword and a Subject keyword.
    /// Example: "[reminiscing, food]"
    /// </summary>
    public static string GetNextTopic()
    {
        lock (Lock) { EnsureDecks(); return TopicCore(); }
    }

    /// <summary>
    /// Returns a topic string based on conversation state triggers, or guaranteed topic if it's the pawn's first talk.
    /// Non-humanlikes (animals, mechanoids, entities) and mutants never receive human narrative topics.
    /// </summary>
    public static string TryGetTopic(Pawn pawn = null)
    {
        if (pawn != null && (!pawn.RaceProps.Humanlike || pawn.IsMutant))
            return null;

        lock (Lock)
        {
            if (!ShouldTriggerTopic(pawn)) return null;
            EnsureDecks();
            return TopicCore();
        }
    }

    private static bool ShouldTriggerTopic(Pawn pawn)
    {
        if (pawn == null) return Rng.NextDouble() < 0.70;
        if (pawn.InMentalState) return true;

        bool isFirstTalk = Cache.Get(pawn)?.LastTalkTick == 0;
        if (isFirstTalk) return true;

        int currentTick = Current.ProgramState == ProgramState.Playing ? GenTicks.TicksGame : 0;
        int lastTick = TalkHistory.GetLastMessageTick(pawn);

        // 1. Fresh conversation session (no prior history or more than 1 in-game hour since last speech)
        if (lastTick <= 0 || (currentTick > 0 && currentTick - lastTick > 2500))
            return true;

        // 2. Ongoing conversation: check how long the current exchange has been active
        int historyCount = TalkHistory.GetHistoryCount(pawn);
        // If 3 or more messages have passed, previous topic is exhausted -> pivot to a new topic
        if (historyCount >= 3)
            return true;

        // During a fresh 1-2 turn exchange, avoid abrupt topic derailment so pawns can respond to each other
        return false;
    }

    private static string TopicCore()
    {
        string approach = PeekDeck(_approachDeck) ?? "casual remark";
        string subject  = PeekDeck(_subjectDeck)  ?? "daily life";
        return $"{approach}, {subject}";
    }

    private static string PeekDeck(Queue<string> deck)
    {
        if (deck.Count == 0) return null;
        return deck.Dequeue();
    }

    private static void EnsureDecks()
    {
        if (_approachDeck.Count == 0) RefillDeck(ref _approachDeck, TopicKeywordPool.ApproachKeywords);
        if (_subjectDeck.Count == 0)  RefillDeck(ref _subjectDeck,  TopicKeywordPool.SubjectKeywords);
    }

    private static void RefillDeck(ref Queue<string> deck, string[] source)
    {
        if (source == null || source.Length == 0) return;

        var list = new List<string>(source);
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
        deck = new Queue<string>(list);
    }

    /// <summary>
    /// Resets all topic decks.
    /// </summary>
    public static void Reset()
    {
        lock (Lock)
        {
            _approachDeck.Clear();
            _subjectDeck.Clear();
        }
    }
}
