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
        JobDef previousJob = ___pawn?.CurJobDef;
        bool wasAsleep = ___pawn != null && SleepDialogueTracker.IsSleepJob(previousJob) && !___pawn.Awake();
        __state = (previousJob, wasAsleep);
    }

    public static void Postfix(Pawn ___pawn, Job newJob, (JobDef prevJob, bool wasAsleep) __state)
    {
        if (___pawn == null || newJob == null) return;

        bool prevWasSleep = SleepDialogueTracker.IsSleepJob(__state.prevJob);
        bool currIsSleep = SleepDialogueTracker.IsSleepJob(newJob.def);

        // Some relationship jobs end LayDown before starting their own job, so the
        // sleep-to-job transition is not always direct.
        if (SleepDialogueTracker.IsBedtimeInterruptionJob(newJob.def))
        {
            SleepDialogueTracker.Notify_SleepInterrupted(___pawn);
            return;
        }

        // 1. Started sleeping (transition from non-sleep -> sleep)
        if (!prevWasSleep && currIsSleep)
        {
            SleepDialogueTracker.Notify_GoingToBed(___pawn);
        }
        // 2. Left a sleep job without entering a recognized bedtime interruption.
        else if (prevWasSleep && !currIsSleep)
        {
            SleepDialogueTracker.Notify_WokeUp(___pawn, __state.wasAsleep);
        }
    }
}
