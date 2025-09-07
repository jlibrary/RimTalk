using HarmonyLib;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public class SkillLevelUpPatch
    {
        private static int previousLevel;

        [HarmonyPrefix]
        public static void TrackPreviousLevel(SkillRecord __instance)
        {
            previousLevel = __instance.Level;
        }

        [HarmonyPostfix]
        public static void CatchLevelUp(SkillRecord __instance, Pawn ___pawn)
        {
            if (__instance.Level > previousLevel)
            {
                string prompt = $"{___pawn.Name} leveled up {__instance.def.defName} from {previousLevel} " +
                                $"to {__instance.Level} ({__instance.LevelDescriptor})";
                Cache.Get(___pawn)?.AddTalkRequest(prompt);
            }
        }
    }
}