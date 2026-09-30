using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimTalk.Service;

/// <summary>
/// Tracks first-time pairwise encounters between pawns to prevent repeated stranger greeting dialogues.
/// </summary>
public static class EncounterTracker
{
    private const int MaxMetPairs = 2000;
    private static readonly HashSet<long> MetPairs = [];
    private static readonly Queue<long> MetOrder = new();
    private static readonly object MetLock = new();
    
    public static bool IsStrangerEncounter(Pawn mainPawn, Pawn partner)
    {
        if (mainPawn == null || partner == null) return false;
        try
        {
            // 1. Never apply to prisoners, slaves, or enemies
            if (mainPawn.IsPrisoner || partner.IsPrisoner || mainPawn.IsSlave || partner.IsSlave || mainPawn.IsEnemy() || partner.IsEnemy())
                return false;

            // 2. Only allow for outside visitors or strangers (including Anomaly creepjoiners with Faction == null)
            if (!IsVisitorOrStranger(mainPawn) && !IsVisitorOrStranger(partner))
                return false;

            // 3. Caravan / group members already know each other
            var lord = mainPawn.GetLord();
            if (lord != null && lord == partner.GetLord())
                return false;

            // 4. Existing relations or friends
            if (mainPawn.GetMostImportantRelation(partner) != null)
                return false;
            if (mainPawn.relations != null && mainPawn.relations.OpinionOf(partner) >= 20f)
                return false;

            // 5. Already met in this save
            long key = GetPairKey(mainPawn, partner);
            lock (MetLock)
            {
                return !MetPairs.Contains(key);
            }
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    public static void Notify_DialogueFinished(TalkRequest req)
    {
        try
        {
            if (req == null || req.IsMonologue || req.Participants is not { Count: >= 2 }) return;

            var p0 = req.Initiator ?? req.Participants[0];
            var p1 = req.Recipient ?? (req.Participants[0] == p0 ? req.Participants[1] : req.Participants[0]);
            if (p0 == null || p1 == null || p0 == p1) return;

            // Only track visitor/stranger encounters; never waste slots on prisoners, slaves, or enemies
            if (p0.IsPrisoner || p1.IsPrisoner || p0.IsSlave || p1.IsSlave || p0.IsEnemy() || p1.IsEnemy()) return;
            if (!IsVisitorOrStranger(p0) && !IsVisitorOrStranger(p1)) return;

            long key = GetPairKey(p0, p1);
            lock (MetLock)
            {
                if (!MetPairs.Add(key)) return;
                MetOrder.Enqueue(key);
                while (MetOrder.Count > MaxMetPairs)
                {
                    MetPairs.Remove(MetOrder.Dequeue());
                }
            }
        }
        catch (System.Exception)
        {
            // ignored
        }
    }

    private static bool IsVisitorOrStranger(Pawn pawn)
    {
        if (pawn == null) return false;
        if (pawn.IsPrisoner || pawn.IsSlave || pawn.IsEnemy()) return false;
        if (pawn.Faction != null && pawn.Faction.IsPlayer) return false;

        // Standard faction visitors/traders OR Anomaly creepjoiners/strangers without faction
        return pawn.IsVisitor() || (pawn.Faction == null && !pawn.IsColonist && pawn.RaceProps.Humanlike);
    }


    public static void ExposeData()
    {
        List<long> metSaveList = null;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            lock (MetLock)
            {
                metSaveList = MetOrder.ToList();
            }
        }

        Scribe_Collections.Look(ref metSaveList, "rimtalkMetPairs", LookMode.Value);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            lock (MetLock)
            {
                MetOrder.Clear();
                MetPairs.Clear();
                if (metSaveList != null)
                {
                    foreach (var key in metSaveList)
                    {
                        MetOrder.Enqueue(key);
                        MetPairs.Add(key);
                    }
                }
            }
        }
    }
    
    private static long GetPairKey(Pawn a, Pawn b)
    {
        uint x = (uint)a.thingIDNumber;
        uint y = (uint)b.thingIDNumber;
        return x < y ? (long)(((ulong)x << 32) | y) : (long)(((ulong)y << 32) | x);
    }

    public static void Reset()
    {
        lock (MetLock)
        {
            MetPairs.Clear();
            MetOrder.Clear();
        }
    }
}
