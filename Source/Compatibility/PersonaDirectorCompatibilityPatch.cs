using System;
using System.Reflection;
using HarmonyLib;
using RimTalk.UI;
using RimTalk.Util;
using Verse;

namespace RimTalk.Compatibility;

/// <summary>
/// Temporary compatibility patch for the 'RimTalk: Persona Director' addon mod.
/// Persona Director injects its custom buttons (Edit Notes, Evolve, Set Time) in DoWindowContents
/// without checking whether the active tab is Personality or Memories, causing them to overlay
/// directly on top of the memories table.
/// This patch intercepts Persona Director's Postfix and skips it when the Memories tab is active.
/// When Persona Director resolves this upstream, this file can simply be deleted.
/// </summary>
[StaticConstructorOnStartup]
public static class PersonaDirectorCompatibilityPatch
{
    private static volatile bool _patched;

    static PersonaDirectorCompatibilityPatch()
    {
        var harmony = new Harmony("cj.rimtalk.compat.personadirector");
        TryPatch(harmony);
    }

    public static void TryPatch(Harmony harmony)
    {
        if (_patched) return;

        try
        {
            Type patchType = AccessTools.TypeByName("RimPersonaDirector.Patch_PersonaEditorWindow_DirectorFeatures");
            if (patchType == null) return;

            MethodInfo postfixMethod = AccessTools.Method(patchType, "Postfix");
            if (postfixMethod == null)
            {
                Logger.Warning("Persona Director found but Patch_PersonaEditorWindow_DirectorFeatures.Postfix was missing");
                return;
            }

            harmony.Patch(postfixMethod,
                prefix: new HarmonyMethod(typeof(PersonaDirectorCompatibilityPatch), nameof(Prefix_DirectorFeaturesPostfix)));

            _patched = true;
            Logger.Message("Persona Director compatibility patch applied (isolated buttons to Personality tab)");
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to apply Persona Director compatibility patch: " + ex.Message);
        }
    }

    public static bool Prefix_DirectorFeaturesPostfix(object[] __args)
    {
        if (__args != null && __args.Length > 1 && __args[1] is PersonaEditorWindow pe && !pe.IsPersonalityTabSelected)
        {
            return false;
        }

        return true;
    }
}
