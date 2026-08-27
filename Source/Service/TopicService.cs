using System;
using System.Collections.Generic;
using RimTalk.Data;

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
    private static Queue<string> _monologueDeck = new();

    /// <summary>
    /// Generates a composite topic with an Approach keyword and a Subject keyword.
    /// Example: "[approach: reminiscing | topic: favorite meals and comfort food]"
    /// </summary>
    public static string GetNextDailyTopic()
    {
        lock (Lock) { EnsureDecks(); return DailyTopicCore(); }
    }

    /// <summary>
    /// Generates a non-repeating monologue keyword for solo pawns.
    /// </summary>
    public static string GetNextMonologueTopic()
    {
        lock (Lock) { EnsureDecks(); return MonologueTopicCore(); }
    }

    /// <summary>
    /// Returns a topic string with 50% probability, or null if the roll fails.
    /// Encapsulates the random gate so callers don't need their own Random instance.
    /// </summary>
    /// <param name="isSolo">True for solo pawn monologues, false for group dialogue.</param>
    public static string TryGetTopic(bool isSolo)
    {
        lock (Lock)
        {
            if (Rng.NextDouble() >= 0.50) return null;
            EnsureDecks();
            return isSolo ? MonologueTopicCore() : DailyTopicCore();
        }
    }

    private static string DailyTopicCore()
    {
        string approach = PeekDeck(_approachDeck) ?? "casual remark";
        string subject  = PeekDeck(_subjectDeck)  ?? "daily life";
        return $"[{approach}, {subject}]";
    }

    private static string MonologueTopicCore()
    {
        string monologue = PeekDeck(_monologueDeck) ?? "quietly reflecting";
        return $"[{monologue}]";
    }

    private static string PeekDeck(Queue<string> deck)
    {
        if (deck.Count == 0) return null;
        return deck.Dequeue();
    }

    private static void EnsureDecks()
    {
        if (_approachDeck.Count == 0)  RefillDeck(ref _approachDeck,  TopicKeywordPool.ApproachKeywords);
        if (_subjectDeck.Count == 0)   RefillDeck(ref _subjectDeck,   TopicKeywordPool.SubjectKeywords);
        if (_monologueDeck.Count == 0) RefillDeck(ref _monologueDeck, TopicKeywordPool.MonologueKeywords);
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
            _monologueDeck.Clear();
        }
    }
}
