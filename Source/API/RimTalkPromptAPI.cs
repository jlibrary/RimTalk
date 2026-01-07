using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimTalk.Prompt;
using RimTalk.Util;

namespace RimTalk.API;

/// <summary>
/// RimTalk prompt system public API for other mods to use.
/// </summary>
public static class RimTalkPromptAPI
{
    /// <summary>
    /// Registers a custom mustache variable.
    /// </summary>
    /// <param name="modId">The mod's package ID (e.g., "MyMod.PackageId")</param>
    /// <param name="variableName">Variable name without prefix (e.g., "health" becomes {{modid.health}})</param>
    /// <param name="provider">Function that provides the variable value</param>
    /// <example>
    /// RimTalkPromptAPI.RegisterVariable(
    ///     "MyMod.PackageId",
    ///     "customhealth",
    ///     ctx => ctx.CurrentPawn?.health?.summaryHealth?.SummaryHealthPercent.ToString("P0") ?? ""
    /// );
    /// // Usage: {{mymod.customhealth}}
    /// </example>
    public static void RegisterVariable(string modId, string variableName, Func<MustacheContext, string> provider)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName) || provider == null)
        {
            Logger.Warning("RimTalkPromptAPI.RegisterVariable: Invalid parameters");
            return;
        }

        var fullName = $"{SanitizeModId(modId)}.{variableName.ToLowerInvariant()}";
        MustacheParser.RegisterProvider(fullName, provider);
        Logger.Message($"Mod '{modId}' registered variable: {{{{{fullName}}}}}");
    }

    /// <summary>
    /// Unregisters a custom mustache variable.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="variableName">The variable name</param>
    public static void UnregisterVariable(string modId, string variableName)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName)) return;

        var fullName = $"{SanitizeModId(modId)}.{variableName.ToLowerInvariant()}";
        MustacheParser.UnregisterProvider(fullName);
        Logger.Message($"Mod '{modId}' unregistered variable: {{{{{fullName}}}}}");
    }

    /// <summary>
    /// Checks if a variable is registered.
    /// </summary>
    public static bool IsVariableRegistered(string modId, string variableName)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName)) return false;
        var fullName = $"{SanitizeModId(modId)}.{variableName.ToLowerInvariant()}";
        return MustacheParser.HasProvider(fullName);
    }

    /// <summary>
    /// Adds a prompt entry to the currently active preset.
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
        Logger.Message($"Added prompt entry: {entry.Name}");
        return true;
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

    /// <summary>
    /// Gets all registered mod variables.
    /// </summary>
    /// <returns>List of variable names</returns>
    public static IEnumerable<string> GetRegisteredModVariables()
    {
        return MustacheParser.GetRegisteredProviders();
    }

    /// <summary>
    /// Registers an appender to modify an existing variable's value.
    /// Unlike RegisterVariable which replaces the value, appenders receive the original value
    /// and can modify it (e.g., append additional information).
    /// </summary>
    /// <param name="modId">The mod's package ID (e.g., "MyMod.PackageId")</param>
    /// <param name="variableName">Variable name to append to (e.g., "weather", "pawn1.health")</param>
    /// <param name="appender">Function that receives (context, originalValue) and returns modified value</param>
    /// <example>
    /// RimTalkPromptAPI.AppendToVariable(
    ///     "MyMod.PackageId",
    ///     "weather",
    ///     (ctx, original) => original + "; Wind: Northeast"
    /// );
    /// // Result: {{weather}} originally outputs "Sunny" → now outputs "Sunny; Wind: Northeast"
    /// </example>
    public static void AppendToVariable(string modId, string variableName, Func<MustacheContext, string, string> appender)
    {
        if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(variableName) || appender == null)
        {
            Logger.Warning("RimTalkPromptAPI.AppendToVariable: Invalid parameters");
            return;
        }

        MustacheParser.RegisterAppender(variableName.ToLowerInvariant(), appender);
        Logger.Message($"Mod '{modId}' registered appender for variable: {{{{{variableName}}}}}");
    }

    /// <summary>
    /// Removes an appender from a variable.
    /// </summary>
    /// <param name="modId">The mod's package ID</param>
    /// <param name="variableName">Variable name</param>
    /// <param name="appender">The same appender function that was registered</param>
    public static void RemoveAppender(string modId, string variableName, Func<MustacheContext, string, string> appender)
    {
        if (string.IsNullOrEmpty(variableName) || appender == null) return;

        MustacheParser.UnregisterAppender(variableName.ToLowerInvariant(), appender);
        Logger.Message($"Mod '{modId}' removed appender for variable: {{{{{variableName}}}}}");
    }

    /// <summary>
    /// Removes all appenders for a variable registered by any mod.
    /// Use with caution - this removes all appenders, not just yours.
    /// </summary>
    /// <param name="variableName">Variable name</param>
    public static void RemoveAllAppenders(string variableName)
    {
        if (string.IsNullOrEmpty(variableName)) return;
        MustacheParser.UnregisterAllAppenders(variableName.ToLowerInvariant());
    }

    /// <summary>
    /// Checks if a variable has any appenders registered.
    /// </summary>
    public static bool HasAppenders(string variableName)
    {
        if (string.IsNullOrEmpty(variableName)) return false;
        return MustacheParser.HasAppenders(variableName.ToLowerInvariant());
    }

    /// <summary>
    /// Gets all variable names that have appenders registered.
    /// </summary>
    public static IEnumerable<string> GetVariablesWithAppenders()
    {
        return MustacheParser.GetVariablesWithAppenders();
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
            Enabled = true,
            Editable = true
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