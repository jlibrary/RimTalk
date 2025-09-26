using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patches;

[HarmonyPatch(typeof(GameDataSaveLoader), nameof(GameDataSaveLoader.SaveGame))]
public static class SaveGamePatch
{
    [HarmonyPrefix]
    public static void PreSaveGame()
    {
        try
        {
            var entries = Find.PlayLog?.AllEntries;
            if (entries == null) return;
                
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is PlayLogEntry_RimTalkInteraction rimTalkEntry)
                {
                    var newEntry = new PlayLogEntry_Interaction(
                        InteractionDefOf.Chitchat,
                        rimTalkEntry.Initiator,
                        rimTalkEntry.Recipient,
                        rimTalkEntry.ExtraSentencePacks ?? new List<RulePackDef>()
                    );

                    var ageTicksField = typeof(LogEntry).GetField("ticksAbs", BindingFlags.NonPublic | BindingFlags.Instance);
                    ageTicksField?.SetValue(newEntry, rimTalkEntry.TicksAbs);

                    // Register the custom text for the new entry
                    InteractionTextPatch.SetTextFor(newEntry, rimTalkEntry.CachedString);

                    entries[i] = newEntry;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error converting RimTalk interactions: {ex}");
        }
    }
}

[HarmonyPatch]
public static class InteractionTextPatch
{
    private static readonly ConditionalWeakTable<LogEntry, string> CustomTextTable =
        new ConditionalWeakTable<LogEntry, string>();

    public static void SetTextFor(LogEntry entry, string text)
    {
        // Remove existing entry if it exists, then add the new one
        if (CustomTextTable.TryGetValue(entry, out _))
        {
            CustomTextTable.Remove(entry);
        }
        CustomTextTable.Add(entry, text);
    }
        
    public static bool IsRimTalkInteraction(LogEntry entry)
    {
        return CustomTextTable.TryGetValue(entry, out _);
    }
        
    [HarmonyPatch(typeof(LogEntry), nameof(LogEntry.ToGameStringFromPOV))]
    [HarmonyPostfix]
    public static void ToGameStringFromPOV_Postfix(LogEntry __instance, ref string __result)
    {
        if (CustomTextTable.TryGetValue(__instance, out var customText))
        {
            __result = customText;
        }
    }

}