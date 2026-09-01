using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Service;

/// <summary>
/// Handles generating bedtime remarks/monologues and waking thoughts.
/// </summary>
public static class SleepDialogueTracker
{
    private const int DailyCooldownTicks = 40000; // ~16 in-game hours (strictly once per daily sleep cycle)
    private const string RelationshipJobDriverTypeName = "rjw.JobDriver_Sex";

    private static readonly Dictionary<int, int> LastBedtimeTicks = new();
    private static readonly Dictionary<int, int> LastWakeUpTicks = new();
    private static readonly HashSet<int> SleepingPawns = new();

    public static void Notify_GoingToBed(Pawn pawn)
    {
        if (!Settings.Get().EnableSleepDialogue) return;
        if (!IsValidForSleepDialogue(pawn)) return;
        if (!IsGoingToSleep(pawn)) return;

        SleepingPawns.Add(pawn.thingIDNumber);

        var pawnState = Cache.Get(pawn);
        if (pawnState == null) return;
        ExpirePendingRequests(pawnState, SleepDialogueKind.WakeUp);

        int ticks = GenTicks.TicksGame;
        if (LastBedtimeTicks.TryGetValue(pawn.thingIDNumber, out int lastTick) && ticks - lastTick < DailyCooldownTicks)
            return;

        LastBedtimeTicks[pawn.thingIDNumber] = ticks;

        var nearbyPawns = PawnSelector.GetNearByTalkablePawns(pawn);
        Pawn partner = nearbyPawns.Count > 0 && IsValidForSleepDialogue(nearbyPawns[0]) ? nearbyPawns[0] : null;

        if (partner != null) LastBedtimeTicks[partner.thingIDNumber] = ticks;

        string prompt = BuildPrompt(SleepDialogueKind.Bedtime, partner);
        pawnState.AddTalkRequest(prompt, partner, TalkType.Sleep, null, SleepDialogueKind.Bedtime);
    }

    public static void Notify_WokeUp(Pawn pawn)
    {
        Notify_WokeUp(pawn, false);
    }

    public static void Notify_WokeUp(Pawn pawn, bool wasAsleep)
    {
        bool wasTrackedSleeping = SleepingPawns.Remove(pawn.thingIDNumber);
        if (!wasTrackedSleeping && !wasAsleep) return;

        var pawnState = Cache.Get(pawn);
        if (pawnState == null) return;
        ExpirePendingRequests(pawnState, SleepDialogueKind.Bedtime);

        if (!Settings.Get().EnableSleepDialogue) return;
        if (!IsValidForSleepDialogue(pawn)) return;

        int ticks = GenTicks.TicksGame;
        if (LastWakeUpTicks.TryGetValue(pawn.thingIDNumber, out int lastWake) && ticks - lastWake < DailyCooldownTicks)
            return;

        LastWakeUpTicks[pawn.thingIDNumber] = ticks;

        var nearbyPawns = PawnSelector.GetNearByTalkablePawns(pawn);
        Pawn nearbyColonist = nearbyPawns.Count > 0 && IsValidForSleepDialogue(nearbyPawns[0]) ? nearbyPawns[0] : null;

        if (nearbyColonist != null) LastWakeUpTicks[nearbyColonist.thingIDNumber] = ticks;

        string prompt = BuildPrompt(SleepDialogueKind.WakeUp, nearbyColonist);
        pawnState.AddTalkRequest(prompt, nearbyColonist, TalkType.Sleep, null, SleepDialogueKind.WakeUp);
    }

    public static void Notify_SleepInterrupted(Pawn pawn)
    {
        if (pawn == null) return;

        SleepingPawns.Remove(pawn.thingIDNumber);

        var pawnState = Cache.Get(pawn);
        if (pawnState == null) return;

        ExpirePendingRequests(pawnState, SleepDialogueKind.Bedtime);
        ExpirePendingRequests(pawnState, SleepDialogueKind.WakeUp);
    }

    public static bool IsGoingToSleep(Pawn pawn)
    {
        if (pawn?.needs?.rest == null) return false;
        bool tired = pawn.needs.rest.CurLevel < 0.75f;
        bool sleepScheduled = pawn.timetable?.CurrentAssignment == TimeAssignmentDefOf.Sleep;
        return tired || sleepScheduled;
    }

