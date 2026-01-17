using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimTalk.Data;
using RimTalk.Service;
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

    /// <summary>Stores the last used context for UI preview purposes.</summary>
    public static PromptContext LastContext { get; private set; }

    /// <summary>All presets</summary>
    public List<PromptPreset> Presets = new();
    
    /// <summary>Global variable store (for setvar/getvar)</summary>
    public VariableStore VariableStore = new();

    /// <summary>Cached preset for Simple Mode to avoid recreation on every call</summary>
    private PromptPreset _simplePresetCache;
    private string _simplePresetCacheKey = "";

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
        
        string baseName = source.Name;
        // Check if name ends with (n) and extract base name if so
        var match = Regex.Match(baseName, @"^(.*?)\s*\((\d+)\)$");
        if (match.Success)
        {
            baseName = match.Groups[1].Value.Trim();
        }

        clone.Name = GetUniqueName(baseName);
        Presets.Add(clone);
        return clone;
    }

    /// <summary>Creates a new preset from the default template</summary>
    public PromptPreset CreateNewPreset(string baseName)
    {
        var preset = CreateDefaultPreset();
        preset.IsActive = false; // New presets shouldn't auto-activate
        preset.Name = GetUniqueName(baseName);
        Presets.Add(preset);
        return preset;
    }

    /// <summary>
    /// Finds a unique name for a preset by appending a suffix if necessary.
    /// </summary>
    /// <param name="baseName">The base name to start with</param>
    /// <param name="excludeId">Optional ID to exclude from uniqueness check (e.g. if checking for an existing preset)</param>
    /// <returns>A unique preset name</returns>
    public string GetUniqueName(string baseName, string excludeId = null)
    {
        if (!Presets.Any(p => p.Name == baseName && p.Id != excludeId))
        {
            return baseName;
        }

        int i = 1;
        string newName;
        do
        {
            newName = $"{baseName} ({i++})";
        } while (Presets.Any(p => p.Name == newName && p.Id != excludeId));

        return newName;
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
        var settings = Settings.Get();
        var baseInstruction = string.IsNullOrWhiteSpace(settings.CustomInstruction)
            ? Constant.DefaultInstruction
            : settings.CustomInstruction;

        return new PromptPreset
        {
            Name = "RimTalk Default",
            Description = "RimTalk default prompt preset",
            IsActive = true,
            Entries = new List<PromptEntry>
            {
                // 1. System Section
                new()
                {
                    Name = "Base Instruction",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = baseInstruction
                },
                new()
                {
                    Name = "JSON Format",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = "{{json.format}}"
                },
                new()
                {
                    Name = "Pawn Profiles",
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = "{{context}}"
                },
                // 2. History Section
                new()
                {
                    Name = "Chat History",
                    Role = PromptRole.User, // Visual placeholder
                    Position = PromptPosition.Relative,
                    IsMainChatHistory = true,
                    Content = "{{chat.history}}"  // Special marker - history will be inserted here
                },
                // 3. Prompt Section
                new()
                {
                    Name = "Dialogue Prompt",
                    Role = PromptRole.User,
                    Position = PromptPosition.Relative,
                    Content = "{{prompt}}"
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
        
        // Clear blacklist so mod entries can be re-added on next startup
        foreach (var preset in Presets)
        {
            preset.ClearBlacklist();
        }
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref Presets, "presets", LookMode.Deep);
        Scribe_Deep.Look(ref VariableStore, "variableStore");

        // Ensure collections are not null
        Presets ??= new List<PromptPreset>();
        VariableStore ??= new VariableStore();

        // Migration: Fix legacy chat history markers
        if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
        {
            foreach (var preset in Presets)
            {
                // If no entry is marked as history, but we have one with the legacy content tag
                if (!preset.Entries.Any(e => e.IsMainChatHistory))
                {
                    var legacyEntry = preset.Entries.FirstOrDefault(e => e.Content.Trim() == "{{chat.history}}");
                    if (legacyEntry != null)
                    {
                        legacyEntry.IsMainChatHistory = true;
                    }
                }
            }
        }
        
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
    /// The primary entry point for building AI messages.
    /// Handles Simple vs Advanced mode switching and provides robust fallbacks.
    /// </summary>
    public List<(Role role, string content)> BuildMessages(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var settings = Settings.Get();
        
        // 1. Prepare shared context data
        string dialogueType = PromptContextProvider.GetDialogueTypeString(talkRequest, pawns);
        talkRequest.Context = PromptService.BuildContext(pawns);
        PromptService.DecoratePrompt(talkRequest, pawns, status);

        // 2. Build Context Object
        var context = PromptContext.FromTalkRequest(talkRequest, pawns);
        context.DialogueType = dialogueType;
        context.DialogueStatus = status;
        context.DialoguePrompt = talkRequest.Prompt;
        LastContext = context;

        // 3. Select Preset (Active for Advanced, Cached for Simple)
        PromptPreset preset;
        if (settings.UseAdvancedPromptMode)
        {
            preset = GetActivePreset();
        }
        else
        {
            var cacheKey = settings.CustomInstruction ?? "";
            if (_simplePresetCache == null || _simplePresetCacheKey != cacheKey)
            {
                _simplePresetCache = CreateDefaultPreset();
                _simplePresetCacheKey = cacheKey;
            }
            preset = _simplePresetCache;
        }
        if (preset == null) preset = CreateDefaultPreset();

        // 4. Build and return
        var segments = new List<PromptMessageSegment>();
        var messages = BuildMessagesFromPreset(preset, context, segments);
        
        talkRequest.PromptMessageSegments = segments.Count > 0 ? segments : null;
        
        return messages.Select(m => ((Role)m.role, m.content)).ToList();
    }

    private List<(PromptRole role, string content)> BuildMessagesFromPreset(
        PromptPreset preset,
        PromptContext context,
        List<PromptMessageSegment> segments)
    {
        var result = new List<(PromptRole role, string content)>();
        int lastHistoryIndex = 0;

        // 1. Process System entries first (Merged)
        var systemParts = preset.Entries
            .Where(e => e.Enabled && e.Role == PromptRole.System && e.Position == PromptPosition.Relative)
            .Select(e => new { e, content = ScribanParser.Render(e.Content, context) })
            .Where(x => !string.IsNullOrWhiteSpace(x.content))
            .ToList();

        foreach (var item in systemParts)
        {
            segments?.Add(new PromptMessageSegment(item.e.Id, item.e.Name, Role.System, item.content));
        }

        if (systemParts.Count > 0)
        {
            result.Add((PromptRole.System, string.Join("\n\n", systemParts.Select(x => x.content))));
        }
        
        int systemBoundary = result.Count; // Ensure nothing is inserted above this index
        lastHistoryIndex = result.Count;

        // 2. Process Relative entries (History/Prompt)
        foreach (var entry in preset.Entries.Where(e => e.Enabled && e.Role != PromptRole.System && e.Position == PromptPosition.Relative))
        {
            if (entry.IsMainChatHistory)
            {
                if (context.ChatHistory != null)
                {
                    foreach (var (role, message) in context.ChatHistory)
                    {
                        var pRole = (PromptRole)role;
                        result.Add((pRole, message));
                        segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "History", role, message));
                    }
                }
                lastHistoryIndex = result.Count; // Anchor point set to end of history
                continue;
            }

            var content = ScribanParser.Render(entry.Content, context);
            if (!string.IsNullOrWhiteSpace(content))
            {
                result.Add((entry.Role, content));
                segments?.Add(new PromptMessageSegment(entry.Id, entry.Name ?? "Entry", (Role)entry.Role, content));
            }
        }

        // 3. Process InChat entries (Anchored to History)
        foreach (var entry in preset.GetInChatEntries())
        {
            var content = ScribanParser.Render(entry.Content, context);
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Calculate position relative to history end, clamped by system boundary
                var insertIndex = Math.Max(systemBoundary, lastHistoryIndex - entry.InChatDepth);
                
                result.Insert(insertIndex, (entry.Role, content));
                segments?.Insert(insertIndex, new PromptMessageSegment(entry.Id, entry.Name ?? "Entry", (Role)entry.Role, content));
                
                // Shift anchor and boundary forward since we increased the list size
                if (insertIndex <= lastHistoryIndex) lastHistoryIndex++;
                systemBoundary++; 
            }
        }

        return MergeConsecutiveRoles(result);
    }
}