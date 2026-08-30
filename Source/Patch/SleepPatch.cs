using HarmonyLib;
using RimTalk.Service;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
public static class SleepPatch_StartJob
{
    public static void Prefix(Pawn ___pawn, Job newJob, out (JobDef prevJob, bool wasAsleep) __state)
    {
        __state = (___pawn?.CurJobDef, ___pawn != null && !___pawn.Awake());
    }

    public static void Postfix(Pawn ___pawn, Job newJob, (JobDef prevJob, bool wasAsleep) __state)
    {
        if (___pawn == null || newJob == null) return;

        bool prevWasSleep = __state.prevJob == JobDefOf.LayDown || __state.prevJob == JobDefOf.Wait_Asleep;
        bool currIsSleep = newJob.def == JobDefOf.LayDown || newJob.def == JobDefOf.Wait_Asleep;

        // 1. Started sleeping (transition from non-sleep -> sleep)
        if (!prevWasSleep && currIsSleep)
        {
            SleepDialogueTracker.Notify_GoingToBed(___pawn);
        }
        // 2. Woke up (transition from sleep -> non-sleep)
        else if (prevWasSleep && !currIsSleep)
        {
            SleepDialogueTracker.Notify_WokeUp(___pawn, __state.wasAsleep);
        }
    }
}
