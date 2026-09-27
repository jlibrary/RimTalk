using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Prompt;
using RimTalk.Data;

namespace RimTalk.Compatibility;

/// <summary>
/// Handles legacy preset and schema migrations for existing saves and config files.
/// Kept isolated here so legacy migration code can be reviewed or removed cleanly in the future.
/// </summary>
internal static class PresetMigrator
{
    private const string LegacyJsonInstruction = """
                                                 Output JSONL.
                                                 Required keys: "name", "text".
                                                 """;

    private const string LegacySocialInstruction = """
                                                   Optional keys (Include only if social interaction occurs):
                                                   "act": Insult, Slight, Chat, Kind
                                                   "target": targetName
                                                   """;

    private const string LegacyJsonFormatInstruction = LegacyJsonInstruction
                                                       + "\n{{ if settings.ApplyMoodAndSocialEffects }}\n" + LegacySocialInstruction + "\n{{ end }}";

    private const string DirectiveSnippet = "Optional keys (Include only if player orders, rules, behavioral instructions, or memorable facts were given or changed)";

    /// <summary>
    /// Migrates legacy preset data to current standards upon loading settings.
    /// </summary>
    public static void Migrate(List<PromptPreset> presets)
    {
        if (presets == null || presets.Count == 0) return;

        foreach (var preset in presets)
        {
            if (preset?.Entries == null) continue;

            // 1. Fix legacy chat history markers and backfill canonical IDs
            foreach (var entry in preset.Entries)
            {
                if (entry == null) continue;

                if (!entry.IsMainChatHistory && entry.Content?.Trim() == "{{chat.history}}")
                {
                    entry.IsMainChatHistory = true;
                }

                if (string.IsNullOrEmpty(entry.SourceModId))
                {
                    if (entry.IsMainChatHistory || string.Equals(entry.Name, BuiltInPromptNames.ChatHistory, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Id = BuiltInPromptIds.ChatHistory;
                        entry.IsMainChatHistory = true;
                    }
                    else if (string.Equals(entry.Name, BuiltInPromptNames.BaseInstruction, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Id = BuiltInPromptIds.BaseInstruction;
                    }
                    else if (string.Equals(entry.Name, BuiltInPromptNames.JsonFormat, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Id = BuiltInPromptIds.JsonFormat;
                    }
                    else if (string.Equals(entry.Name, BuiltInPromptNames.Context, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(entry.Name, BuiltInPromptNames.PawnProfiles, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Id = BuiltInPromptIds.Context;
                    }
                    else if (string.Equals(entry.Name, BuiltInPromptNames.RecentEvents, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Id = BuiltInPromptIds.RecentEvents;
                    }
                    else if (string.Equals(entry.Name, BuiltInPromptNames.DialoguePrompt, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Id = BuiltInPromptIds.DialoguePrompt;
                    }
                }
            }

            // 2. Migrate unmodified legacy JSON format entries to {{ json.format }}
            var jsonEntry = preset.Entries.FirstOrDefault(e => e.IsJsonFormat);
            if (jsonEntry != null)
            {
                var normalized = jsonEntry.Content?.Trim().Replace("\r\n", "\n");
                if (normalized != null && (string.Equals(normalized, LegacyJsonFormatInstruction.Trim().Replace("\r\n", "\n"), StringComparison.Ordinal)
                                           || normalized.Contains(DirectiveSnippet)))
                {
                    jsonEntry.Content = BuiltInPromptTokens.JsonFormat;
                }
            }

            // 3. Remove obsolete legacy custom instructions
            preset.Entries.RemoveAll(e =>
                string.Equals(e.Name, "Legacy Custom Instruction", StringComparison.OrdinalIgnoreCase));
        }
    }
}
