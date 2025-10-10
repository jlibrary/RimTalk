using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
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

            var worldComp = Find.World.GetComponent<RimTalkWorldComponent>();
            if (worldComp == null)
            {
                Logger.Error("RimTalkWorldComponent not found");
                return;
            }
                
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is not PlayLogEntry_RimTalkInteraction rimTalkEntry) continue;
                var newEntry = new PlayLogEntry_Interaction(
                    InteractionDefOf.Chitchat,
                    rimTalkEntry.Initiator,
                    rimTalkEntry.Recipient,
                    rimTalkEntry.ExtraSentencePacks ?? []
                );

                var ageTicksField = typeof(LogEntry).GetField("ticksAbs", BindingFlags.NonPublic | BindingFlags.Instance);
                ageTicksField?.SetValue(newEntry, rimTalkEntry.TicksAbs);
                    
                worldComp.SetTextFor(newEntry, rimTalkEntry.CachedString);

                entries[i] = newEntry;
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
    public static bool IsRimTalkInteraction(LogEntry entry)
    {
        if (entry == null) return false;
        var worldComp = Find.World?.GetComponent<RimTalkWorldComponent>();
        return worldComp != null && worldComp.RimTalkInteractionTexts.ContainsKey(entry.GetUniqueLoadID());
    }
    
    [HarmonyPatch(typeof(LogEntry), nameof(LogEntry.ToGameStringFromPOV))]
    [HarmonyPostfix]
    public static void ToGameStringFromPOV_Postfix(LogEntry __instance, ref string __result)
    {
        var worldComp = Find.World?.GetComponent<RimTalkWorldComponent>();
        if (worldComp != null && worldComp.TryGetTextFor(__instance, out var customText))
        {
            __result = customText;
        }
    }
}