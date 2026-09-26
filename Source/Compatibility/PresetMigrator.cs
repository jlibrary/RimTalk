using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Prompt;

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

            // 1. Fix legacy chat history markers
            if (!preset.Entries.Any(e => e.IsMainChatHistory))
            {
                var legacyEntry = preset.Entries.FirstOrDefault(e => e.Content?.Trim() == "{{chat.history}}");
                legacyEntry?.IsMainChatHistory = true;
            }

            // 2. Migrate unmodified legacy JSON format entries to {{ json.format }}
            var jsonEntry = preset.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "JSON Format", StringComparison.OrdinalIgnoreCase));
            if (jsonEntry != null)
            {
                var normalized = jsonEntry.Content?.Trim().Replace("\r\n", "\n");
                if (normalized != null && (string.Equals(normalized, LegacyJsonFormatInstruction.Trim().Replace("\r\n", "\n"), StringComparison.Ordinal)
                                           || normalized.Contains(DirectiveSnippet)))
                {
                    jsonEntry.Content = "{{ json.format }}";
                }
            }

            // 3. Remove obsolete legacy custom instructions
            preset.Entries.RemoveAll(e =>
                string.Equals(e.Name, "Legacy Custom Instruction", StringComparison.OrdinalIgnoreCase));
        }
    }
}
