using System;
using RimTalk.Compatibility;
using Verse;

namespace RimTalk.Patch;

/// <summary>
/// Backward-compatibility bridge for third-party Harmony addons.
/// Active patch logic is now managed by <see cref="BubbleCompatibilityPatch"/>.
/// </summary>
[Obsolete("Use RimTalk.Compatibility.BubbleCompatibilityPatch instead.")]
public static class BubbleCompatibility
{
    public static bool IsBubblesModLoaded => BubbleCompatibilityPatch.IsBubblesModLoaded;
}

// Maintained for strict ABI / Harmony addon backward compatibility
[Obsolete("Interaction Bubbles compatibility bridge. Active patch logic moved to RimTalk.Compatibility.BubbleCompatibilityPatch.")]
public static class Bubbler_Add
{
    public static bool Prefix(LogEntry entry)
    {
        return BubbleCompatibilityPatch.OnBubblerAddPrefix(entry);
    }

    public static void Postfix() { } // Preserved for external addon ABI
}

// Maintained for strict ABI / Harmony addon backward compatibility
[Obsolete("Interaction Bubbles compatibility bridge. Active patch logic moved to RimTalk.Compatibility.BubbleCompatibilityPatch.")]
public static class Bubbler_Draw
{
    public static bool Prefix()
    {
        return BubbleCompatibilityPatch.OnBubblerDrawPrefix();
    }
}
