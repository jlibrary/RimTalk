using System;
using System.Collections.Generic;
using System.Text;

namespace RimTalk.Memory;

/// <summary>
/// Formats retrieved memories into token-minimal, attitude-focused prompt directives
/// designed to guide LLM behavior without causing literal repetition/echoing.
/// </summary>
public static class MemoryFormatter
{
    /// <summary>
    /// Formats selected top memories into a token-minimal, fact-based memory header without artificial emotional boilerplate.
    /// Format: Memory with {targetName}: {event1}, {event2}
    /// </summary>
    public static string FormatMemories(string targetName, List<MemoryEntry> topMemories)
    {
        if (topMemories == null || topMemories.Count == 0)
            return string.Empty;

        if (string.IsNullOrEmpty(targetName))
            targetName = "them";

        var sb = new StringBuilder(64);
        sb.Append("Memory with ").Append(targetName).Append(": ");
        bool hasItem = false;
        foreach (var t in topMemories)
        {
            var note = CleanNote(t?.Note);
            if (!string.IsNullOrWhiteSpace(note))
            {
                if (hasItem) sb.Append(", ");
                sb.Append(note);
                hasItem = true;
            }
        }

        return hasItem ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Formats selected top personal deed/crisis memories into a token-minimal header.
    /// Format: Recent Experience: {note1}, {note2}
    /// </summary>
    public static string FormatPersonalMemories(List<MemoryEntry> personalMemories)
    {
        if (personalMemories == null || personalMemories.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(64);
        sb.Append("Recent Experience: ");
        bool hasItem = false;
        foreach (var t in personalMemories)
        {
            var note = CleanNote(t?.Note);
            if (!string.IsNullOrWhiteSpace(note))
            {
                if (hasItem) sb.Append(", ");
                sb.Append(note);
                hasItem = true;
            }
        }

        return hasItem ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Formats selected positive and negative memories cleanly.
    /// Maintained for backwards compatibility.
    /// </summary>
    public static string FormatImpression(string targetName, MemoryEntry positive, MemoryEntry negative)
    {
        var list = new List<MemoryEntry>();
        if (positive != null && !string.IsNullOrWhiteSpace(positive.Note)) list.Add(positive);
        if (negative != null && !string.IsNullOrWhiteSpace(negative.Note)) list.Add(negative);
        return FormatMemories(targetName, list);
    }

    /// <summary>
    /// Formats prominent core traumas or deep grief into an all-pervading mental disposition.
    /// Combines multiple severe losses (e.g. spouse and children) naturally.
    /// </summary>
    public static string FormatCoreTraumas(List<MemoryEntry> traumas)
    {
        if (traumas == null || traumas.Count == 0)
            return string.Empty;

        if (traumas.Count == 1)
            return FormatCoreTrauma(traumas[0]);

        var sb = new StringBuilder(128);
        sb.Append("Core Mindset: Deeply burdened by ");
        for (int i = 0; i < traumas.Count; i++)
        {
            if (i > 0)
            {
                if (i == traumas.Count - 1)
                    sb.Append(", and ");
                else
                    sb.Append(", ");
            }
            sb.Append(CleanNote(traumas[i].Note));
        }
        sb.Append(" (emotionally shaken, grieving, subdued).");
        return sb.ToString();
    }

    /// <summary>
    /// Formats a prominent core trauma or deep grief (e.g. death of a close friend/spouse)
    /// into an all-pervading mental disposition.
    /// </summary>
    public static string FormatCoreTrauma(MemoryEntry trauma)
    {
        if (trauma == null || string.IsNullOrWhiteSpace(trauma.Note))
            return string.Empty;

        string note = CleanNote(trauma.Note);

        // Callous / positive disposition (e.g. bloodlust killer)
        if (trauma.BaseWeight > 0)
        {
            return $"Core Mindset: Marked by {note} (callous, remorseless).";
        }

        // Homicide / violent deed with guilt
        if (trauma.EventKey != null && (trauma.EventKey.IndexOf("Kill", StringComparison.OrdinalIgnoreCase) >= 0 || trauma.EventKey.IndexOf("Execut", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return $"Core Mindset: Marked by {note} (guilty, defensive).";
        }

        // Standard grief and loss
        return $"Core Mindset: Deeply burdened by {note} (subdued, shaken).";
    }

    /// <summary>
    /// Formats active direct orders given by the player into bulleted instructions.
    /// </summary>
    public static string FormatDirectives(List<MemoryEntry> directives)
    {
        if (directives == null || directives.Count == 0)
            return string.Empty;

        if (directives.Count == 1)
            return FormatDirective(directives[0]);

        var sb = new StringBuilder(128);
        sb.AppendLine("Active Directives:");
        for (int i = 0; i < directives.Count; i++)
        {
            sb.Append("- ").AppendLine(CleanNote(directives[i].Note));
        }
        sb.Append("(Follow these behavioral directives during your actions and words.)");
        return sb.ToString();
    }

    /// <summary>
    /// Formats active direct orders given to this pawn.
    /// </summary>
    public static string FormatDirective(MemoryEntry directive)
    {
        if (directive == null || string.IsNullOrWhiteSpace(directive.Note))
            return string.Empty;

        return $"Active Directive: \"{CleanNote(directive.Note)}\". (Follow this directive during your actions and words.)";
    }

    /// <summary>
    /// Formats a permanent relationship milestone into an attitude-guiding directive that prevents rote recitation.
    /// </summary>
    public static string FormatMilestone(MemoryEntry milestone, string targetName)
    {
        if (milestone == null || string.IsNullOrWhiteSpace(milestone.Note))
            return string.Empty;

        if (string.IsNullOrEmpty(targetName))
            targetName = "them";

        string cleanNote = CleanNote(milestone.Note);
        return $"Milestone: {cleanNote}. (Treat {targetName} with profound, unspoken depth shaped by this shared history; do not recite or quote this past event out of context.)";
    }

    /// <summary>
    /// Formats colony seniority between two pawns.
    /// Distinguishes between warm bond and bitter coexistence based on opinion.
    /// </summary>
    public static string FormatSeniority(int years, int opinion)
    {
        if (years < 1) return string.Empty;
        string yearStr = years == 1 ? "1 year" : $"{years} years";
        if (opinion >= 0)
            return $"Bond: {yearStr} together in colony";
        return $"History: {yearStr} of bitter coexistence in colony";
    }

    private static string CleanNote(string note)
    {
        if (string.IsNullOrEmpty(note)) return string.Empty;
        var trimmed = PawnMemoryTracker.StripBoilerplate(note);
        if (trimmed.EndsWith("."))
            return trimmed.Substring(0, trimmed.Length - 1).Trim();
        return trimmed;
    }
}
