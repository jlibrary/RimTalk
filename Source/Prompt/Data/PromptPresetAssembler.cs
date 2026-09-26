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
    public const string ChatHistoryHeader = "[Chat History]\n(Past dialogue. Advance the scene naturally without echoing earlier lines):";
    public const string CurrentTaskHeader = "[Situation]";
    public const string SituationHeader = CurrentTaskHeader;
    public const string DefaultRecentEventsInstruction = """
                                                          {{- if events && events != "" -}}
                                                          [Recent Events]
                                                          (Recent colony incidents; let them naturally shape mood, tone, or thoughts if still relevant, never force):
                                                          {{ events }}
                                                          {{- end -}}
                                                          """;

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
                if (chatHistory != null && chatHistory.Count > 0)
                {
                    // Default to single-block history with inline causal triggers (Approach B') to minimize
                    // token overhead while preventing repetitive degenerations. Approach A (multi-turn) is opted via 'history_raw'.
                    // See: Docs/dialogue-history-architecture.md
                    bool isRaw = entry.Content?.IndexOf("history_raw", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isRaw)
                    {
                        foreach (var (role, message) in chatHistory)
                        {
                            if (string.IsNullOrWhiteSpace(message)) continue;
                            var pRole = (PromptRole)role;
                            result.Add((pRole, message));
                            segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "History", role, message) { IsHistory = true });
                        }
                    }
                    else
                    {
                        var lines = new List<string>();
                        foreach (var (_, message) in chatHistory)
                        {
                            if (string.IsNullOrWhiteSpace(message)) continue;
                            lines.Add(message.Trim());
                        }

                        if (lines.Count > 0)
                        {
                            string historyContent = $"{ChatHistoryHeader}\n{string.Join("\n", lines)}";
                            result.Add((PromptRole.User, historyContent));
                            segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "History", Role.User, historyContent) { IsHistory = true });
                        }
                    }
                }

                if (!boundarySet) { systemBoundary = result.Count; boundarySet = true; }
                lastHistoryIndex = result.Count;
                continue;
            }

            var content = renderFunc != null ? renderFunc(entry.Content) : entry.Content;
            content = content?.Trim();
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

        // Trailing format anchor: reinforces JSON format at the very end of user input without polluting dialogue history
        var activeJsonEntry = preset.Entries.FirstOrDefault(e => e.Enabled && string.Equals(e.Name, "JSON Format", StringComparison.OrdinalIgnoreCase));
        if (activeJsonEntry != null)
        {
            var rawContent = activeJsonEntry.Content;
            if (rawContent != null && string.Equals(rawContent.Trim().Replace(" ", ""), "{{json.format}}", StringComparison.OrdinalIgnoreCase))
            {
                rawContent = "{{ json.anchor }}";
            }

            var content = renderFunc != null ? renderFunc(rawContent) : rawContent;
            content = content?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                result.Add((PromptRole.User, content));
                segments?.Add(new PromptMessageSegment("format-reminder", "JSON Format Reminder", Role.User, content));
            }
        }

        if (!boundarySet) systemBoundary = result.Count;

        // 2. Process InChat entries (Anchored to History)
        foreach (var entry in preset.GetInChatEntries())
        {
            var content = renderFunc != null ? renderFunc(entry.Content) : entry.Content;
            content = content?.Trim();
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
        string fallbackInstruction = "",
        string fallbackJsonInstruction = "")
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
        bool hasJsonFormat = false;
        bool hasContext = false;
        bool hasChatHistory = false;
        bool hasRecentEvents = false;
        bool hasDialoguePrompt = false;

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
                    clone.Enabled = true;
                    if (string.Equals(clone.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase))
                    {
                        clone.Content = effectiveInstruction;
                        hasBaseInstruction = true;
                    }

                    if (string.Equals(clone.Name, "JSON Format", StringComparison.OrdinalIgnoreCase))
                    {
                        hasJsonFormat = true;
                    }

                    if (clone.IsMainChatHistory)
                    {
                        hasChatHistory = true;
                    }

                    if (string.Equals(clone.Name, "Context", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(clone.Name, "Pawn Profiles", StringComparison.OrdinalIgnoreCase))
                    {
                        hasContext = true;
                    }

                    if (string.Equals(clone.Name, "Recent Events", StringComparison.OrdinalIgnoreCase))
                    {
                        hasRecentEvents = true;
                    }

                    if (string.Equals(clone.Name, "Dialogue Prompt", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDialoguePrompt = true;
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

        if (!hasJsonFormat)
        {
            int baseIndex = simplePreset.Entries.FindIndex(e =>
                string.Equals(e.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase));
            int insertIndex = baseIndex >= 0 ? baseIndex + 1 : 0;

            simplePreset.Entries.Insert(insertIndex, new PromptEntry
            {
                Name = "JSON Format",
                Role = PromptRole.System,
                Position = PromptPosition.Relative,
                Content = !string.IsNullOrEmpty(fallbackJsonInstruction)
                    ? fallbackJsonInstruction
                    : "{{ json.format }}"
            });
        }

        if (!hasContext)
        {
            int insertIndex = simplePreset.Entries.FindLastIndex(e =>
                string.Equals(e.Name, "JSON Format", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase)) + 1;

            simplePreset.Entries.Insert(Math.Max(0, insertIndex), new PromptEntry
            {
                Name = "Context",
                Role = PromptRole.System,
                Position = PromptPosition.Relative,
                Content = "{{context}}"
            });
        }

        if (!hasRecentEvents)
        {
            int histIndex = simplePreset.Entries.FindIndex(e =>
                e.IsMainChatHistory || string.Equals(e.Name, "Chat History", StringComparison.OrdinalIgnoreCase));
            int dpIndex = simplePreset.Entries.FindIndex(e =>
                string.Equals(e.Name, "Dialogue Prompt", StringComparison.OrdinalIgnoreCase));
            int insertIndex = histIndex >= 0 ? histIndex : (dpIndex >= 0 ? dpIndex : simplePreset.Entries.Count);

            simplePreset.Entries.Insert(insertIndex, new PromptEntry
            {
                Name = "Recent Events",
                Role = PromptRole.User,
                Position = PromptPosition.Relative,
                Content = DefaultRecentEventsInstruction
            });
        }

        if (!hasChatHistory)
        {
            int dpIndex = simplePreset.Entries.FindIndex(e =>
                string.Equals(e.Name, "Dialogue Prompt", StringComparison.OrdinalIgnoreCase));
            int histIndex = dpIndex >= 0 ? dpIndex : simplePreset.Entries.Count;

            simplePreset.Entries.Insert(histIndex, new PromptEntry
            {
                Name = "Chat History",
                Role = PromptRole.User,
                Position = PromptPosition.Relative,
                IsMainChatHistory = true,
                Content = "{{chat.history}}"
            });
        }

        if (!hasDialoguePrompt)
        {
            simplePreset.Entries.Add(new PromptEntry
            {
                Name = "Dialogue Prompt",
                Role = PromptRole.User,
                Position = PromptPosition.Relative,
                Content = "{{prompt}}"
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

    private static bool IsBuiltInEntry(PromptEntry entry)
    {
        if (entry == null) return false;
        if (entry.IsMainChatHistory) return true;
        return string.Equals(entry.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "JSON Format", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "Context", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "Pawn Profiles", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "Recent Events", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, "Dialogue Prompt", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldShowHistoryWarning(PromptPreset preset, bool isExternalMemoryModActive = false)
    {
        if (preset?.Entries == null || isExternalMemoryModActive) return false;

        bool hasExternalModEntry = false;
        for (int i = 0; i < preset.Entries.Count; i++)
        {
            var e = preset.Entries[i];
            if (!e.Enabled) continue;

            if (e.IsMainChatHistory || (!string.IsNullOrEmpty(e.Content) &&
                (e.Content.IndexOf("chat.history", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 e.Content.IndexOf("ctx.history", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(e.SourceModId))
                hasExternalModEntry = true;
        }

        return !hasExternalModEntry;
    }
}

