using System.Collections.Generic;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Global variable store - stored in global settings.
/// </summary>
public class VariableStore : IExposable
{
    /// <summary>Global variables dictionary</summary>
    private Dictionary<string, string> _variables = new();

    /// <summary>Sets a variable</summary>
    public void SetVar(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        _variables[key.ToLowerInvariant()] = value ?? "";
    }

    /// <summary>Gets a variable</summary>
    public string GetVar(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        return _variables.TryGetValue(key.ToLowerInvariant(), out var value) ? value : "";
    }

    /// <summary>Gets a variable, returns default value if not found</summary>
    public string GetVar(string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(key)) return defaultValue;
        var value = GetVar(key);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    /// <summary>Checks if a variable exists</summary>
    public bool HasVar(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return _variables.ContainsKey(key.ToLowerInvariant());
    }

    /// <summary>Removes a variable</summary>
    public bool RemoveVar(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return _variables.Remove(key.ToLowerInvariant());
    }

    /// <summary>Clears all variables</summary>
    public void Clear()
    {
        _variables.Clear();
    }

    /// <summary>Gets all variables (for UI display)</summary>
    public IReadOnlyDictionary<string, string> GetAllVariables() => _variables;

    /// <summary>Gets the variable count</summary>
    public int Count => _variables.Count;

    public void ExposeData()
    {
        Scribe_Collections.Look(ref _variables, "variables", LookMode.Value, LookMode.Value);
        _variables ??= new Dictionary<string, string>();
    }
}