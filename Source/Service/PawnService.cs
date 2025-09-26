using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimTalk.Service;

public static class PawnService
{
    public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
    {
        var thoughts = new List<Thought>();
        pawn?.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

        return thoughts
            .GroupBy(t => t.def.defName)
            .ToDictionary(g => g.First(), g => g.Sum(t => t.MoodOffset()));
    }

    public static HashSet<Hediff> GetHediffs(Pawn pawn)
    {
        return pawn.health.hediffSet.hediffs.Where(hediff => hediff.Visible).ToHashSet();
    }

    // Compare and return new thought that has the highest overall effect
    public static KeyValuePair<Thought, float> GetNewThought(Pawn pawn)
    {
        var newThoughts = GetThoughts(pawn).OrderByDescending(kvp => Math.Abs(kvp.Value));

        return newThoughts.FirstOrDefault(kvp =>
            !Cache.Get(pawn).Thoughts.TryGetValue(kvp.Key.def.defName, out float moodOffset) ||
            Math.Abs(kvp.Value) > Math.Abs(moodOffset));
    }

    public static string GetNewThoughtLabel(Thought thought)
    {
        if (thought == null) return null;

        // var offset = thought.MoodOffset();
        // var attitude = offset > 0 ? "up" : offset < 0 ? "down" : "";

        return $"thought: {thought.LabelCap}({thought.Description})";
    }
        
    public static bool IsPawnInDanger(Pawn pawn)
    {
        if (pawn.Dead) return true;
        if (pawn.Downed) return true;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
        if (pawn.InMentalState) return true;
        if (pawn.IsBurning()) return true;
        if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
        if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;
        if (IsPawnInCombat(pawn)) return true;
        if (pawn.CurJobDef == JobDefOf.Flee) return true;

        // Check severe Hediffs
        foreach (var h in pawn.health.hediffSet.hediffs)
        {
            if (h.Visible && (h.CurStage?.lifeThreatening == true || 
                              h.def.lethalSeverity > 0 && h.Severity > h.def.lethalSeverity * 0.8f))
                return true;
        }

        return false;
    }
        
    public static bool IsPawnInCombat(Pawn pawn)
    {
        if (pawn == null) return false;

        // 1. MindState target
        if (pawn.mindState.enemyTarget != null) return true;

        // 2. Stance busy with attack verb
        if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null)
            return true;

