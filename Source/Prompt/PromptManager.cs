using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Prompt manager - handles presets, variables, and builds final prompts.
/// Stored in global settings (shared across all saves).
/// </summary>
public class PromptManager : IExposable
{
    private static PromptManager _instance;
    
    /// <summary>Singleton instance</summary>
    public static PromptManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new PromptManager();
                _instance.InitializeDefaults();
            }
            return _instance;
        }
    }

    /// <summary>All presets</summary>
    public List<PromptPreset> Presets = new();
    
    /// <summary>Global variable store</summary>
    public VariableStore VariableStore = new();
    
    /// <summary>Player custom variables</summary>
    public List<CustomVariable> CustomVariables = new();

    /// <summary>Gets the currently active preset</summary>
    public PromptPreset GetActivePreset()
    {
        var active = Presets.FirstOrDefault(p => p.IsActive);
        if (active == null && Presets.Count > 0)
        {
            // If no preset is active, activate the first one
            Presets[0].IsActive = true;
            return Presets[0];
        }
        return active;
    }

    /// <summary>Sets the active preset</summary>
    public void SetActivePreset(string presetId)
    {
        foreach (var preset in Presets)
        {
            preset.IsActive = preset.Id == presetId;
        }
    }

    /// <summary>Adds a new preset</summary>
    public void AddPreset(PromptPreset preset)
    {
        Presets.Add(preset);
    }

    /// <summary>Removes a preset</summary>
    public bool RemovePreset(string presetId)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset != null)
        {
            Presets.Remove(preset);
            // If the removed preset was active, activate the first one
            if (preset.IsActive && Presets.Count > 0)
            {
                Presets[0].IsActive = true;
            }
            return true;
        }
        return false;
    }

    /// <summary>Duplicates a preset</summary>
    public PromptPreset DuplicatePreset(string presetId)
    {
        var source = Presets.FirstOrDefault(p => p.Id == presetId);
        if (source == null) return null;

        var clone = source.Clone();
        Presets.Add(clone);
        return clone;
    }

    /// <summary>
    /// Builds the final message list for AI client use.
    /// Chat history is obtained from context.ChatHistory and inserted at the {{chat.history}} marker.
    /// </summary>
    /// <param name="context">Parse context (containing ChatHistory)</param>
    /// <returns>Message list (role, content)</returns>
    public List<(PromptRole role, string content)> BuildPromptMessages(MustacheContext context)
    {
        var result = new List<(PromptRole role, string content)>();
        var preset = GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("No active preset found");
            return result;
        }

        // 1. Process Relative position entries (ordered by list position)
        foreach (var entry in preset.GetRelativeEntries())
        {
            // Check if this entry is the chat history marker
            if (entry.Content.Trim() == "{{chat.history}}")
            {
                // Insert chat history at this position
                if (context.ChatHistory != null && context.ChatHistory.Count > 0)
                {
                    foreach (var (role, message) in context.ChatHistory)
                    {
                        // Map all roles correctly: System -> System, User -> User, AI -> Assistant
                        var promptRole = role switch
                        {
                            Role.System => PromptRole.System,
                            Role.User => PromptRole.User,
                            Role.AI => PromptRole.Assistant,
                            _ => PromptRole.System
                        };
                        result.Add((promptRole, message));
                    }
                }
                continue;  // Don't add the marker itself as a message
            }
            
            var content = MustacheParser.Parse(entry.Content, context);
            if (!string.IsNullOrWhiteSpace(content))
            {
                result.Add((entry.Role, content));
            }
        }

        // 2. Process InChat position entries (insert at specified depth)
        foreach (var entry in preset.GetInChatEntries())
        {
            var content = MustacheParser.Parse(entry.Content, context);
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Calculate insertion position (counting from end of result)
                var insertIndex = Math.Max(0, result.Count - entry.InChatDepth);
                result.Insert(insertIndex, (entry.Role, content));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds system instruction string (for legacy API compatibility).
    /// </summary>
    public string BuildSystemInstruction(MustacheContext context)
    {
        var preset = GetActivePreset();
        if (preset == null) return "";

        var systemParts = new List<string>();
        
        foreach (var entry in preset.GetRelativeEntries())
        {
            if (entry.Role == PromptRole.System)
            {
                var content = MustacheParser.Parse(entry.Content, context);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    systemParts.Add(content);
                }
            }
        }

        return string.Join("\n\n", systemParts);
    }

    /// <summary>Initializes default presets</summary>
    public void InitializeDefaults()
    {
        if (Presets.Count == 0)
        {
            var defaultPreset = CreateDefaultPreset();
            Presets.Add(defaultPreset);
        }
    }

    /// <summary>Creates default preset - entry order is determined by list position (drag-to-reorder like SillyTavern)</summary>
    private PromptPreset CreateDefaultPreset()
    {
        return new PromptPreset
        {
            Name = "RimTalk Default",
            Description = "RimTalk默认提示词预设",
            IsActive = true,
            Entries = new List<PromptEntry>
            {
                // 1. Base Instruction
                new()
                {
                    Name = "Base Instruction",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Editable = true,
                    Content = @"Role-play RimWorld character per profile

Rules:
Preserve original names (no translation)
Keep dialogue short ({{lang}} only, 1-2 sentences)

Roles:
Prisoner: wary, hesitant; mention confinement; plead or bargain
Slave: fearful, obedient; reference forced labor and exhaustion; call colonists ""master""
Visitor: polite, curious, deferential; treat other visitors in the same group as companions
Enemy: hostile, aggressive; terse commands/threats

Monologue = 1 turn. Conversation = 4-8 short turns"
                },
                // 2. JSON Format
                new()
                {
                    Name = "JSON Format",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Editable = false,
                    Content = @"Output JSONL.
Required keys: ""name"", ""text"".
Optional keys (Include only if social interaction occurs):
""act"": Insult, Slight, Chat, Kind
""target"": targetName"
                },
                // 3. Pawn Profiles
                new()
                {
                    Name = "Pawn Profiles",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Editable = true,
                    Content = "{{pawns.all}}"
                },
                // 4. Chat History - 历史对话在这里插入
                new()
                {
                    Name = "Chat History",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Editable = false,
                    Content = "{{chat.history}}"  // Special marker - history will be inserted here
                },
                // 5. Dialogue Prompt - 当前对话提示
                new()
                {
                    Name = "Dialogue Prompt",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Editable = true,
                    Content = @"{{dialogue.type}}
{{dialogue.status}}
{{pawn1.name}}({{pawn1.age}}{{pawn1.gender}}/{{pawn1.role}}/{{pawn1.race}}) {{pawn1.job}}中。
Nearby: {{pawns.nearby}}
Time: {{time.hour12}}
Season: {{time.season}}
Weather: {{weather}}
Location: {{location}}"
                }
            }
        };
    }

    /// <summary>Migrates legacy custom instruction</summary>
    public void MigrateLegacyInstruction(string legacyInstruction)
    {
        if (string.IsNullOrWhiteSpace(legacyInstruction)) return;

        var preset = GetActivePreset();
        if (preset == null) return;

        // Check if already migrated
        if (preset.Entries.Any(e => e.Name == "Legacy Custom Instruction")) return;

        // Insert at second position (after Base Instruction)
        preset.Entries.Insert(1, new PromptEntry
        {
            Name = "Legacy Custom Instruction",
            Role = PromptRole.System,
            Position = PromptPosition.Relative,
            Content = legacyInstruction,
            Editable = true
        });

        Logger.Message("Migrated legacy custom instruction to new prompt system");
    }

    /// <summary>Adds a custom variable</summary>
    public void AddCustomVariable(CustomVariable variable)
    {
        if (variable == null || string.IsNullOrEmpty(variable.Name)) return;
        
        // Remove existing variable with same name
        CustomVariables.RemoveAll(v => v.Name.ToLowerInvariant() == variable.Name.ToLowerInvariant());
        CustomVariables.Add(variable);
    }

    /// <summary>Removes a custom variable</summary>
    public bool RemoveCustomVariable(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return CustomVariables.RemoveAll(v => v.Name.ToLowerInvariant() == name.ToLowerInvariant()) > 0;
    }

    /// <summary>Gets a custom variable</summary>
    public CustomVariable GetCustomVariable(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return CustomVariables.FirstOrDefault(v => v.Name.ToLowerInvariant() == name.ToLowerInvariant());
    }

    /// <summary>Resets to default settings</summary>
    public void ResetToDefaults()
    {
        Presets.Clear();
        CustomVariables.Clear();
        VariableStore.Clear();
        InitializeDefaults();
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref Presets, "presets", LookMode.Deep);
        Scribe_Deep.Look(ref VariableStore, "variableStore");
        Scribe_Collections.Look(ref CustomVariables, "customVariables", LookMode.Deep);

        // Ensure collections are not null
        Presets ??= new List<PromptPreset>();
        VariableStore ??= new VariableStore();
        CustomVariables ??= new List<CustomVariable>();
        
        // Ensure there's a default preset
        if (Presets.Count == 0)
        {
            InitializeDefaults();
        }
    }

    /// <summary>Sets the singleton instance (for loading settings)</summary>
    public static void SetInstance(PromptManager manager)
    {
        _instance = manager;
        if (_instance != null && _instance.Presets.Count == 0)
        {
            _instance.InitializeDefaults();
        }
    }
}