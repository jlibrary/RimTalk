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

    internal static PromptPreset BuildSimpleModePreset(
        PromptPreset activePreset,
        string simpleInstruction,
        string fallbackInstruction = "")
    {
        var simplePreset = new PromptPreset
        {
            Id = activePreset?.Id ?? Guid.NewGuid().ToString(),
            Name = activePreset?.Name ?? "Simple Mode Preset",
            Description = activePreset?.Description ?? "",
            IsActive = true,
            Entries = new List<PromptEntry>()
        };

        bool hasBaseInstruction = false;
        bool hasChatHistory = false;

        string effectiveInstruction = !string.IsNullOrWhiteSpace(simpleInstruction)
            ? simpleInstruction
            : (!string.IsNullOrWhiteSpace(fallbackInstruction) ? fallbackInstruction : "");

        if (activePreset?.Entries != null)
        {
            foreach (var entry in activePreset.Entries)
            {
                if (IsBuiltInEntry(entry))
                {
                    var clone = ShallowCopyEntry(entry);
                    if (string.Equals(clone.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase))
                    {
                        clone.Content = effectiveInstruction;
                        hasBaseInstruction = true;
                    }

                    if (clone.IsMainChatHistory)
                    {
                        hasChatHistory = true;
                    }

                    simplePreset.Entries.Add(clone);
                }
                else if (!string.IsNullOrEmpty(entry.SourceModId))
                {
                    // Addon mod entry: attach to simple mode
                    simplePreset.Entries.Add(ShallowCopyEntry(entry));
                }
                // User-created custom entries (SourceModId is null/empty and not built-in) are excluded
            }
        }

        if (!hasBaseInstruction)
        {
            simplePreset.Entries.Insert(0, new PromptEntry
            {
                Name = "Base Instruction",
                Role = PromptRole.System,
                Position = PromptPosition.Relative,
                Content = effectiveInstruction
            });
        }

        if (!hasChatHistory)
        {
            simplePreset.Entries.Add(new PromptEntry
            {
                Name = "Chat History",
                Role = PromptRole.User,
                Position = PromptPosition.Relative,
                IsMainChatHistory = true,
                Content = "{{chat.history}}"
            });
        }

        return simplePreset;
    }

    internal static PromptEntry ShallowCopyEntry(PromptEntry entry)
    {
        return new PromptEntry
        {
            Id = entry.Id,
            Name = entry.Name,
            Content = entry.Content,
            Role = entry.Role,
            CustomRole = entry.CustomRole,
            Position = entry.Position,
            InChatDepth = entry.InChatDepth,
            Enabled = entry.Enabled,
            IsMainChatHistory = entry.IsMainChatHistory,
            SourceModId = entry.SourceModId
        };
    }

    internal static bool IsBuiltInEntry(PromptEntry entry)
    {
        if (entry == null) return false;
        if (entry.IsMainChatHistory) return true;
        return string.Equals(entry.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "JSON Format", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "Context", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "Dialogue Prompt", StringComparison.OrdinalIgnoreCase);
    }
}
