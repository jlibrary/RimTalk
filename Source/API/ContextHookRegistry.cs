using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.API;

/// <summary>
/// Unified hook registry for context modifications.
/// Consolidates PawnPropertyAppenderRegistry, EnvironmentAppenderRegistry, and MustacheParser.Appenders.
/// </summary>
public static class ContextHookRegistry
{
    public enum HookOperation
    {
        Append,
        Prepend,
        Override
    }
    
    public enum InjectPosition
    {
        Before,
        After
    }
    
    private static readonly Dictionary<ContextCategory, List<HookEntry>> PrependHooks = new(ContextCategory.Comparer);
    private static readonly Dictionary<ContextCategory, List<HookEntry>> AppendHooks = new(ContextCategory.Comparer);
    private static readonly Dictionary<ContextCategory, List<HookEntry>> OverrideHooks = new(ContextCategory.Comparer);
    private static readonly List<InjectedSection> InjectedSections = new();
    
    // Custom variable providers - for registering completely new variables
    // Key: variable name (lowercase), Value: provider entry
    private static readonly Dictionary<string, CustomVariableEntry> CustomPawnVariables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CustomVariableEntry> CustomEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CustomVariableEntry> CustomContextVariables = new(StringComparer.OrdinalIgnoreCase);
    
    private class HookEntry
    {
        public string ModId { get; }
        public Delegate Handler { get; }
        public int Priority { get; }
        
        public HookEntry(string modId, Delegate handler, int priority)
        {
            ModId = modId;
            Handler = handler;
            Priority = priority;
        }
    }
    
    private class InjectedSection
    {
        public string Name { get; }
        public string ModId { get; }
        public ContextCategory Anchor { get; }
        public InjectPosition Position { get; }
        public int Priority { get; }
        public Delegate Provider { get; }
        
        public InjectedSection(string name, string modId, ContextCategory anchor, InjectPosition position, int priority, Delegate provider)
        {
            Name = name;
            ModId = modId;
            Anchor = anchor;
            Position = position;
            Priority = priority;
            Provider = provider;
        }
    }
    
    private class CustomVariableEntry
    {
        public string Name { get; }
        public string ModId { get; }
        public string Description { get; }
        public Delegate Provider { get; }
        public int Priority { get; }
        
        public CustomVariableEntry(string name, string modId, string description, Delegate provider, int priority)
        {
            Name = name;
            ModId = modId;
            Description = description;
            Provider = provider;
            Priority = priority;
        }
    }
    
    // ===== Custom Variable Registration API =====
    
    /// <summary>
    /// Registers a new pawn property variable (e.g., "bloodtype" for {{pawn1.bloodtype}}).
    /// </summary>
    /// <param name="variableName">Variable name without pawn prefix (e.g., "bloodtype")</param>
    /// <param name="modId">Mod package ID for tracking</param>
    /// <param name="provider">Function that takes a Pawn and returns the variable value</param>
    /// <param name="description">Description for UI display (optional)</param>
    /// <param name="priority">Priority for ordering (lower = first, default 100)</param>
    public static void RegisterPawnVariable(
        string variableName,
        string modId,
        Func<Pawn, string> provider,
        string description = null,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(variableName) || provider == null) return;
        
