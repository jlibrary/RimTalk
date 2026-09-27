using HarmonyLib;
using RimTalk.Memory;
using Verse;

namespace RimTalk.Patch;

/// <summary>
/// Intercepts pawn death to record kill events, colonist homicide trauma,
/// and prisoner executions in episodic memory and core mindset.
/// </summary>
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
public static class DeathPatch
{
    public static void Postfix(Pawn __instance, DamageInfo? dinfo)
    {
        MemoryHookService.TryRecordKill(__instance, dinfo);
    }
}
