using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Service;

/// <summary>
/// Service responsible for collecting, ranking, and formatting active and past colony events for AI context.
/// </summary>
public static class EventService
{
    private static readonly string[] KnownExternalEventModIds =
    {
        "saltgin.rimtalkeventmemory"
    };

    /// <summary>
    /// Checks if a known external event-handling addon mod is active.
    /// </summary>
    public static bool IsExternalEventModActive => ModUtil.IsAnyModActive(KnownExternalEventModIds);

    /// <summary>
    /// Returns the display names of currently active external event addon mods, fetched directly from their mod metadata.
    /// </summary>
    public static string GetActiveExternalEventModNames() => ModUtil.GetActiveModNames(KnownExternalEventModIds);

    /// <summary>
    /// Default include events toggle: false if an external event mod is active, otherwise true.
    /// </summary>
    public static bool DefaultIncludeEvents => !IsExternalEventModActive;

    public class ColonyEventCandidate
    {
        public Letter Letter { get; set; }
        public string Label { get; set; }
        public int ArrivalTick { get; set; }
        public int ElapsedTicks { get; set; }
        public float ElapsedHours { get; set; }
        public bool IsActiveOnScreen { get; set; }
        public int Score { get; set; }
    }

    private static readonly ConcurrentDictionary<string, int> RecentInjectedTicks = new();
    private const int FatigueDurationTicks = 5000; // 2 in-game hours (2500 ticks/hr * 2)
    private const int FatiguePenaltyScore = 250;

