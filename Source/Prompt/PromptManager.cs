using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimTalk.API;
using RimTalk.Compatibility;
using RimTalk.Data;
using RimTalk.Service;
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

    public bool IsStartupFinished => true;
    public const string DefaultPresetName = "RimTalk Default";

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
        clone.SourceModId = null;
        foreach (var entry in clone.Entries)
        {
            entry.SourceModId = null;
            if (!entry.IsBuiltIn)
                entry.Id = Guid.NewGuid().ToString();
        }
        
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
    /// Extracts the last user message content from the built messages.
    /// Used for saving accurate history when using advanced templates.
    /// </summary>
    /// <param name="messages">The list of built messages to search</param>
    /// <returns>The content of the last user message, or empty string if not found</returns>
    public static string ExtractUserPrompt(List<(Role role, string content)> messages)
    {
        if (messages == null || messages.Count == 0)
            return string.Empty;
    
        // Find the last user message
        var lastUserMessage = messages
            .LastOrDefault(m => m.role == Role.User);
    
        return lastUserMessage.content ?? string.Empty;
    }

    /// <summary>
    /// Merges consecutive messages with the same role into a single message.
    /// Preserves strict role alternation required by APIs like Gemini.
    /// This prevents chat history messages from being merged with the current prompt.
    /// </summary>
    /// <param name="messages">Original message list</param>
    /// <param name="mergeBoundary">Index at which to force a merge break (e.g. after chat history).
    /// Messages before and after this index will never be merged together.</param>
    /// <returns>Merged message list</returns>
    internal static List<(PromptRole role, string content)> MergeConsecutiveRoles(
        List<(PromptRole role, string content)> messages,
        int mergeBoundary = -1)
    {
        return PromptMessageRoleMerger.MergeConsecutiveRoles(messages, mergeBoundary);
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
        CleanOrphanedModEntries();
    }

    // Creates default preset - entry order is determined by list position (drag-to-reorder like SillyTavern)
    private PromptPreset CreateDefaultPreset()
    {
        return new PromptPreset
        {
            Name = DefaultPresetName,
            Description = "RimTalk default prompt preset",
            IsActive = true,
            Entries = new List<PromptEntry>
            {
                // 1. System Section
                new()
                {
                    Id = BuiltInPromptIds.BaseInstruction,
                    Name = BuiltInPromptNames.BaseInstruction,
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = Constant.DefaultInstruction
                },
                new()
                {
                    Id = BuiltInPromptIds.JsonFormat,
                    Name = BuiltInPromptNames.JsonFormat,
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = Constant.DefaultJsonFormatInstruction
                },
                new()
                {
                    Id = BuiltInPromptIds.Context,
                    Name = BuiltInPromptNames.Context,
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = BuiltInPromptTokens.Context
                },
                // 2. Events & History Section
                new()
                {
                    Id = BuiltInPromptIds.RecentEvents,
                    Name = BuiltInPromptNames.RecentEvents,
                    Role = PromptRole.System,
                    Position = PromptPosition.Relative,
                    Content = Constant.DefaultRecentEventsInstruction
                },
                new()
                {
                    Id = BuiltInPromptIds.ChatHistory,
                    Name = BuiltInPromptNames.ChatHistory,
                    Role = PromptRole.User,
                    Position = PromptPosition.Relative,
                    IsMainChatHistory = true,
                    Content = BuiltInPromptTokens.ChatHistory
                },
                // 3. Prompt Section (Immediate intent & situation)
                new()
                {
                    Id = BuiltInPromptIds.DialoguePrompt,
                    Name = BuiltInPromptNames.DialoguePrompt,
                    Role = PromptRole.User,
                    Position = PromptPosition.Relative,
                    Content = BuiltInPromptTokens.Prompt
                }
            }
        };
    }

    /// <summary>Resets to default settings, keeping active mod defaults</summary>
    public void ResetToDefaults()
    {
        ResetToModDefaults();
    }

    /// <summary>
    /// Identifies whether a preset was created/duplicated by the user (as opposed to the system default or a mod-provided preset).
    /// </summary>
    public bool IsUserCreatedPreset(PromptPreset preset)
    {
        if (preset == null) return false;

        // Mod-provided presets belong to mods
        if (!string.IsNullOrEmpty(preset.SourceModId))
            return false;

        // "RimTalk Default" is always the system default
        if (string.Equals(preset.Name, DefaultPresetName, StringComparison.OrdinalIgnoreCase))
            return false;

        // If this is the only preset and has no mod ID, treat it as the default preset
        if (Presets.Count > 0 && Presets[0] == preset && !Presets.Any(p => string.Equals(p.Name, DefaultPresetName, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    /// <summary>Resets to default settings, restoring active mod defaults while preserving user custom presets</summary>
    public void ResetToModDefaults()
    {
        var userPresets = Presets.Where(IsUserCreatedPreset).Select(p => p.Clone()).ToList();
        var defaultPreset = CreateDefaultPreset();

        // Replay active mod entries onto the fresh default preset
        RimTalkPromptAPI.ApplyRegisteredModDefaults(defaultPreset);

        var modPresets = Presets.Where(p => !string.IsNullOrEmpty(p.SourceModId)).Select(p => p.Clone()).ToList();

        var newPresets = new List<PromptPreset> { defaultPreset };
        foreach (var mp in modPresets)
        {
            if (!newPresets.Any(p => p.Id == mp.Id))
                newPresets.Add(mp);
        }
        foreach (var up in userPresets)
        {
            if (!newPresets.Any(p => p.Id == up.Id))
                newPresets.Add(up);
        }

        Presets = newPresets;
        VariableStore.Clear();

        foreach (var preset in Presets)
            preset.ClearBlacklist();

        if (!Presets.Any(p => p.IsActive) && Presets.Count > 0)
            Presets[0].IsActive = true;
    }

    /// <summary>Resets to pure vanilla RimTalk defaults while preserving user custom presets</summary>
    public void ResetToVanillaDefaults()
    {
        var userPresets = Presets.Where(IsUserCreatedPreset).Select(p => p.Clone()).ToList();
        var defaultPreset = CreateDefaultPreset();

        var newPresets = new List<PromptPreset> { defaultPreset };
        foreach (var up in userPresets)
        {
            if (!newPresets.Any(p => p.Id == up.Id))
                newPresets.Add(up);
        }

        Presets = newPresets;
        VariableStore.Clear();

        foreach (var preset in Presets)
            preset.ClearBlacklist();

        if (!Presets.Any(p => p.IsActive) && Presets.Count > 0)
            Presets[0].IsActive = true;
    }

    /// <summary>
    /// Checks whether active addons provide any default presets, entries, or variables.
    /// </summary>
    public bool HasAddonInjectedDefaults()
    {
        if (RimTalkPromptAPI.HasRegisteredModDefaults())
            return true;

        if (Presets != null)
        {
            if (Presets.Any(p => !string.IsNullOrEmpty(p.SourceModId)))
                return true;

            foreach (var preset in Presets)
            {
                if (preset?.Entries == null) continue;
                if (preset.Entries.Any(e => !string.IsNullOrEmpty(e.SourceModId) || !e.IsBuiltIn))
                    return true;
                if (preset.Entries.Any(e => e.IsBuiltIn && !e.Enabled))
                    return true;
            }
        }

        return RimTalkPromptAPI.GetRegisteredCustomVariables().Any();
    }

    /// <summary>
    /// Compatibility hook maintained for external addons.
    /// </summary>
    public void OnStartupFinished()
    {
        CleanOrphanedModEntries();
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref Presets, "presets", LookMode.Deep);
        Scribe_Deep.Look(ref VariableStore, "variableStore");

        Presets ??= new List<PromptPreset>();
        VariableStore ??= new VariableStore();

        if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
        {
            CleanOrphanedModEntries();
            PresetMigrator.Migrate(Presets);
            CaptureExistingModRegistrations();
        }
    }

    private void CaptureExistingModRegistrations()
    {
        if (Presets == null) return;
        foreach (var preset in Presets)
        {
            if (preset?.Entries == null) continue;
            foreach (var entry in preset.Entries)
            {
                if (!string.IsNullOrEmpty(entry.SourceModId))
                {
                    RimTalkPromptAPI.RegisterModDefaultEntry(entry);
                }
            }
        }
    }

    /// <summary>
    /// Removes entries and presets belonging to mods that are no longer active or installed.
    /// </summary>
    public void CleanOrphanedModEntries()
    {
        if (Presets == null || Presets.Count == 0) return;

        Presets.RemoveAll(p => !string.IsNullOrEmpty(p.SourceModId) && !IsModActive(p.SourceModId));

        foreach (var preset in Presets)
        {
            if (preset?.Entries == null) continue;
            preset.Entries.RemoveAll(e => !string.IsNullOrEmpty(e.SourceModId) && !IsModActive(e.SourceModId));
        }
    }

    private static bool IsModActive(string packageId)
    {
        if (string.IsNullOrEmpty(packageId)) return false;
        try
        {
            return ModsConfig.IsActive(packageId);
        }
        catch
        {
            return true; // Fail safe: preserve entries in test or uninitialized environments
        }
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
        if (talkRequest != null && talkRequest.Participants == null && pawns != null)
        {
            talkRequest.Participants = pawns;
        }
        
        if (talkRequest == null) return [];
        
        // 1. Prepare shared context data
        var (dialogueType, intent, topic) = PromptContextProvider.GetDialogueTypeData(talkRequest, pawns);
        talkRequest.Context = PromptService.BuildContext(pawns, talkRequest.IsAnnouncement);
        talkRequest.CausalPrompt = TalkHistory.BuildCausalSummary(talkRequest, intent, topic);
        PromptService.DecoratePrompt(talkRequest, pawns, status);

        // 2. Build Context Object
        var context = PromptContext.FromTalkRequest(talkRequest, pawns);
        context.DialogueType = dialogueType;
        context.Intent = intent;
        context.ConversationTopic = topic;
        context.DialogueStatus = status;
        context.DialoguePrompt = talkRequest.Prompt;
        LastContext = context;

        // 3. Select Preset
        PromptPreset preset = GetActivePreset();
        if (preset == null) preset = CreateDefaultPreset();

        PromptPreset effectivePreset;
        if (!settings.UseAdvancedPromptMode)
        {
            // Simple Mode: Exclude user-added custom entries, keep built-in & active addon entries
            effectivePreset = PromptPresetAssembler.BuildSimpleModePreset(
                preset,
                settings.SimpleModeInstruction,
                Constant.DefaultInstruction,
                Constant.DefaultJsonFormatInstruction);
        }
        else
        {
            effectivePreset = preset;
        }

        // 4. Reset session variables and build
        ScribanParser.ResetSessionVariables();
        var segments = new List<PromptMessageSegment>();
        var messages = BuildMessagesFromPreset(effectivePreset, context, segments);

        talkRequest.PromptMessageSegments = segments.Count > 0 ? segments : null;

        return messages.Select(m => ((Role)m.role, m.content)).ToList();
    }

    private List<(PromptRole role, string content)> BuildMessagesFromPreset(
        PromptPreset preset,
        PromptContext context,
        List<PromptMessageSegment> segments)
    {
        bool useCompact = Settings.Get()?.Context?.UseCompactHistory ?? true;
        if (!useCompact)
        {
            var legacyMarker = preset.Entries.FirstOrDefault(e => e.Enabled && e.Position == PromptPosition.Relative && e.IsMainChatHistory);
            List<(Role role, string message)> legacyHistory = null;
            if (legacyMarker != null)
            {
                var marker = legacyMarker.Content?.Trim().ToLowerInvariant() ?? "";
                legacyHistory = marker.Contains("history_simplified")
                    ? context.GetChatHistory(simplified: true)
                    : context.GetChatHistory(simplified: false);
            }

            return LegacyMultiTurnPromptBuilder.AssembleMessages(
                preset,
                content => ScribanParser.Render(content, context),
                legacyHistory,
                segments);
        }

        var markerEntry = preset.Entries.FirstOrDefault(e => e.Enabled && e.Position == PromptPosition.Relative && e.IsMainChatHistory);
        List<(Role role, string message)> history = null;
        if (markerEntry != null)
        {
            history = context.GetChatHistory(simplified: true);
        }

        return PromptPresetAssembler.AssembleMessages(
            preset,
            content => ScribanParser.Render(content, context),
            history,
            segments);
    }
}