    public static bool TryRefreshRequest(TalkRequest request)
    {
        if (request == null) return false;
        if (request.TalkType != TalkType.Sleep || request.SleepDialogueKind == SleepDialogueKind.None) return true;

        if (!IsRequestStillValid(request))
        {
            TalkRequestPool.AddToHistory(request, RequestStatus.Expired);
            Cache.Get(request.Initiator)?.TalkRequests.Remove(request);
            return false;
        }

        var nearbyPawns = PawnSelector.GetNearByTalkablePawns(request.Initiator);
        Pawn partner = nearbyPawns.FirstOrDefault(p => p == request.Recipient && IsValidForSleepDialogue(p))
                       ?? nearbyPawns.FirstOrDefault(IsValidForSleepDialogue);

        string prompt = BuildPrompt(request.SleepDialogueKind, partner);
        request.Recipient = partner;
        request.IsMonologue = partner == null;
        request.Prompt = prompt;
        request.RawPrompt = prompt;
        return true;
    }

    public static bool IsSleepJob(JobDef jobDef)
    {
        return jobDef == JobDefOf.LayDown || jobDef == JobDefOf.Wait_Asleep;
    }

    public static bool IsBedtimeInterruptionJob(JobDef jobDef)
    {
        if (jobDef == JobDefOf.Lovin) return true;

        for (Type driverType = jobDef?.driverClass; driverType != null; driverType = driverType.BaseType)
        {
            if (driverType.FullName == RelationshipJobDriverTypeName)
                return true;
        }

        return false;
    }

    private static bool IsRequestStillValid(TalkRequest request)
    {
        if (!Settings.Get().EnableSleepDialogue) return false;

        Pawn pawn = request.Initiator;
        if (!IsValidForSleepDialogue(pawn)) return false;

        return request.SleepDialogueKind switch
        {
            SleepDialogueKind.Bedtime => pawn.Awake() && IsSleepJob(pawn.CurJobDef) && IsGoingToSleep(pawn),
            SleepDialogueKind.WakeUp => pawn.Awake() && !IsSleepJob(pawn.CurJobDef),
            _ => true
        };
    }

    private static void ExpirePendingRequests(PawnState pawnState, SleepDialogueKind kind)
    {
        var node = pawnState.TalkRequests.First;
        while (node != null)
        {
            var next = node.Next;
            var request = node.Value;
            if (request.TalkType == TalkType.Sleep && request.SleepDialogueKind == kind)
            {
                TalkRequestPool.AddToHistory(request, RequestStatus.Expired);
                pawnState.TalkRequests.Remove(node);
            }
            node = next;
        }
    }

    private static string BuildPrompt(SleepDialogueKind kind, Pawn partner)
    {
        return kind switch
        {
            SleepDialogueKind.Bedtime when partner != null =>
                $"Heading to bed, saying a brief sleepy remark to {partner.LabelShort}",
            SleepDialogueKind.Bedtime =>
                "Heading to bed, making a brief sleepy remark before resting",
            SleepDialogueKind.WakeUp when partner != null =>
                $"Just woke up, greeting {partner.LabelShort} with a brief waking remark",
            SleepDialogueKind.WakeUp =>
                "Just woke up, making a brief waking remark about rest, mood, or what to do next",
            _ => string.Empty
        };
    }

    private static bool IsValidForSleepDialogue(Pawn pawn)
    {
        if (pawn is not { Spawned: true, Dead: false, Downed: false }) return false;
        if (pawn.IsEnemy() || pawn.IsPrisoner || pawn.IsInDanger(true) || pawn.InMentalState) return false;
        if (ModsConfig.BiotechActive && pawn.Deathresting) return false;
        if (pawn.health?.hediffSet?.HasHediff(HediffDefOf.Anesthetic) == true) return false;
        if (pawn.CurJob?.restUntilHealed == true) return false;
        if (pawn.CurrentBed()?.Medical == true) return false;

        return true;
    }

    public static void Reset()
    {
        LastBedtimeTicks.Clear();
        LastWakeUpTicks.Clear();
        SleepingPawns.Clear();
    }
}
