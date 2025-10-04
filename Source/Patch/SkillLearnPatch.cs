using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
public class SkillLevelUpPatch
{
    private static int _previousLevel;

    [HarmonyPrefix]
    public static void TrackPreviousLevel(SkillRecord __instance)
    {
        _previousLevel = __instance.Level;
    }

    [HarmonyPostfix]
    public static void CatchLevelUp(SkillRecord __instance, Pawn ___pawn)
    {
        if (__instance.Level > _previousLevel)
        {
            string prompt = $"{___pawn.Name} leveled up {__instance.def.defName} from {_previousLevel} " +
                            $"to {__instance.Level} ({__instance.LevelDescriptor})";
            Cache.Get(___pawn)?.AddTalkRequest(prompt, talkType: TalkType.LevelUp);
        }
    }
}