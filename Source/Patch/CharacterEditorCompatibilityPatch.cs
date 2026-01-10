using System;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.Patch;

// This is meant to be a temporary compatibility patch for Character Editor mod;
// As soon as communicated with its author, it will be removed/edited as needed.
[StaticConstructorOnStartup]
public static class CharacterEditorCompatibilityPatch
{
    private const string PersonaMarker = "RIMTALK_PERSONA:";
    private static volatile bool _patched;

    static CharacterEditorCompatibilityPatch()
    {
        var harmony = new Harmony("cj.rimtalk.compat.charactereditor");
        TryPatch(harmony);
    }

    public static void TryPatch(Harmony harmony)
    {
        if (_patched) return;

        try
        {
            var healthToolType = AccessTools.TypeByName("CharacterEditor.HealthTool");
            if (healthToolType == null) return;

            var getAllHediffsMethod = AccessTools.Method(healthToolType, "GetAllHediffsAsSeparatedString");
            var setHediffsMethod = AccessTools.Method(healthToolType, "SetHediffsFromSeparatedString");

            if (getAllHediffsMethod == null || setHediffsMethod == null)
            {
                Logger.Warning("Character Editor found but methods missing, compatibility patch skipped");
                return;
            }

            harmony.Patch(getAllHediffsMethod,
                postfix: new HarmonyMethod(typeof(CharacterEditorCompatibilityPatch), nameof(AppendPersonaData)));
            harmony.Patch(setHediffsMethod,
                postfix: new HarmonyMethod(typeof(CharacterEditorCompatibilityPatch), nameof(RestorePersonaData)));

            _patched = true;
            Logger.Message("Character Editor compatibility enabled");
        }
        catch (Exception e)
        {
            Logger.Warning("Failed to apply Character Editor compatibility patch: " + e.Message);
        }
    }

    public static void AppendPersonaData(Pawn p, ref string __result)
    {
        if (p == null) return;

        try
        {
            var personality = PersonaService.GetPersonality(p);
            if (string.IsNullOrEmpty(personality)) return;

            var chattiness = PersonaService.GetTalkInitiationWeight(p);
            var encoded = Uri.EscapeDataString(personality);
            var personaData = PersonaMarker + encoded + "|" + chattiness;

            __result = string.IsNullOrEmpty(__result)
                ? personaData
                : __result + ":" + personaData;
        }
        catch (Exception e)
        {
            Logger.Warning("Failed to export persona for " + p.LabelShort + ": " + e.Message);
        }
    }

    public static void RestorePersonaData(Pawn p, string s)
    {
        if (p == null || string.IsNullOrEmpty(s)) return;

        try
        {
            int markerIndex = s.IndexOf(PersonaMarker, StringComparison.Ordinal);
            if (markerIndex < 0) return;

            int dataStart = markerIndex + PersonaMarker.Length;
            int entryEnd = s.IndexOf(':', dataStart);
            if (entryEnd < 0) entryEnd = s.Length;

            var dataSection = s.Substring(dataStart, entryEnd - dataStart);
            int lastPipe = dataSection.LastIndexOf('|');
            if (lastPipe < 0) return;

            var encodedPersonality = dataSection.Substring(0, lastPipe);
            var chattinessStr = dataSection.Substring(lastPipe + 1);

            var personality = Uri.UnescapeDataString(encodedPersonality);
            if (string.IsNullOrEmpty(personality)) return;

            if (float.TryParse(chattinessStr, out float chattiness))
            {
                PersonaService.SetPersonality(p, personality);
                PersonaService.SetTalkInitiationWeight(p, chattiness);
            }
        }
        catch (Exception e)
        {
            Logger.Warning("Failed to restore persona for " + p.LabelShort + ": " + e.Message);
        }
    }
}