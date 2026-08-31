using System.Collections.Generic;
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

    private static readonly Dictionary<int, int> LastBedtimeTicks = new();
    private static readonly Dictionary<int, int> LastWakeUpTicks = new();
    private static readonly HashSet<int> SleepingPawns = new();

    public static void Notify_GoingToBed(Pawn pawn)
    {
        if (!IsValidForSleepDialogue(pawn)) return;
        if (!IsGoingToSleep(pawn)) return;

        SleepingPawns.Add(pawn.thingIDNumber);

        int ticks = GenTicks.TicksGame;
        if (LastBedtimeTicks.TryGetValue(pawn.thingIDNumber, out int lastTick) && ticks - lastTick < DailyCooldownTicks)
            return;

        LastBedtimeTicks[pawn.thingIDNumber] = ticks;

        var pawnState = Cache.Get(pawn);
        if (pawnState == null) return;

        var nearbyPawns = PawnSelector.GetNearByTalkablePawns(pawn);
        Pawn partner = nearbyPawns.Count > 0 && IsValidForSleepDialogue(nearbyPawns[0]) ? nearbyPawns[0] : null;

        string timeStr = GetInGameTimeString(pawn);
        if (partner != null)
        {
            LastBedtimeTicks[partner.thingIDNumber] = ticks;
            pawnState.AddTalkRequest($"Heading to bed to sleep at {timeStr}, saying a brief sleepy remark to {partner.LabelShort}", partner, TalkType.Sleep);
        }
        else
        {
            pawnState.AddTalkRequest($"Heading to bed to sleep at {timeStr}, a short sleepy thought before resting", null, TalkType.Sleep);
        }
    }

    public static void Notify_WokeUp(Pawn pawn)
    {
        Notify_WokeUp(pawn, false);
    }

    public static void Notify_WokeUp(Pawn pawn, bool wasAsleep)
    {
        bool wasTrackedSleeping = SleepingPawns.Remove(pawn.thingIDNumber);
        if (!wasTrackedSleeping && !wasAsleep) return;

        if (!IsValidForSleepDialogue(pawn)) return;

        int ticks = GenTicks.TicksGame;
        if (LastWakeUpTicks.TryGetValue(pawn.thingIDNumber, out int lastWake) && ticks - lastWake < DailyCooldownTicks)
            return;

        LastWakeUpTicks[pawn.thingIDNumber] = ticks;

        var pawnState = Cache.Get(pawn);
        if (pawnState == null) return;

        var nearbyPawns = PawnSelector.GetNearByTalkablePawns(pawn);
        Pawn nearbyColonist = nearbyPawns.Count > 0 && IsValidForSleepDialogue(nearbyPawns[0]) ? nearbyPawns[0] : null;

        string timeStr = GetInGameTimeString(pawn);
        if (nearbyColonist != null)
        {
            LastWakeUpTicks[nearbyColonist.thingIDNumber] = ticks;
            pawnState.AddTalkRequest($"Just woke up from sleep at {timeStr}, greeting {nearbyColonist.LabelShort}", nearbyColonist, TalkType.Sleep);
        }
        else
        {
            pawnState.AddTalkRequest($"Just woke up from sleep at {timeStr}, thinking about rest, mood, or what to do next", null, TalkType.Sleep);
        }
    }

    public static bool IsGoingToSleep(Pawn pawn)
    {
        if (pawn?.needs?.rest == null) return false;
        bool tired = pawn.needs.rest.CurLevel < 0.75f;
        bool sleepScheduled = pawn.timetable?.CurrentAssignment == TimeAssignmentDefOf.Sleep;
        return tired || sleepScheduled;
    }

    private static string GetInGameTimeString(Pawn pawn)
    {
        var map = pawn?.Map ?? Find.CurrentMap;
        if (map != null && Find.WorldGrid != null && Find.TickManager != null)
        {
            var longLat = Find.WorldGrid.LongLatOf(map.Tile);
            return CommonUtil.GetInGameHour12HString(Find.TickManager.TicksAbs, longLat);
        }
        return CommonUtil.GetInGameData().Hour12HString;
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
