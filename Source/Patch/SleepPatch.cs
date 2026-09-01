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

        // 1. Started sleeping (transition from non-sleep -> sleep)
        if (!prevWasSleep && currIsSleep)
        {
            SleepDialogueTracker.Notify_GoingToBed(___pawn);
        }
        // 2. Left a sleep job, either by waking normally or for a bedtime interruption.
        else if (prevWasSleep && !currIsSleep)
        {
            if (SleepDialogueTracker.IsBedtimeInterruptionJob(newJob.def))
                SleepDialogueTracker.Notify_SleepInterrupted(___pawn);
            else
                SleepDialogueTracker.Notify_WokeUp(___pawn, __state.wasAsleep);
        }
    }
}
