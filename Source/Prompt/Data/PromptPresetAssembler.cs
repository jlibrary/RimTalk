using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;

namespace RimTalk.Prompt;

/// <summary>
/// Assembles preset entries into a sequential message list with proper role resolution,
/// InChat depth anchoring relative to chat history, and consecutive role merging.
/// </summary>
internal static class PromptPresetAssembler
{
    internal static PromptRole GetEffectiveRole(PromptEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.CustomRole) ? entry.Role : PromptRole.User;
    }

    internal static string ApplyCustomRolePrefix(PromptEntry entry, string content)
    {
        if (string.IsNullOrWhiteSpace(entry.CustomRole)) return content;
        return $"[role: {entry.CustomRole}]\n{content}";
    }

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

        // 1. Process Relative entries in defined order (System/History/Prompt)
        foreach (var entry in preset.Entries.Where(e => e.Enabled && e.Position == PromptPosition.Relative))
        {
            if (entry.IsMainChatHistory)
            {
                if (chatHistory != null)
                {
                    foreach (var (role, message) in chatHistory)
                    {
                        var pRole = (PromptRole)role;
                        result.Add((pRole, message));
                        segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "History", role, message));
                    }
                }

                if (!boundarySet) { systemBoundary = result.Count; boundarySet = true; }
                lastHistoryIndex = result.Count;
                continue;
            }

            var content = renderFunc != null ? renderFunc(entry.Content) : entry.Content;
            if (!string.IsNullOrWhiteSpace(content))
            {
                var role = GetEffectiveRole(entry);
                var finalContent = ApplyCustomRolePrefix(entry, content);

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
                var role = GetEffectiveRole(entry);
                var finalContent = ApplyCustomRolePrefix(entry, content);

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