    /// <summary>
    /// Builds formatted events context string for the specified map based on active letters and past archive.
    /// </summary>
    public static string GetEventsContext(Map map, PromptService.InfoLevel infoLevel = PromptService.InfoLevel.Normal)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeEvents || contextSettings.MaxEventsCount <= 0)
            return null;

        int currentTick = Find.TickManager?.TicksGame ?? 0;
        var candidates = new List<ColonyEventCandidate>();
        var seenLetters = new HashSet<Letter>();

        // 1. Collect Active Letters from LetterStack
        var letterStack = Find.LetterStack;
        if (letterStack?.LettersListForReading != null)
        {
            foreach (var letter in letterStack.LettersListForReading)
            {
                if (letter == null || !seenLetters.Add(letter)) continue;
                if (!IsLetterMapRelevant(letter, map) || IsInspirationLetter(letter)) continue;

                var label = letter.Label.ToString().Trim().StripTags();
                if (string.IsNullOrEmpty(label)) continue;

                int arrivalTick = letter.arrivalTick > 0 ? letter.arrivalTick : currentTick;
                int elapsedTicks = Mathf.Max(0, currentTick - arrivalTick);
                float elapsedHours = elapsedTicks / 2500f;

                int baseScore = GetEventBaseScore(letter, label);
                float maxAllowedHours = GetEventMaxRetentionHours(letter, label);
                if (elapsedHours > maxAllowedHours) continue;

                int score = CalculateEventScore(letter, label, baseScore, elapsedTicks, true, currentTick);

                candidates.Add(new ColonyEventCandidate
                {
                    Letter = letter,
                    Label = label,
                    ArrivalTick = arrivalTick,
                    ElapsedTicks = elapsedTicks,
                    ElapsedHours = elapsedHours,
                    IsActiveOnScreen = true,
                    Score = score
                });
            }
        }

        // 2. Collect Past Letters from Archive
        var archive = Find.Archive;
        if (archive?.ArchivablesListForReading != null)
        {
            foreach (var archivable in archive.ArchivablesListForReading)
            {
                if (archivable is not Letter letter || !seenLetters.Add(letter)) continue;
                if (!IsLetterMapRelevant(letter, map) || IsInspirationLetter(letter)) continue;

                var label = letter.Label.ToString().Trim().StripTags();
                if (string.IsNullOrEmpty(label)) continue;

                int arrivalTick = letter.arrivalTick > 0 ? letter.arrivalTick : archivable.CreatedTicksGame;
                if (arrivalTick <= 0) arrivalTick = currentTick;
                int elapsedTicks = Mathf.Max(0, currentTick - arrivalTick);
                float elapsedHours = elapsedTicks / 2500f;

                int baseScore = GetEventBaseScore(letter, label);
                float maxAllowedHours = GetEventMaxRetentionHours(letter, label);

                if (elapsedHours > maxAllowedHours) continue;

                int score = CalculateEventScore(letter, label, baseScore, elapsedTicks, false, currentTick);

                candidates.Add(new ColonyEventCandidate
                {
                    Letter = letter,
                    Label = label,
                    ArrivalTick = arrivalTick,
                    ElapsedTicks = elapsedTicks,
                    ElapsedHours = elapsedHours,
                    IsActiveOnScreen = false,
                    Score = score
                });
            }
        }

        if (candidates.Count == 0) return null;

        // Separate candidates into Recent (< 12h) vs Past Important (>= 12h)
        var recentPool = candidates
            .Where(c => c.ElapsedHours < 12f)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.ArrivalTick)
            .ToList();

        var pastPool = candidates
            .Where(c => c.ElapsedHours >= 12f)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.ArrivalTick)
            .ToList();

        int totalMax = contextSettings.MaxEventsCount;
        var selected = new List<ColonyEventCandidate>();

        // 1. Prioritize fresh events (< 12h or active on screen)
        var takenRecent = recentPool.Take(totalMax).ToList();
        selected.AddRange(takenRecent);

        // 2. If slots remain, fill with significant past events (>= 12h)
        if (selected.Count < totalMax)
        {
            var takenPast = pastPool.Take(totalMax - selected.Count).ToList();
            selected.AddRange(takenPast);
        }

        // Final display ordering: Recent first, then past
        var orderedSelection = selected
            .OrderBy(c => c.ElapsedTicks)
            .ToList();

        var formattedLines = new List<string>(orderedSelection.Count);
        foreach (var c in orderedSelection)
        {
            string timeStr = FormatElapsedTime(c.ElapsedTicks);
            formattedLines.Add($"{c.Label} ({timeStr})");
        }

        if (currentTick > 0 && orderedSelection.Count > 0)
        {
            foreach (var c in orderedSelection)
            {
                RecentInjectedTicks[GetEventKey(c.Letter, c.Label)] = currentTick;
            }

            if (RecentInjectedTicks.Count > 40)
            {
                foreach (var kvp in RecentInjectedTicks)
                {
                    if (currentTick - kvp.Value > 120000)
                        RecentInjectedTicks.TryRemove(kvp.Key, out _);
                }
            }
        }

        return formattedLines.Count > 0 ? string.Join("\n", formattedLines) : null;
    }

    private static string GetEventKey(Letter letter, string label)
    {
        if (letter != null && letter.ID > 0)
            return $"{letter.ID}_{label}";
        return label ?? "";
    }

    private static int CalculateEventScore(Letter letter, string label, int baseScore, int elapsedTicks, bool isActiveOnScreen, int currentTick)
    {
        int score = baseScore + (isActiveOnScreen ? 200 : 0) - (elapsedTicks / 1000);
        if (currentTick > 0)
        {
            string key = GetEventKey(letter, label);
            if (RecentInjectedTicks.TryGetValue(key, out int lastTick))
            {
                int diff = currentTick - lastTick;
                if (diff >= 0 && diff < FatigueDurationTicks)
                {
                    score -= FatiguePenaltyScore;
                }
            }
        }
        return score;
    }

    private static bool IsLetterMapRelevant(Letter letter, Map map)
    {
        if (map == null || letter.lookTargets is not { Any: true }) return true;
        var targetMaps = letter.lookTargets.targets
            .Select(t => t.Map)
            .Where(m => m != null)
            .Distinct()
            .ToList();
        return targetMaps.Count == 0 || targetMaps.Contains(map);
    }

    private static int GetEventBaseScore(Letter letter, string label)
    {
        if (letter is DeathLetter ||
            letter.def == LetterDefOf.ThreatBig ||
            letter.def == LetterDefOf.ThreatSmall ||
            letter.def == LetterDefOf.Death ||
            (letter.def?.defName != null && letter.def.defName.IndexOf("threat", StringComparison.OrdinalIgnoreCase) >= 0))
            return 1000;

        if (letter.def == LetterDefOf.NegativeEvent ||
            (letter.def?.defName != null && letter.def.defName.IndexOf("negative", StringComparison.OrdinalIgnoreCase) >= 0) ||
            label.IndexOf("pregnan", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("birth", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("crash", StringComparison.OrdinalIgnoreCase) >= 0 ||
            label.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            return 500;

        return 100;
    }

    private static float GetEventMaxRetentionHours(Letter letter, string label)
    {
        int baseScore = GetEventBaseScore(letter, label);
        if (baseScore >= 1000) return 48f; // Critical: 2 days (48 hours)
        if (baseScore >= 500) return 24f;  // Major: 1 day (24 hours)
        return 12f; // Minor: 12 hours
    }

    private static bool IsInspirationLetter(Letter letter)
    {
        if (letter?.def != LetterDefOf.PositiveEvent) return false;

        string label = letter?.Label.ToString();
        if (string.IsNullOrEmpty(label)) return false;

        var defs = DefDatabase<InspirationDef>.AllDefsListForReading;
        if (defs == null) return false;

        foreach (var def in defs)
        {
            if (def == null) continue;

            string prefix = !def.beginLetterLabel.NullOrEmpty()
                ? def.beginLetterLabel.CapitalizeFirst()
                : def.LabelCap.Resolve().CapitalizeFirst();

            if (!string.IsNullOrEmpty(prefix) && label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;   
    }

    public static string FormatElapsedTime(int elapsedTicks)
    {
        float hours = elapsedTicks / 2500f;
        if (hours < 24f)
        {
            int displayHours = Mathf.Max(1, (int)hours);
            return $"{displayHours}h ago";
        }
        int days = Mathf.Max(1, (int)(elapsedTicks / 60000f));
        return $"{days}d ago";
    }
}
