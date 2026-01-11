using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
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
                // Don't call InitializeDefaults here - will be done lazily in GetActivePreset
            }
            return _instance;
        }
    }

    /// <summary>All presets</summary>
    public List<PromptPreset> Presets = new();
    
    /// <summary>Global variable store (for setvar/getvar)</summary>
    public VariableStore VariableStore = new();

    /// <summary>Gets the currently active preset</summary>
    public PromptPreset GetActivePreset()
    {
        // Lazy initialization - only create defaults when game systems are ready
        if (Presets.Count == 0)
        {
            EnsureInitialized();
        }
        
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
    /// Consecutive messages with the same role are automatically merged for API compatibility.
    /// </summary>
    /// <param name="context">Parse context (containing ChatHistory)</param>
    /// <returns>Message list (role, content)</returns>
    public List<(PromptRole role, string content)> BuildPromptMessages(MustacheContext context)
    {
        return BuildPromptMessagesInternal(context, null);
    }

    private List<(PromptRole role, string content)> BuildPromptMessagesInternal(
        MustacheContext context,
        List<PromptMessageSegment> segments)
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
                    string entryName = string.IsNullOrWhiteSpace(entry.Name) ? "Chat History" : entry.Name;
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
                        segments?.Add(new PromptMessageSegment(entry.Id, entryName, ConvertToRole(promptRole), message));
                    }
                }
                continue; // Don't add the marker itself as a message
            }

            var content = MustacheParser.Parse(entry.Content, context);
            if (!string.IsNullOrWhiteSpace(content))
            {
                result.Add((entry.Role, content));
                string entryName = string.IsNullOrWhiteSpace(entry.Name) ? "Entry" : entry.Name;
                segments?.Add(new PromptMessageSegment(entry.Id, entryName, ConvertToRole(entry.Role), content));
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
                string entryName = string.IsNullOrWhiteSpace(entry.Name) ? "Entry" : entry.Name;
                segments?.Insert(insertIndex, new PromptMessageSegment(entry.Id, entryName, ConvertToRole(entry.Role),
                    content));
            }
        }

        // 3. Merge consecutive messages with the same role for API compatibility
        // This ensures compatibility with APIs like Gemini that require alternating roles
        return MergeConsecutiveRoles(result);
    }

    /// <summary>
    /// Merges consecutive messages with the same role into a single message.
    /// This improves compatibility with APIs that require strict role alternation (e.g., Gemini).
    /// </summary>
    /// <param name="messages">Original message list</param>
    /// <returns>Merged message list</returns>
    private static List<(PromptRole role, string content)> MergeConsecutiveRoles(
        List<(PromptRole role, string content)> messages)
    {
        if (messages == null || messages.Count <= 1)
            return messages;

        var merged = new List<(PromptRole role, string content)>();
        
        foreach (var (role, content) in messages)
        {
            if (merged.Count > 0 && merged[^1].role == role)
            {
                // Same role as previous - merge content
                var last = merged[^1];
                merged[^1] = (role, last.content + "\n\n" + content);
            }
            else
            {
                // Different role - add as new message
                merged.Add((role, content));
            }
        }

        return merged;
    }

    /// <summary>
    /// Converts PromptRole to Role (for AIService compatibility).
    /// Both enums have matching values, so direct cast works.
    /// </summary>
    public static Role ConvertToRole(PromptRole promptRole)
    {
        // PromptRole.System=0, User=1, Assistant=2 maps to Role.System=0, User=1, AI=2
        return (Role)promptRole;
    }

    /// <summary>
    /// Builds prompt messages and converts to Role format for direct use by AIService.
    /// </summary>
    /// <param name="context">Parse context</param>
    /// <returns>Message list in (Role, content) format</returns>
    public List<(Role role, string content)> BuildPromptMessagesAsRoles(MustacheContext context)
    {
        var promptMessages = BuildPromptMessagesInternal(context, null);
        return promptMessages
            .Select(m => (ConvertToRole(m.role), m.content))
            .ToList();
    }

    public List<(Role role, string content)> BuildPromptMessagesAsRoles(
        MustacheContext context,
        List<PromptMessageSegment> segments)
    {
        var promptMessages = BuildPromptMessagesInternal(context, segments);
        return promptMessages
            .Select(m => (ConvertToRole(m.role), m.content))
            .ToList();
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

    /// <summary>
    /// Initializes default presets.
    /// Should only be called after game systems are ready (language, defs, etc.)
    /// </summary>
    public void InitializeDefaults()
    {
        if (Presets.Count == 0)
        {
            var defaultPreset = CreateDefaultPreset();
            Presets.Add(defaultPreset);
        }
    }

    /// <summary>
    /// Ensures defaults are initialized. Safe to call during settings load.
    /// Actual initialization is deferred if game systems aren't ready.
    /// </summary>
    public void EnsureInitialized()
    {
        // Only initialize if language system is ready
        if (Presets.Count == 0 && LanguageDatabase.activeLanguage != null)
        {
            InitializeDefaults();
        }
    }

    // Creates default preset - entry order is determined by list position (drag-to-reorder like SillyTavern)
    private PromptPreset CreateDefaultPreset()
    {
        return new PromptPreset
        {
            Name = "RimTalk Default",
            Description = "RimTalk default prompt preset",
            IsActive = true,
            Entries = new List<PromptEntry>
            {
                // 1. Base Instruction - reuses Constant.DefaultInstruction
                new()
                {
                    Name = "Base Instruction",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = Constant.DefaultInstruction
                },
                // 2. JSON Format (dynamic based on ApplyMoodAndSocialEffects setting)
                new()
                {
                    Name = "JSON Format",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = "{{json.format}}"
                },
                // 3. Pawn Profiles
                new()
                {
                    Name = "Pawn Profiles",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = "{{context}}"
                },
                // 4. Dialogue Prompt
                new()
                {
                    Name = "Dialogue Prompt",
                    Role = PromptRole.User,
                    Position = PromptPosition.Relative,
                    Content = "{{dialogue}}"
                },
                // 5. Chat History
                new()
                {
                    Name = "Chat History",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = "{{chat.history}}"  // Special marker - history will be inserted here
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
            Content = legacyInstruction
        });

        Logger.Debug("Migrated legacy custom instruction to new prompt system");
    }

    /// <summary>Resets to default settings</summary>
    public void ResetToDefaults()
    {
        Presets.Clear();
        VariableStore.Clear();
        InitializeDefaults();
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref Presets, "presets", LookMode.Deep);
        Scribe_Deep.Look(ref VariableStore, "variableStore");

        // Ensure collections are not null
        Presets ??= new List<PromptPreset>();
        VariableStore ??= new VariableStore();
        
        // Don't initialize defaults here - game systems may not be ready
        // Defaults will be initialized lazily when needed
    }

    /// <summary>Sets the singleton instance (for loading settings)</summary>
    public static void SetInstance(PromptManager manager)
    {
        _instance = manager;
        // Don't initialize defaults here - game systems may not be ready
        // Defaults will be initialized lazily when GetActivePreset() is called
    }

    /// <summary>
    /// Prepares prompt messages for a talk request.
    /// This method collects all Mustache context variables and builds the prompt messages.
    /// Must be called AFTER DecoratePrompt has been called on the talkRequest.
    /// </summary>
    /// <param name="talkRequest">The talk request (after DecoratePrompt)</param>
    /// <param name="pawns">List of participating pawns</param>
    /// <param name="status">Pawn status string</param>
    /// <param name="dialogueTypeString">Pre-computed dialogue type string (from ContextBuilder.GetDialogueTypeString, must be called before DecoratePrompt)</param>
    /// <returns>List of prompt messages with roles, or null if no active preset</returns>
    public List<(Role role, string content)> PreparePromptForRequest(
        TalkRequest talkRequest,
        List<Pawn> pawns,
        string status,
        string dialogueTypeString)
    {
        if (GetActivePreset() == null) return null;
        
        // Build MustacheContext with all necessary data
        var mustacheContext = MustacheContext.FromTalkRequest(talkRequest, pawns);
        mustacheContext.DialogueType = dialogueTypeString;
        mustacheContext.DialogueStatus = status;
        mustacheContext.DialoguePrompt = talkRequest.Prompt;  // This is now the decorated prompt
        
        var segments = new List<PromptMessageSegment>();
        var messages = BuildPromptMessagesAsRoles(mustacheContext, segments);
        talkRequest.PromptMessageSegments = segments.Count > 0 ? segments : null;
        return messages.Count > 0 ? messages : null;
    }
}
