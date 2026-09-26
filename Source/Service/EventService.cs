using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
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
    /// <summary>
    /// Checks if a known external event-handling addon mod is active.
    /// </summary>
    public static bool IsExternalEventModActive =>
        ModsConfig.IsActive("saltgin.rimtalkeventmemory");

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
                if (!IsLetterMapRelevant(letter, map)) continue;

                var label = letter.Label.ToString().Trim().StripTags();
                if (string.IsNullOrEmpty(label)) continue;

                int arrivalTick = letter.arrivalTick > 0 ? letter.arrivalTick : currentTick;
                int elapsedTicks = Mathf.Max(0, currentTick - arrivalTick);
                float elapsedHours = elapsedTicks / 2500f;

                int baseScore = GetEventBaseScore(letter, label);
                int score = baseScore + 200 - (elapsedTicks / 1000);

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
                if (!IsLetterMapRelevant(letter, map)) continue;

                var label = letter.Label.ToString().Trim().StripTags();
                if (string.IsNullOrEmpty(label)) continue;

                int arrivalTick = letter.arrivalTick > 0 ? letter.arrivalTick : archivable.CreatedTicksGame;
                if (arrivalTick <= 0) arrivalTick = currentTick;
                int elapsedTicks = Mathf.Max(0, currentTick - arrivalTick);
                float elapsedHours = elapsedTicks / 2500f;

                int baseScore = GetEventBaseScore(letter, label);
                float maxAllowedHours = GetEventMaxRetentionHours(letter, label);

                if (elapsedHours > maxAllowedHours) continue;

                int score = baseScore - (elapsedTicks / 1000);

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

        // Separate candidates into Recent (< 12h or Active on screen) vs Past Important (>= 12h)
        var recentPool = candidates
            .Where(c => c.IsActiveOnScreen || c.ElapsedHours < 12f)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.ArrivalTick)
            .ToList();

        var pastPool = candidates
            .Where(c => !c.IsActiveOnScreen && c.ElapsedHours >= 12f)
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

        var formattedLines = new List<string>();
        foreach (var c in orderedSelection)
        {
            string timeStr = FormatElapsedTime(c.ElapsedTicks);

            // Fresh event (< 12 in-game hours): Include concise 1-line summary if available
            if (c.ElapsedHours < 12f && infoLevel != PromptService.InfoLevel.Short)
            {
                string summary = null;
                try
                {
                    summary = (c.Letter as IArchivable)?.ArchivedTooltip?.StripTags()?.Trim();
                }
                catch { }

                if (!string.IsNullOrEmpty(summary) && !string.Equals(summary, c.Label, StringComparison.OrdinalIgnoreCase))
                {
                    var firstLine = summary.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(firstLine) && !string.Equals(firstLine, c.Label, StringComparison.OrdinalIgnoreCase))
                    {
                        if (firstLine.Length > 55)
                            firstLine = firstLine.Substring(0, 52) + "...";
                        formattedLines.Add($"{c.Label} ({timeStr}): {firstLine}");
                        continue;
                    }
                }
            }

            // Older memory (>= 12 hours) or short mode: Title with time only
            formattedLines.Add($"{c.Label} ({timeStr})");
        }

        return formattedLines.Count > 0 ? string.Join("\n", formattedLines) : null;
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
        return 6f; // Minor: 6 hours
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

    public static string FormatElapsedTime(int elapsedTicks, bool isKorean)
    {
        return FormatElapsedTime(elapsedTicks);
    }
}
