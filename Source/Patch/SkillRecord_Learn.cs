using HarmonyLib;
using RimWorld;
using Verse;
using RimTalk.Service;


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
                TalkService.GenerateTalk(prompt, ___pawn, null, true);
            }
        }
    }
}