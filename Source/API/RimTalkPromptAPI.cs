using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimTalk.Prompt;
using RimTalk.Util;
using Verse;

namespace RimTalk.API;

/// <summary>
/// RimTalk prompt system public API for other mods to use.
/// </summary>
public static class RimTalkPromptAPI
{
    // ===== Custom Variable Registration API =====
    
    /// <summary>
    /// Registers a new pawn property variable (e.g., "bloodtype" for {{pawn1.bloodtype}}, {{pawn2.bloodtype}}, etc.).
    /// These are independent variables that mods can use in templates.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="variableName">Variable name (e.g., "bloodtype" for {{pawn1.bloodtype}})</param>
    /// <param name="provider">Function that takes a Pawn and returns the variable value</param>
    /// <param name="description">Description for UI display (optional)</param>
    /// <param name="priority">Priority for ordering (lower = first, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.RegisterPawnVariable(
    ///     "MyMod.PackageId",
    ///     "bloodtype",
    ///     pawn => GetBloodType(pawn),
    ///     "Blood type of the pawn"
    /// );
    /// // Usage: {{pawn1.bloodtype}}, {{pawn2.bloodtype}}, etc.
    /// </example>
    public static void RegisterPawnVariable(
        string modId,
        string variableName,
        Func<Pawn, string> provider,
        string description = null,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName) || provider == null)
        {
            Logger.Warning("RimTalkPromptAPI.RegisterPawnVariable: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.RegisterPawnVariable(variableName, SanitizeModId(modId), provider, description, priority);
        Logger.Debug($"Mod '{modId}' registered pawn variable: {{{{pawnN.{variableName}}}}}");
    }
    
    /// <summary>
    /// Registers a new environment variable (e.g., "radiation" for {{radiation}}).
    /// These are independent variables that mods can use in templates.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="variableName">Variable name (e.g., "radiation")</param>
    /// <param name="provider">Function that takes a Map and returns the variable value</param>
    /// <param name="description">Description for UI display (optional)</param>
    /// <param name="priority">Priority for ordering (lower = first, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.RegisterEnvironmentVariable(
    ///     "MyMod.PackageId",
    ///     "radiation",
    ///     map => GetRadiationLevel(map),
    ///     "Current radiation level"
    /// );
    /// // Usage: {{radiation}}
    /// </example>
    public static void RegisterEnvironmentVariable(
        string modId,
        string variableName,
        Func<Map, string> provider,
        string description = null,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName) || provider == null)
        {
            Logger.Warning("RimTalkPromptAPI.RegisterEnvironmentVariable: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.RegisterEnvironmentVariable(variableName, SanitizeModId(modId), provider, description, priority);
        Logger.Debug($"Mod '{modId}' registered environment variable: {{{{{variableName}}}}}");
    }
    
    /// <summary>
    /// Registers a new context variable (e.g., "memory" for {{memory}}).
    /// Context variables have access to the full PromptContext including all pawns, dialogue info, etc.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="variableName">Variable name (e.g., "memory")</param>
    /// <param name="provider">Function that takes PromptContext and returns the variable value</param>
    /// <param name="description">Description for UI display (optional)</param>
    /// <param name="priority">Priority for ordering (lower = first, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.RegisterContextVariable(
    ///     "MyMod.PackageId",
    ///     "memory",
    ///     ctx => GetMemoryContext(ctx.CurrentPawn),
    ///     "AI memory content for this pawn"
    /// );
    /// // Usage: {{memory}}
    /// </example>
    public static void RegisterContextVariable(
        string modId,
        string variableName,
        Func<PromptContext, string> provider,
        string description = null,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName) || provider == null)
        {
            Logger.Warning("RimTalkPromptAPI.RegisterContextVariable: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.RegisterContextVariable(variableName, SanitizeModId(modId), provider, description, priority);
        Logger.Debug($"Mod '{modId}' registered context variable: {{{{{variableName}}}}}");
    }
    
    /// <summary>
    /// Gets all registered custom variables (for UI display).
    /// </summary>
    /// <returns>Enumerable of (Name, ModId, Description, Type) tuples</returns>
    public static IEnumerable<(string Name, string ModId, string Description, string Type)> GetRegisteredCustomVariables()
    {
        return ContextHookRegistry.GetAllCustomVariables();
    }

    // ===== Prompt Entry API =====
    
    /// <summary>
    /// Adds a prompt entry to the currently active preset (at the end).
    /// </summary>
    /// <param name="entry">The prompt entry to add</param>
    /// <returns>Whether the addition was successful</returns>
    public static bool AddPromptEntry(PromptEntry entry)
    {
        if (entry == null) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("RimTalkPromptAPI.AddPromptEntry: No active preset");
            return false;
        }

        preset.AddEntry(entry);
        Logger.Debug($"Added prompt entry: {entry.Name}");
        return true;
    }

    /// <summary>
    /// Inserts a prompt entry at a specific index in the currently active preset.
    /// </summary>
    /// <param name="entry">The prompt entry to insert</param>
    /// <param name="index">The index to insert at (0 = beginning, -1 or >= Count = end)</param>
    /// <returns>Whether the insertion was successful</returns>
    public static bool InsertPromptEntry(PromptEntry entry, int index)
    {
        if (entry == null) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("RimTalkPromptAPI.InsertPromptEntry: No active preset");
            return false;
        }

        preset.InsertEntry(entry, index);
        Logger.Debug($"Inserted prompt entry: {entry.Name} at index {index}");
        return true;
    }

    /// <summary>
    /// Inserts a prompt entry after a specific entry in the currently active preset.
    /// </summary>
    /// <param name="entry">The prompt entry to insert</param>
    /// <param name="afterEntryId">The ID of the entry to insert after</param>
    /// <returns>Whether the target entry was found (entry is always added)</returns>
    public static bool InsertPromptEntryAfter(PromptEntry entry, string afterEntryId)
    {
        if (entry == null) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("RimTalkPromptAPI.InsertPromptEntryAfter: No active preset");
            return false;
        }

        var result = preset.InsertEntryAfter(entry, afterEntryId);
        Logger.Debug($"Inserted prompt entry: {entry.Name} after {afterEntryId} (found: {result})");
        return result;
    }

    /// <summary>
    /// Inserts a prompt entry before a specific entry in the currently active preset.
    /// </summary>
    /// <param name="entry">The prompt entry to insert</param>
    /// <param name="beforeEntryId">The ID of the entry to insert before</param>
    /// <returns>Whether the target entry was found (entry is always added)</returns>
    public static bool InsertPromptEntryBefore(PromptEntry entry, string beforeEntryId)
    {
        if (entry == null) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("RimTalkPromptAPI.InsertPromptEntryBefore: No active preset");
            return false;
        }

        var result = preset.InsertEntryBefore(entry, beforeEntryId);
        Logger.Debug($"Inserted prompt entry: {entry.Name} before {beforeEntryId} (found: {result})");
        return result;
    }

    /// <summary>
    /// Inserts a prompt entry after an entry with the specified name.
    /// Useful when you don't have the entry ID.
    /// </summary>
    /// <param name="entry">The prompt entry to insert</param>
    /// <param name="afterEntryName">The name of the entry to insert after</param>
    /// <returns>Whether the target entry was found (entry is always added)</returns>
    public static bool InsertPromptEntryAfterName(PromptEntry entry, string afterEntryName)
    {
        if (entry == null || string.IsNullOrEmpty(afterEntryName)) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("RimTalkPromptAPI.InsertPromptEntryAfterName: No active preset");
            return false;
        }

        var targetId = preset.FindEntryIdByName(afterEntryName);
        if (targetId == null)
        {
            preset.AddEntry(entry); // Fall back to adding at end
            Logger.Debug($"Inserted prompt entry: {entry.Name} (target '{afterEntryName}' not found, added at end)");
            return false;
        }

        return InsertPromptEntryAfter(entry, targetId);
    }

    /// <summary>
    /// Inserts a prompt entry before an entry with the specified name.
    /// Useful when you don't have the entry ID.
    /// </summary>
    /// <param name="entry">The prompt entry to insert</param>
    /// <param name="beforeEntryName">The name of the entry to insert before</param>
    /// <returns>Whether the target entry was found (entry is always added)</returns>
    public static bool InsertPromptEntryBeforeName(PromptEntry entry, string beforeEntryName)
    {
        if (entry == null || string.IsNullOrEmpty(beforeEntryName)) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null)
        {
            Logger.Warning("RimTalkPromptAPI.InsertPromptEntryBeforeName: No active preset");
            return false;
        }

        var targetId = preset.FindEntryIdByName(beforeEntryName);
        if (targetId == null)
        {
            preset.AddEntry(entry); // Fall back to adding at end
            Logger.Debug($"Inserted prompt entry: {entry.Name} (target '{beforeEntryName}' not found, added at end)");
            return false;
        }

        return InsertPromptEntryBefore(entry, targetId);
    }

    /// <summary>
    /// Finds an entry ID by its name in the active preset.
    /// </summary>
    /// <param name="entryName">The name of the entry to find</param>
    /// <returns>The entry ID if found, null otherwise</returns>
    public static string FindEntryIdByName(string entryName)
    {
        if (string.IsNullOrEmpty(entryName)) return null;

        var preset = PromptManager.Instance.GetActivePreset();
        return preset?.FindEntryIdByName(entryName);
    }

    /// <summary>
    /// Removes a prompt entry by its ID.
    /// </summary>
    /// <param name="entryId">The entry ID</param>
    /// <returns>Whether the removal was successful</returns>
    public static bool RemovePromptEntry(string entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return false;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null) return false;

        return preset.RemoveEntry(entryId);
    }

    /// <summary>
    /// Removes all prompt entries by mod ID.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <returns>Number of entries removed</returns>
    public static int RemovePromptEntriesByModId(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return 0;

        var preset = PromptManager.Instance.GetActivePreset();
        if (preset == null) return 0;

        var toRemove = preset.Entries.Where(e => e.SourceModId == modId).ToList();
        foreach (var entry in toRemove)
        {
            preset.Entries.Remove(entry);
        }

        return toRemove.Count;
    }

    /// <summary>
    /// Gets the global variable store (read/write access).
    /// </summary>
    /// <returns>The variable store instance</returns>
    public static VariableStore GetVariableStore()
    {
        return PromptManager.Instance.VariableStore;
    }

    /// <summary>
    /// Sets a global variable.
    /// </summary>
    /// <param name="key">Variable name</param>
    /// <param name="value">Variable value</param>
    public static void SetGlobalVariable(string key, string value)
    {
        PromptManager.Instance.VariableStore.SetVar(key, value);
    }

    /// <summary>
    /// Gets a global variable.
    /// </summary>
    /// <param name="key">Variable name</param>
    /// <param name="defaultValue">Default value if not found</param>
    /// <returns>The variable value</returns>
    public static string GetGlobalVariable(string key, string defaultValue = "")
    {
        return PromptManager.Instance.VariableStore.GetVar(key, defaultValue);
    }

    /// <summary>
    /// Gets the currently active preset.
    /// </summary>
    /// <returns>The current preset (read-only access recommended)</returns>
    public static PromptPreset GetActivePreset()
    {
        return PromptManager.Instance.GetActivePreset();
    }

    /// <summary>
    /// Gets all presets.
    /// </summary>
    /// <returns>List of presets</returns>
    public static IReadOnlyList<PromptPreset> GetAllPresets()
    {
        return PromptManager.Instance.Presets;
    }

    // ===== Unified Hook API =====
    // All hook methods now use the unified ContextHookRegistry
    
    /// <summary>
    /// Registers a pawn context hook that applies to all pawn context properties.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="category">The context category to hook (use ContextCategories.Pawn.*)</param>
    /// <param name="operation">The hook operation (Append, Prepend, or Override)</param>
    /// <param name="handler">Function that receives (pawn, originalValue) and returns modified value</param>
    /// <param name="priority">Priority (lower = earlier execution, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.RegisterPawnHook(
    ///     "MyMod.PackageId",
    ///     ContextCategories.Pawn.Health,
    ///     ContextHookRegistry.HookOperation.Append,
    ///     (pawn, original) => original + $"; Toxicity: {pawn.GetToxicityLevel()}"
    /// );
    /// </example>
    public static void RegisterPawnHook(
        string modId,
        ContextCategory category,
        ContextHookRegistry.HookOperation operation,
        Func<Pawn, string, string> handler,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || handler == null)
        {
            Logger.Warning("RimTalkPromptAPI.RegisterPawnHook: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.RegisterPawnHook(category, operation, SanitizeModId(modId), handler, priority);
        Logger.Debug($"Mod '{modId}' registered {operation} hook for {category}");
    }
    
    /// <summary>
    /// Registers an environment context hook that applies to all environment context properties.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="category">The context category to hook (use ContextCategories.Environment.*)</param>
    /// <param name="operation">The hook operation (Append, Prepend, or Override)</param>
    /// <param name="handler">Function that receives (map, originalValue) and returns modified value</param>
    /// <param name="priority">Priority (lower = earlier execution, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.RegisterEnvironmentHook(
    ///     "MyMod.PackageId",
    ///     ContextCategories.Environment.Weather,
    ///     ContextHookRegistry.HookOperation.Append,
    ///     (map, original) => original + "; Wind: Strong NE"
    /// );
    /// </example>
    public static void RegisterEnvironmentHook(
        string modId,
        ContextCategory category,
        ContextHookRegistry.HookOperation operation,
        Func<Map, string, string> handler,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || handler == null)
        {
            Logger.Warning("RimTalkPromptAPI.RegisterEnvironmentHook: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.RegisterEnvironmentHook(category, operation, SanitizeModId(modId), handler, priority);
        Logger.Debug($"Mod '{modId}' registered {operation} hook for {category}");
    }
    
    /// <summary>
    /// Injects a new pawn section that appears alongside existing pawn context sections.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="sectionName">Name for the new section (used as variable key)</param>
    /// <param name="anchor">The category to anchor relative to (use ContextCategories.Pawn.*)</param>
    /// <param name="position">Position relative to anchor (Before or After)</param>
    /// <param name="provider">Function that takes a pawn and returns the section content</param>
    /// <param name="priority">Priority (lower = earlier within same position, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.InjectPawnSection(
    ///     "MyMod.PackageId",
    ///     "bloodtype",
    ///     ContextCategories.Pawn.Health,
    ///     ContextHookRegistry.InjectPosition.After,
    ///     pawn => $"Blood Type: {GetBloodType(pawn)}"
    /// );
    /// </example>
    public static void InjectPawnSection(
        string modId,
        string sectionName,
        ContextCategory anchor,
        ContextHookRegistry.InjectPosition position,
        Func<Pawn, string> provider,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(sectionName) || provider == null)
        {
            Logger.Warning("RimTalkPromptAPI.InjectPawnSection: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.InjectPawnSection(sectionName, SanitizeModId(modId), anchor, position, provider, priority);
        Logger.Debug($"Mod '{modId}' injected pawn section '{sectionName}' {position} {anchor}");
    }
    
    /// <summary>
    /// Injects a new environment section that appears alongside existing environment context sections.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="sectionName">Name for the new section (used as variable key)</param>
    /// <param name="anchor">The category to anchor relative to (use ContextCategories.Environment.*)</param>
    /// <param name="position">Position relative to anchor (Before or After)</param>
    /// <param name="provider">Function that takes a map and returns the section content</param>
    /// <param name="priority">Priority (lower = earlier within same position, default 100)</param>
    /// <example>
    /// RimTalkPromptAPI.InjectEnvironmentSection(
    ///     "MyMod.PackageId",
    ///     "windspeed",
    ///     ContextCategories.Environment.Weather,
    ///     ContextHookRegistry.InjectPosition.After,
    ///     map => $"Wind: {GetWindSpeed(map)}"
    /// );
    /// </example>
    public static void InjectEnvironmentSection(
        string modId,
        string sectionName,
        ContextCategory anchor,
        ContextHookRegistry.InjectPosition position,
        Func<Map, string> provider,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(sectionName) || provider == null)
        {
            Logger.Warning("RimTalkPromptAPI.InjectEnvironmentSection: Invalid parameters");
            return;
        }
        
        ContextHookRegistry.InjectEnvironmentSection(sectionName, SanitizeModId(modId), anchor, position, provider, priority);
        Logger.Debug($"Mod '{modId}' injected environment section '{sectionName}' {position} {anchor}");
    }
    
    /// <summary>
    /// Unregisters all hooks and injected sections registered by a specific mod.
    /// Call this when your mod is unloaded or disabled.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    public static void UnregisterAllHooks(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return;
        ContextHookRegistry.UnregisterMod(SanitizeModId(modId));
        Logger.Debug($"Mod '{modId}' unregistered all hooks");
    }
    
    /// <summary>
    /// Checks if there are any hooks registered.
    /// </summary>
    public static bool HasAnyHooks()
    {
        return ContextHookRegistry.HasAnyHooks || ContextHookRegistry.HasAnyInjections;
    }

    /// <summary>
    /// Creates a new prompt entry.
    /// </summary>
    /// <param name="name">Entry name</param>
    /// <param name="content">Content (supports mustache syntax)</param>
    /// <param name="role">The message role</param>
    /// <param name="position">Position type (Relative or InChat)</param>
    /// <param name="inChatDepth">Insertion depth for InChat position</param>
    /// <param name="sourceModId">Source mod ID</param>
    /// <returns>The newly created entry</returns>
    public static PromptEntry CreatePromptEntry(
        string name,
        string content,
        PromptRole role = PromptRole.System,
        PromptPosition position = PromptPosition.Relative,
        int inChatDepth = 0,
        string sourceModId = null)
    {
        return new PromptEntry
        {
            Name = name,
            Content = content,
            Role = role,
            Position = position,
            InChatDepth = inChatDepth,
            SourceModId = sourceModId,
            Enabled = true
        };
    }

    /// <summary>
    /// Sanitizes a mod ID to lowercase letters and numbers only.
    /// </summary>
    private static string SanitizeModId(string modId)
    {
        // Remove special characters, keep only letters and digits
        var sanitized = Regex.Replace(modId.ToLowerInvariant(), @"[^a-z0-9]", "");
        // Ensure it's not empty
        return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
    }
}