        return false;
    }

    public static string GetRole(Pawn pawn)
    {
        if (pawn.IsPrisoner) return "Prisoner";
        if (pawn.IsSlave) return "Slave";
        if (IsVisitor(pawn)) return "Visitor";
        if (IsInvader(pawn)) return "Invader";
        if (pawn.IsFreeColonist) return "Colonist";
        return "Unknown";
    }
        
    public static bool IsVisitor(Pawn pawn)
    {
        return pawn.Faction != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer);
    }

    public static bool IsInvader(Pawn pawn)
    {
        return pawn.HostileTo(Faction.OfPlayer);
    }

    public static string GetPawnName(Pawn pawn, Pawn nearbyPawn)
    {
        string shortName = nearbyPawn.LabelShort;

        // Same group: prisoners, slaves, or same faction
        if ((pawn.IsPrisoner && nearbyPawn.IsPrisoner) ||
            (pawn.IsSlave && nearbyPawn.IsSlave) ||
            (pawn.Faction != null && pawn.Faction == nearbyPawn.Faction))
        {
            return shortName;
        }

        // Master relationships
        if ((pawn.IsPrisoner || pawn.IsSlave) && nearbyPawn.Faction == Faction.OfPlayer)
        {
            return $"{shortName}(master)";
        }

        // Prisoner or slave labels
        if (nearbyPawn.IsPrisoner) return $"{shortName}(prisoner)";
        if (nearbyPawn.IsSlave) return $"{shortName}(slave)";

        // Faction-based labels
        if (nearbyPawn.Faction != null)
        {
            if (pawn.Faction != null && pawn.Faction.HostileTo(nearbyPawn.Faction))
            {
                return $"{shortName}(enemy)";
            }

            if (nearbyPawn.Faction == Faction.OfPlayer)
            {
                return $"{shortName}(colonist)";
            }

            return $"{shortName}(visitor)";
        }

        // Fallback
        return shortName;
    }

    public static string GetPawnStatusFull(Pawn pawn, List<Pawn> nearbyPawns)
    {
        bool isInDanger = false;
            
        List<string> parts = new List<string>();
            
        // --- 1. Add status ---
        parts.Add($"Currently: {GetStatus(pawn)}");

        if (IsPawnInDanger(pawn))
        {
            parts.Add("be dramatic");
            isInDanger = true;
        }
            
        // --- 2. Nearby pawns ---
        if (nearbyPawns.Any())
        {
            // Collect critical statuses of nearby pawns
            var nearbyNotableStatuses = nearbyPawns
                .Where(nearbyPawn => nearbyPawn.Faction == pawn.Faction && IsPawnInDanger(nearbyPawn))
                .Take(2)
                .Select(other => $"{other.LabelShort} in {GetStatus(other).Replace("\n", "; ")}")
                .ToList();

            if (nearbyNotableStatuses.Any())
            {
                parts.Add("People in condition nearby: " + string.Join("; ", nearbyNotableStatuses));
                isInDanger = true;
            }

            // Names of nearby pawns
            var nearbyNames = nearbyPawns
                .Select(nearbyPawn => 
                {
                    string name = GetPawnName(pawn, nearbyPawn);
                    if (Cache.Get(nearbyPawn) is PawnState pawnState)
                    {
                        name = $"{name} ({GetStatus(nearbyPawn).StripTags()})";
                    }
                    return name;
                })
                .ToList();

            string nearbyText = nearbyNames.Count == 0 ? "none"
                : nearbyNames.Count > 3
                    ? string.Join(", ", nearbyNames.Take(3)) + ", and others"
                    : string.Join(", ", nearbyNames);

            parts.Add($"Nearby: {nearbyText}");
        }
        else
        {
            parts.Add("Nearby people: none");
        }

        if (IsInvader(pawn))
        {
            parts.Add("invading user colony");
            return string.Join("\n", parts);
        }

        // --- 3. Enemy proximity / combat info ---
        Pawn nearestHostile = HostilePawnNearBy(pawn);
        if (nearestHostile != null)
        {
            float distance = pawn.Position.DistanceTo(nearestHostile.Position);

            if (distance <= 10f)
                parts.Add("Threat: Engaging in battle!");
            else if (distance <= 20f)
                parts.Add("Threat: Hostiles are dangerously close!");
            else
                parts.Add("Alert: hostiles in the area");
            isInDanger = true;
        }
            
        if (!isInDanger)
            parts.Add(Constant.Prompt);

        return string.Join("\n", parts);
    }
        
    public static Pawn HostilePawnNearBy(Pawn pawn)
    {
        // Get all targets on the map that are hostile to the player faction
        var hostileTargets = pawn.Map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer);

        Pawn closestPawn = null;
        float closestDistSq = float.MaxValue;

        foreach (IAttackTarget target in hostileTargets)
        {
            // First, check if the target is considered an active threat by the game's logic
            if (GenHostility.IsActiveThreatTo(target, Faction.OfPlayer))
            {
                if (target.Thing is Pawn threatPawn)
                {
                    Lord lord = threatPawn.GetLord();
                        
                    // === 1. EXCLUDE TACTICALLY RETREATING PAWNS ===
                    if (lord != null && (lord.CurLordToil is LordToil_ExitMapFighting || lord.CurLordToil is LordToil_ExitMap))
                    {
                        continue;
                    }

                    // === 2. EXCLUDE ROAMING MECH CLUSTER PAWNS ===
                    if (threatPawn.RaceProps.IsMechanoid && lord != null && lord.CurLordToil is LordToil_DefendPoint)
                    {
                        continue;
                    }

                    // === 3. CALCULATE DISTANCE FOR VALID THREATS ===
                    float distSq = pawn.Position.DistanceToSquared(threatPawn.Position);

                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestPawn = threatPawn;
                    }
                }
            }
        }

        return closestPawn;
    }
        
    // Using a HashSet for better readability and maintainability.
    private static readonly HashSet<string> ResearchJobDefNames =
    [
        "Research",
        // MOD: Research Reinvented
        "RR_Analyse",
        "RR_AnalyseInPlace",
        "RR_AnalyseTerrain",
        "RR_Research",
        "RR_InterrogatePrisoner",
        "RR_LearnRemotely"
    ];
        
    public static string GetStatus(Pawn pawn)
    {
        pawn.def.hideMainDesc = true;
        string status = pawn.GetInspectString();
        if (ResearchJobDefNames.Contains(pawn.CurJob?.def.defName)) // The job is compared against its defined name.
        {
            ResearchProjectDef project = Find.ResearchManager.GetProject();
            status += $" (Project: {project.label})"; // Adding 'Project:' seems to work better for dialogue generation!
        }
        return status;
    }
}