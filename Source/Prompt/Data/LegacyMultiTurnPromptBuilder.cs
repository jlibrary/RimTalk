using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;

namespace RimTalk.Prompt;

/// <summary>
/// Legacy v1.2.15 multi-turn prompt builder.
/// Isolated in this dedicated class so the main pipeline (PromptPresetAssembler)
/// remains clean and this legacy implementation can be safely deleted at any time without ripple effects.
/// </summary>
internal static class LegacyMultiTurnPromptBuilder
{
    internal static List<(PromptRole role, string content)> AssembleMessages(
        PromptPreset preset,
        Func<string, string> renderFunc,
        List<(Role role, string message)> chatHistory,
        List<PromptMessageSegment> segments = null)
    {
        var result = new List<(PromptRole role, string content)>();
        int lastHistoryIndex = 0;
        int systemBoundary = 0;
        bool boundarySet = false;

        if (preset?.Entries == null) return result;

        // 1. Process Relative entries in defined order (System/History/Prompt)
        foreach (var entry in preset.Entries.Where(e => e.Enabled && e.Position == PromptPosition.Relative))
        {
            if (entry.IsMainChatHistory)
            {
                if (chatHistory != null && chatHistory.Count > 0)
                {
                    int historyStartIndex = result.Count;

                    // Ensure strictly alternating User -> Assistant history turns (v1.2.15 contract)
                    Role expectedRole = Role.User;
                    for (int i = 0; i < chatHistory.Count; i++)
                    {
                        var (role, message) = chatHistory[i];
                        if (string.IsNullOrWhiteSpace(message)) continue;

                        if (role == expectedRole)
                        {
                            var pRole = (PromptRole)role;
                            result.Add((pRole, message));
                            segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "History", role, message) { IsHistory = true });
                            expectedRole = (role == Role.User) ? Role.AI : Role.User;
                        }
                    }

                    // If history ended on a User message, trim it so subsequent User prompt does not collide
                    if (result.Count > historyStartIndex && result[^1].role == PromptRole.User)
                    {
                        result.RemoveAt(result.Count - 1);
                        if (segments != null && segments.Count > 0 && segments[^1].IsHistory)
                            segments.RemoveAt(segments.Count - 1);
                    }
                }

                if (!boundarySet) { systemBoundary = result.Count; boundarySet = true; }
                lastHistoryIndex = result.Count;
                continue;
            }

            var content = renderFunc != null ? renderFunc(entry.Content) : entry.Content;
            if (!string.IsNullOrWhiteSpace(content))
            {
                var role = PromptPresetAssembler.GetEffectiveRole(entry);
                var finalContent = PromptPresetAssembler.ApplyCustomRolePrefix(entry, content);

                result.Add((role, finalContent));
                segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "Entry", (Role)role, finalContent));

                if (!boundarySet && role != PromptRole.System)
                {
                    systemBoundary = result.Count - 1;
                    boundarySet = true;
                }
            }
        }

        if (!boundarySet) systemBoundary = result.Count;

        // 2. Process InChat entries (Anchored to History)
        foreach (var entry in preset.GetInChatEntries())
        {
            var content = renderFunc != null ? renderFunc(entry.Content) : entry.Content;
            if (!string.IsNullOrWhiteSpace(content))
            {
                var role = PromptPresetAssembler.GetEffectiveRole(entry);
                var finalContent = PromptPresetAssembler.ApplyCustomRolePrefix(entry, content);

                var insertIndex = Math.Max(systemBoundary, lastHistoryIndex - entry.InChatDepth);

                result.Insert(insertIndex, (role, finalContent));
                segments?.Insert(insertIndex, new PromptMessageSegment(entry.Id, entry.Name ?? "Entry", (Role)role, finalContent));

                if (insertIndex <= lastHistoryIndex) lastHistoryIndex++;
                systemBoundary++;
            }
        }

        return PromptMessageRoleMerger.MergeConsecutiveRoles(result, lastHistoryIndex);
    }
}
