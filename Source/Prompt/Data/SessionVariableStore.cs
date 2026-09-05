using System.Collections.Generic;

namespace RimTalk.Prompt;

/// <summary>
/// Thread-safe cross-entry session variable store for Scriban evaluation.
/// </summary>
internal static class SessionVariableStore
{
    private static readonly Dictionary<string, object> _sessionVariables = new();
    private static readonly object _sessionLock = new();

    internal static void Reset()
    {
        lock (_sessionLock)
        {
            _sessionVariables.Clear();
        }
    }

    internal static void SetVar(string key, object value)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_sessionLock)
        {
            _sessionVariables[key.ToLowerInvariant()] = value;
        }
    }

    internal static object GetVar(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        lock (_sessionLock)
        {
            return _sessionVariables.TryGetValue(key.ToLowerInvariant(), out var value) ? value : "";
        }
    }
}
