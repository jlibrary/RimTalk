using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch]
    public static class InteractionTextPatch
    {
        private static readonly ConditionalWeakTable<PlayLogEntry_Interaction, string> CustomTextTable =
            new ConditionalWeakTable<PlayLogEntry_Interaction, string>();

        public static void SetTextFor(PlayLogEntry_Interaction entry, string text)
        {
            CustomTextTable.Add(entry, text);
        }
        
        public static bool IsRimTalkInteraction(PlayLogEntry_Interaction entry)
        {
            return CustomTextTable.TryGetValue(entry, out _);
        }

        [HarmonyPatch(typeof(PlayLogEntry_Interaction), "ToGameStringFromPOV_Worker")]
        [HarmonyPostfix]
        public static void ToGameStringFromPOV_Worker_Postfix(PlayLogEntry_Interaction __instance, ref string __result)
        {
            if (CustomTextTable.TryGetValue(__instance, out var customText))
            {
                __result = customText;
            }
        }
    }
}