        var key = variableName.ToLowerInvariant();
        CustomPawnVariables[key] = new CustomVariableEntry(variableName, modId, description, provider, priority);
        Logger.Debug($"Registered pawn variable '{variableName}' by {modId}");
    }
    
    /// <summary>
    /// Registers a new environment variable (e.g., "radiation" for {{radiation}}).
    /// </summary>
    /// <param name="variableName">Variable name (e.g., "radiation")</param>
    /// <param name="modId">Mod package ID for tracking</param>
    /// <param name="provider">Function that takes a Map and returns the variable value</param>
    /// <param name="description">Description for UI display (optional)</param>
    /// <param name="priority">Priority for ordering (lower = first, default 100)</param>
    public static void RegisterEnvironmentVariable(
        string variableName,
        string modId,
        Func<Map, string> provider,
        string description = null,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(variableName) || provider == null) return;
        
        var key = variableName.ToLowerInvariant();
        CustomEnvironmentVariables[key] = new CustomVariableEntry(variableName, modId, description, provider, priority);
        Logger.Debug($"Registered environment variable '{variableName}' by {modId}");
    }
    
    /// <summary>
    /// Registers a new context variable (e.g., "memory" for {{memory}}).
    /// Context variables have access to the full MustacheContext including all pawns, dialogue info, etc.
    /// </summary>
    /// <param name="variableName">Variable name (e.g., "memory")</param>
    /// <param name="modId">Mod package ID for tracking</param>
    /// <param name="provider">Function that takes MustacheContext and returns the variable value</param>
    /// <param name="description">Description for UI display (optional)</param>
    /// <param name="priority">Priority for ordering (lower = first, default 100)</param>
    public static void RegisterContextVariable(
        string variableName,
        string modId,
        Delegate provider,
        string description = null,
        int priority = 100)
    {
        if (string.IsNullOrEmpty(variableName) || provider == null) return;
        
        var key = variableName.ToLowerInvariant();
        CustomContextVariables[key] = new CustomVariableEntry(variableName, modId, description, provider, priority);
        Logger.Debug($"Registered context variable '{variableName}' by {modId}");
    }
    
    /// <summary>
    /// Tries to get a custom pawn variable value.
    /// </summary>
    /// <returns>True if variable exists and was evaluated, false otherwise</returns>
    public static bool TryGetPawnVariable(string variableName, Pawn pawn, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(variableName) || pawn == null) return false;
        
        var key = variableName.ToLowerInvariant();
        if (!CustomPawnVariables.TryGetValue(key, out var entry)) return false;
        
        try
        {
            if (entry.Provider is Func<Pawn, string> provider)
            {
                value = provider(pawn) ?? "";
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Custom pawn variable '{variableName}' from {entry.ModId} failed: {ex.Message}");
        }
        return false;
    }
    
    /// <summary>
    /// Tries to get a custom environment variable value.
    /// </summary>
    /// <returns>True if variable exists and was evaluated, false otherwise</returns>
    public static bool TryGetEnvironmentVariable(string variableName, Map map, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(variableName) || map == null) return false;
        
        var key = variableName.ToLowerInvariant();
        if (!CustomEnvironmentVariables.TryGetValue(key, out var entry)) return false;
        
        try
        {
            if (entry.Provider is Func<Map, string> provider)
            {
                value = provider(map) ?? "";
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Custom environment variable '{variableName}' from {entry.ModId} failed: {ex.Message}");
        }
        return false;
    }
    
    /// <summary>
    /// Tries to get a custom context variable value.
    /// </summary>
    /// <returns>True if variable exists and was evaluated, false otherwise</returns>
    public static bool TryGetContextVariable(string variableName, object context, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(variableName)) return false;
        
        var key = variableName.ToLowerInvariant();
        if (!CustomContextVariables.TryGetValue(key, out var entry)) return false;
        
        try
        {
            // Try to invoke the provider with the context
            value = entry.Provider.DynamicInvoke(context)?.ToString() ?? "";
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Custom context variable '{variableName}' from {entry.ModId} failed: {ex.Message}");
        }
        return false;
    }
    
    /// <summary>
    /// Gets all registered custom variables for UI display.
    /// </summary>
    public static IEnumerable<(string Name, string ModId, string Description, string Type)> GetAllCustomVariables()
    {
        foreach (var entry in CustomPawnVariables.Values.OrderBy(e => e.Priority))
            yield return ($"pawnN.{entry.Name}", entry.ModId, entry.Description ?? "", "Pawn");
        
        foreach (var entry in CustomEnvironmentVariables.Values.OrderBy(e => e.Priority))
            yield return (entry.Name, entry.ModId, entry.Description ?? "", "Environment");
        
        foreach (var entry in CustomContextVariables.Values.OrderBy(e => e.Priority))
            yield return (entry.Name, entry.ModId, entry.Description ?? "", "Context");
    }
    
    /// <summary>
    /// Checks if a pawn variable is registered.
    /// </summary>
    public static bool HasPawnVariable(string variableName)
    {
        return !string.IsNullOrEmpty(variableName) && CustomPawnVariables.ContainsKey(variableName.ToLowerInvariant());
    }
    
    /// <summary>
    /// Checks if an environment variable is registered.
    /// </summary>
    public static bool HasEnvironmentVariable(string variableName)
    {
        return !string.IsNullOrEmpty(variableName) && CustomEnvironmentVariables.ContainsKey(variableName.ToLowerInvariant());
    }
    
    /// <summary>
    /// Checks if a context variable is registered.
    /// </summary>
    public static bool HasContextVariable(string variableName)
    {
        return !string.IsNullOrEmpty(variableName) && CustomContextVariables.ContainsKey(variableName.ToLowerInvariant());
    }
    
    // ===== Pawn Hook API =====
    
    public static void RegisterPawnHook(
        ContextCategory category,
        HookOperation operation,
        string modId,
        Func<Pawn, string, string> handler,
        int priority = 100)
    {
        if (category.Type != ContextType.Pawn)
        {
            Logger.Warning($"RegisterPawnHook: Category '{category}' is not a Pawn category");
            return;
        }
        RegisterHookInternal(category, operation, modId, handler, priority);
    }
    
    public static void InjectPawnSection(
        string sectionName,
        string modId,
        ContextCategory anchor,
        InjectPosition position,
        Func<Pawn, string> provider,
        int priority = 100)
    {
        if (anchor.Type != ContextType.Pawn)
        {
            Logger.Warning($"InjectPawnSection: Anchor '{anchor}' must be a Pawn category");
            return;
        }
        InjectSectionInternal(sectionName, modId, anchor, position, provider, priority);
    }
    
    // ===== Environment Hook API =====
    
    public static void RegisterEnvironmentHook(
        ContextCategory category,
        HookOperation operation,
        string modId,
        Func<Map, string, string> handler,
        int priority = 100)
    {
        if (category.Type != ContextType.Environment)
        {
            Logger.Warning($"RegisterEnvironmentHook: Category '{category}' is not an Environment category");
            return;
        }
        RegisterHookInternal(category, operation, modId, handler, priority);
    }
    
    public static void InjectEnvironmentSection(
        string sectionName,
        string modId,
        ContextCategory anchor,
        InjectPosition position,
        Func<Map, string> provider,
        int priority = 100)
    {
        if (anchor.Type != ContextType.Environment)
        {
            Logger.Warning($"InjectEnvironmentSection: Anchor '{anchor}' must be an Environment category");
            return;
        }
        InjectSectionInternal(sectionName, modId, anchor, position, provider, priority);
    }
    
    // ===== Internal Registration =====
    
    private static void RegisterHookInternal(ContextCategory category, HookOperation operation, string modId, Delegate handler, int priority)
    {
        var entry = new HookEntry(modId, handler, priority);
        var targetDict = operation switch
        {
            HookOperation.Prepend => PrependHooks,
            HookOperation.Append => AppendHooks,
            HookOperation.Override => OverrideHooks,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        
        if (!targetDict.TryGetValue(category, out var list))
        {
            list = new List<HookEntry>();
            targetDict[category] = list;
        }
        list.Add(entry);
        list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        
        Logger.Debug($"Registered {operation} hook for {category} by {modId} (priority {priority})");
    }
    
    private static void InjectSectionInternal(string sectionName, string modId, ContextCategory anchor, InjectPosition position, Delegate provider, int priority)
    {
        InjectedSections.Add(new InjectedSection(sectionName, modId, anchor, position, priority, provider));
        InjectedSections.Sort((a, b) =>
        {
            var anchorCmp = string.Compare(a.Anchor.Key, b.Anchor.Key, StringComparison.Ordinal);
            if (anchorCmp != 0) return anchorCmp;
            var posCmp = a.Position.CompareTo(b.Position);
            if (posCmp != 0) return posCmp;
            return a.Priority.CompareTo(b.Priority);
        });
        
        Logger.Debug($"Injected section '{sectionName}' {position} {anchor} (priority {priority}) by {modId}");
    }
    
    // ===== Apply Hooks =====
    
    public static string ApplyPawnHooks(ContextCategory category, Pawn pawn, string originalValue)
    {
        // Override hooks - first successful one wins
        if (OverrideHooks.TryGetValue(category, out var overrideList))
        {
            foreach (var hook in overrideList)
            {
                try
                {
                    if (hook.Handler is Func<Pawn, string, string> h)
                    {
                        var result = h(pawn, originalValue);
                        if (result != null) return result;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Override hook from '{hook.ModId}' for {category} failed: {ex.Message}");
                }
            }
        }
        
        var value = originalValue;
        
        // Prepend hooks
        if (PrependHooks.TryGetValue(category, out var prependList))
        {
            foreach (var hook in prependList)
            {
                try
                {
                    if (hook.Handler is Func<Pawn, string, string> h)
                        value = h(pawn, value) ?? value;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Prepend hook from '{hook.ModId}' for {category} failed: {ex.Message}");
                }
            }
        }
        
        // Append hooks
        if (AppendHooks.TryGetValue(category, out var appendList))
        {
            foreach (var hook in appendList)
            {
                try
                {
                    if (hook.Handler is Func<Pawn, string, string> h)
                        value = h(pawn, value) ?? value;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Append hook from '{hook.ModId}' for {category} failed: {ex.Message}");
                }
            }
        }
        
        return value;
    }
    
    public static string ApplyEnvironmentHooks(ContextCategory category, Map map, string originalValue)
    {
        // Override hooks
        if (OverrideHooks.TryGetValue(category, out var overrideList))
        {
            foreach (var hook in overrideList)
            {
                try
                {
                    if (hook.Handler is Func<Map, string, string> h)
                    {
                        var result = h(map, originalValue);
                        if (result != null) return result;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Override hook from '{hook.ModId}' for {category} failed: {ex.Message}");
                }
            }
        }
        
        var value = originalValue;
        
        // Prepend hooks
        if (PrependHooks.TryGetValue(category, out var prependList))
        {
            foreach (var hook in prependList)
            {
                try
                {
                    if (hook.Handler is Func<Map, string, string> h)
                        value = h(map, value) ?? value;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Prepend hook from '{hook.ModId}' for {category} failed: {ex.Message}");
                }
            }
        }
        
        // Append hooks
        if (AppendHooks.TryGetValue(category, out var appendList))
        {
            foreach (var hook in appendList)
            {
                try
                {
                    if (hook.Handler is Func<Map, string, string> h)
                        value = h(map, value) ?? value;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Append hook from '{hook.ModId}' for {category} failed: {ex.Message}");
                }
            }
        }
        
        return value;
    }
    
    // ===== Injected Section Queries =====
    
    public static IEnumerable<(string Name, InjectPosition Position, int Priority, Delegate Provider)>
        GetInjectedSectionsAt(ContextCategory anchor)
    {
        return InjectedSections
            .Where(s => s.Anchor.Equals(anchor))
            .OrderBy(s => s.Position)
            .ThenBy(s => s.Priority)
            .Select(s => (s.Name, s.Position, s.Priority, s.Provider));
    }
    
    public static Func<Pawn, string> GetInjectedPawnSection(string name)
    {
        var section = InjectedSections.FirstOrDefault(s =>
            s.Anchor.Type == ContextType.Pawn &&
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        return section?.Provider as Func<Pawn, string>;
    }
    
    public static Func<Map, string> GetInjectedEnvironmentSection(string name)
    {
        var section = InjectedSections.FirstOrDefault(s =>
            s.Anchor.Type == ContextType.Environment &&
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        return section?.Provider as Func<Map, string>;
    }
    
    // ===== Cleanup API =====
    
    public static void UnregisterMod(string modId)
    {
        RemoveFromDict(PrependHooks, modId);
        RemoveFromDict(AppendHooks, modId);
        RemoveFromDict(OverrideHooks, modId);
        InjectedSections.RemoveAll(s => s.ModId == modId);
        
        // Also remove custom variables from this mod
        RemoveCustomVariables(CustomPawnVariables, modId);
        RemoveCustomVariables(CustomEnvironmentVariables, modId);
        RemoveCustomVariables(CustomContextVariables, modId);
        
        Logger.Debug($"Unregistered all hooks and variables from mod: {modId}");
    }
    
    private static void RemoveCustomVariables(Dictionary<string, CustomVariableEntry> dict, string modId)
    {
        var keysToRemove = dict.Where(kvp => kvp.Value.ModId == modId).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
            dict.Remove(key);
    }
    
    private static void RemoveFromDict(Dictionary<ContextCategory, List<HookEntry>> dict, string modId)
    {
        foreach (var key in dict.Keys.ToList())
        {
            dict[key].RemoveAll(e => e.ModId == modId);
            if (dict[key].Count == 0) dict.Remove(key);
        }
    }
    
    public static bool HasAnyHooks => PrependHooks.Count > 0 || AppendHooks.Count > 0 || OverrideHooks.Count > 0;
    public static bool HasAnyInjections => InjectedSections.Count > 0;
    public static bool HasAnyCustomVariables => CustomPawnVariables.Count > 0 || CustomEnvironmentVariables.Count > 0 || CustomContextVariables.Count > 0;
    
    /// <summary>
    /// Clears all registered hooks, injected sections, and custom variables.
    /// Call this when you need to reset the entire registry (e.g., during testing or full mod reload).
    /// </summary>
    public static void Clear()
    {
        PrependHooks.Clear();
        AppendHooks.Clear();
        OverrideHooks.Clear();
        InjectedSections.Clear();
        CustomPawnVariables.Clear();
        CustomEnvironmentVariables.Clear();
        CustomContextVariables.Clear();
        Logger.Debug("ContextHookRegistry cleared all registrations");
    }
}