using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.PawnMemory;
using RimTalk.Source.Data;
using RimTalk.Service;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Util;

public static partial class PawnUtil
{
    /// <summary>
    /// Checks if there are any active, conscious, non-downed hostile pawns on the map.
    /// </summary>
    public static bool HasActiveHostiles(this Pawn pawn)
    {
        if (pawn?.Map == null) return false;

        Faction referenceFaction = GetReferenceFaction(pawn);
        if (referenceFaction == null) return false;

        var hostileTargets = pawn.Map.attackTargetsCache?.TargetsHostileToFaction(referenceFaction);
        if (hostileTargets == null) return false;

        foreach (var target in hostileTargets)
        {
            if (target.Thing is not Pawn threatPawn || threatPawn.Downed || threatPawn.Dead)
                continue;

            if (IsValidThreat(pawn, threatPawn) && GenHostility.IsActiveThreatTo(target, referenceFaction))
                return true;
        }

        return false;
    }

    public static (int totalCount, Pawn nearest, string summary, string dangerAssessment, bool isSevere) GetHostileThreatInfo(this Pawn pawn)
    {
        if (pawn?.Map == null) return (0, null, null, null, false);

        Faction referenceFaction = GetReferenceFaction(pawn);
        if (referenceFaction == null) return (0, null, null, null, false);

        var hostileTargets = pawn.Map.attackTargetsCache?.TargetsHostileToFaction(referenceFaction);
        if (hostileTargets == null) return (0, null, null, null, false);

        Pawn closestPawn = null;
        float closestDistSq = float.MaxValue;
        int totalCount = 0;
        float enemyPower = 0f;
        int fleeingCount = 0;
        var threatCounts = new Dictionary<string, int>();

        foreach (var target in hostileTargets)
        {
            if (!GenHostility.IsActiveThreatTo(target, referenceFaction))
                continue;

            if (target.Thing is not Pawn threatPawn || threatPawn.Downed || threatPawn.Dead)
                continue;

            if (!IsValidThreat(pawn, threatPawn))
                continue;

            totalCount++;
            enemyPower += threatPawn.kindDef?.combatPower ?? 50f;

            if (threatPawn.MentalStateDef == MentalStateDefOf.PanicFlee ||
                threatPawn.CurJobDef == JobDefOf.Flee || threatPawn.CurJobDef == JobDefOf.FleeAndCower ||
                threatPawn.GetLord()?.CurLordToil is LordToil_PanicFlee)
            {
                fleeingCount++;
            }

            string label = GetThreatLabel(threatPawn, referenceFaction);
            threatCounts[label] = threatCounts.TryGetValue(label, out int c) ? c + 1 : 1;

            float distSq = pawn.Position.DistanceToSquared(threatPawn.Position);
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closestPawn = threatPawn;
            }
        }

        if (totalCount == 0 || closestPawn == null)
            return (0, null, null, null, false);

        // Calculate ally combat power on map
        float allyPower = 0f;
        var spawnedAllies = pawn.Map.mapPawns?.SpawnedPawnsInFaction(referenceFaction);
        if (spawnedAllies != null)
        {
            foreach (var ally in spawnedAllies)
            {
                if (ally.Downed || ally.Dead || !ally.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                    continue;
                allyPower += ally.kindDef?.combatPower ?? 50f;
            }
        }
        if (allyPower < 50f) allyPower = 50f;

        float ratio = enemyPower / allyPower;
        string dangerAssessment;
        bool isSevere;

        if (fleeingCount >= totalCount)
        {
            dangerAssessment = "Enemies Fleeing";
            isSevere = false;
        }
        else if (ratio < 0.35f)
        {
            dangerAssessment = "Low Danger";
            isSevere = false;
        }
        else if (ratio <= 1.25f)
        {
            dangerAssessment = "Moderate Danger";
            isSevere = true;
        }
        else
        {
            dangerAssessment = "Severe Danger";
            isSevere = true;
        }

        var parts = new List<string>(threatCounts.Count);
        foreach (var kv in threatCounts)
        {
            parts.Add(kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key);
        }
        string summary = string.Join(", ", parts);
        return (totalCount, closestPawn, summary, dangerAssessment, isSevere);
    }

    public static Pawn GetHostilePawnNearBy(this Pawn pawn)
    {
        if (pawn?.Map == null) return null;

        Faction referenceFaction = GetReferenceFaction(pawn);
        if (referenceFaction == null) return null;

        var hostileTargets = pawn.Map.attackTargetsCache?.TargetsHostileToFaction(referenceFaction);
        if (hostileTargets == null) return null;

        Pawn closestPawn = null;
        float closestDistSq = float.MaxValue;

        foreach (var target in hostileTargets)
        {
            if (!GenHostility.IsActiveThreatTo(target, referenceFaction))
                continue;

            if (target.Thing is not Pawn threatPawn || threatPawn.Downed || threatPawn.Dead)
                continue;

            if (!IsValidThreat(pawn, threatPawn))
                continue;

            float distSq = pawn.Position.DistanceToSquared(threatPawn.Position);
            if (distSq < closestDistSq)
            {
                closestDistSq = distSq;
                closestPawn = threatPawn;
            }
        }

        return closestPawn;
    }

    private static Faction GetReferenceFaction(Pawn pawn)
    {
        if (pawn.IsPrisoner || pawn.IsSlave || pawn.IsFreeColonist ||
            pawn.IsVisitor() || pawn.IsQuestLodger())
        {
            return Faction.OfPlayer;
        }

        return pawn.Faction;
    }

    private static bool IsValidThreat(Pawn observer, Pawn threat)
    {
        if (Faction.OfPlayer == null)
            return true;

        // Filter out prisoners/slaves as threats to colonists
        if (threat.IsPrisoner && threat.HostFaction == Faction.OfPlayer)
            return false;

        if (threat.IsSlave && threat.HostFaction == Faction.OfPlayer)
            return false;

        // Prisoners don't threaten each other
        if (observer.IsPrisoner && threat.IsPrisoner)
            return false;

        Lord lord = threat.GetLord();

        // Exclude tactically retreating pawns
        if (lord is { CurLordToil: LordToil_ExitMapFighting or LordToil_ExitMap })
            return false;

        if (threat.CurJob?.exitMapOnArrival == true)
            return false;

        // Exclude roaming mech cluster pawns
        if (threat.RaceProps.IsMechanoid && lord is { CurLordToil: LordToil_DefendPoint })
            return false;

        return true;
    }

    private static string GetThreatLabel(Pawn threat, Faction referenceFaction = null)
    {
        if (threat == null) return "unknown threat";

        var allyFaction = referenceFaction ?? Faction.OfPlayer;
        if (allyFaction != null && threat.Faction == allyFaction)
            return $"{threat.LabelShort} ({threat.MentalStateDef?.label ?? "ally"})";

        if (threat.RaceProps.Humanlike)
        {
            if (ModsConfig.BiotechActive && threat.genes?.Xenotype != null)
                return threat.genes.XenotypeLabel;

            return threat.def.LabelCap.RawText;
        }

        return threat.KindLabel;
    }
}
