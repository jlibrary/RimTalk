using System;
using System.Collections.Generic;
using RimTalk.Data;
using RimTalk.Source.Data;
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
    /// Checks whether general conversation topic keywords are permitted for the given request and pawn.
    /// Excludes mental breaks, stranger encounters, non-humanlikes, and non-social talk types.
    /// </summary>
    public static bool IsTopicAllowed(TalkRequest talkRequest, Pawn pawn, bool isStrangerEncounter = false)
    {
        if (pawn == null || !pawn.RaceProps.Humanlike || pawn.IsMutant || pawn.InMentalState || isStrangerEncounter)
            return false;

        var settings = Settings.Get()?.Context;
        if (settings == null || !settings.IncludeTopicKeywords)
            return false;

        if (talkRequest == null)
            return true;

        return talkRequest.TalkType is TalkType.Chitchat or TalkType.Interaction or TalkType.Other;
    }

    /// <summary>
    /// Checks whether mental break delusion topic keywords are permitted for the given request and pawn.
    /// </summary>
    public static bool IsMentalBreakTopicAllowed(TalkRequest talkRequest, Pawn pawn)
    {
        if (pawn == null || !pawn.RaceProps.Humanlike || pawn.IsMutant || !pawn.InMentalState)
            return false;

        var settings = Settings.Get()?.Context;
        if (settings == null || !settings.IncludeTopicKeywords)
            return false;

        return talkRequest?.TalkType != TalkType.Urgent;
    }

    /// <summary>
    /// Evaluates and returns a cached topic for general dialogue requests, ensuring prompt and history remain synchronized.
    /// </summary>
    public static string ResolveTopicForRequest(TalkRequest talkRequest, Pawn pawn, bool isStrangerEncounter = false, bool isMonologue = false)
    {
        if (pawn == null || pawn.InMentalState)
            return null;

        if (talkRequest == null)
            return IsTopicAllowed(null, pawn, isStrangerEncounter) ? TryGetTopic(pawn, isMonologue) : null;

        if (talkRequest.TopicEvaluated)
            return talkRequest.Topic;

        talkRequest.TopicEvaluated = true;

        if (!IsTopicAllowed(talkRequest, pawn, isStrangerEncounter))
        {
            talkRequest.Topic = null;
            return null;
        }

        talkRequest.Topic = TryGetTopic(pawn, isMonologue);
        return talkRequest.Topic;
    }

    /// <summary>
    /// Evaluates and returns a cached topic for mental break requests.
    /// </summary>
    public static string ResolveMentalBreakTopicForRequest(TalkRequest talkRequest, Pawn pawn)
    {
        if (pawn == null || !pawn.InMentalState)
            return null;

        if (talkRequest == null)
            return IsMentalBreakTopicAllowed(null, pawn) ? TryGetTopic(pawn, isMonologue: true) : null;

        if (talkRequest.TopicEvaluated)
            return talkRequest.Topic;

        talkRequest.TopicEvaluated = true;

        if (!IsMentalBreakTopicAllowed(talkRequest, pawn))
        {
            talkRequest.Topic = null;
            return null;
        }

        talkRequest.Topic = TryGetTopic(pawn, isMonologue: true);
        return talkRequest.Topic;
    }

    /// <summary>
    /// Returns a topic string based on conversation state triggers, or guaranteed topic if it's the pawn's first talk.
    /// Non-humanlikes (animals, mechanoids, entities) and mutants never receive human narrative topics.
    /// </summary>
    public static string TryGetTopic(Pawn pawn = null)
    {
        return TryGetTopic(pawn, isMonologue: false);
    }

    /// <summary>
    /// Returns a topic string based on conversation state triggers, taking monologue status into account.
    /// </summary>
    public static string TryGetTopic(Pawn pawn, bool isMonologue)
    {
        if (pawn != null && (!pawn.RaceProps.Humanlike || pawn.IsMutant))
            return null;

        lock (Lock)
        {
            if (!ShouldTriggerTopic(pawn, isMonologue)) return null;
            EnsureDecks();
            return TopicCore();
        }
    }

    private static bool ShouldTriggerTopic(Pawn pawn, bool isMonologue = false)
    {
        if (pawn == null) return Rng.NextDouble() < 0.50;
        if (pawn.InMentalState) return true;

        bool isFirstTalk = Cache.Get(pawn)?.LastTalkTick == 0;
        if (isFirstTalk) return true;

        return Rng.NextDouble() < 0.50;
    }

    private static string TopicCore()
    {
        string approach = DrawFromDeck(_approachDeck) ?? "casual remark";
        string subject  = DrawFromDeck(_subjectDeck)  ?? "daily life";
        return $"{approach}, {subject}";
    }

    private static string DrawFromDeck(Queue<string> deck)
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
