using HarmonyLib;
using RimTalk.Data;
using Verse;
using Verse.AI;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
public static class MentalStatePatch
{
    public static void Postfix(Pawn ___pawn, MentalStateDef stateDef, bool __result)
    {
        if (__result && ___pawn != null)
        {
            Cache.Get(___pawn)?.IgnoreAllTalkResponses();
        }
    }
}