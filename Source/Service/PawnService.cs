using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

public static class PawnService
{
    public static HashSet<Hediff> GetHediffs(Pawn pawn)
    {
        return pawn.health.hediffSet.hediffs.Where(hediff => hediff.Visible).ToHashSet();
    }
        
    public static bool IsPawnInDanger(Pawn pawn, bool includeMentalState = false)
    {
        if (pawn.Dead) return true;
        if (pawn.Downed) return true;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
        if (pawn.InMentalState && includeMentalState) return true;
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
        
        Pawn hostilePawn = HostilePawnNearBy(pawn);
        if (hostilePawn != null && pawn.Position.DistanceTo(hostilePawn.Position) <= 20f)
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
        return pawn?.Faction != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer);
    }

    public static bool IsInvader(Pawn pawn)
    {
        return pawn != null && pawn.HostileTo(Faction.OfPlayer);
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

    public static (string, bool) GetPawnStatusFull(Pawn pawn, List<Pawn> nearbyPawns)
    {
        bool isInDanger = false;
            
        List<string> parts = new List<string>();
            
        // --- 1. Add status ---
        parts.Add($"{pawn.LabelShort} ({GetActivity(pawn)})");

        if (IsPawnInDanger(pawn))
        {
            isInDanger = true;
        }
            
        // --- 2. Nearby pawns ---
        if (nearbyPawns.Any())
        {
            // Collect critical statuses of nearby pawns
            var nearbyNotableStatuses = nearbyPawns
                .Where(nearbyPawn => nearbyPawn.Faction == pawn.Faction && IsPawnInDanger(nearbyPawn, true))
                .Take(2)
                .Select(other => $"{other.LabelShort} in {GetActivity(other).Replace("\n", "; ")}")
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
                        name = $"{name} ({GetActivity(nearbyPawn).StripTags()})";
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

        if (IsVisitor(pawn))
        {
            parts.Add("Visiting user colony");
        }

        if (IsInvader(pawn))
        {
            if (pawn.GetLord()?.LordJob is LordJob_StageThenAttack || pawn.GetLord()?.LordJob is LordJob_Siege)
            {
                parts.Add("waiting to invade user colony");
            }
            else
            {
                parts.Add("invading user colony");
            }
            
            return (string.Join("\n", parts), isInDanger);
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

        return (string.Join("\n", parts), isInDanger);
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
    
    private static string GetActivity(Pawn pawn)
    {
        if (pawn.InMentalState)
            return pawn.MentalState?.InspectLine;

        if (pawn.CurJobDef is null)
            return null;

        var target = pawn.IsAttacking() ? pawn.TargetCurrentlyAimingAt.Thing?.LabelShortCap : null;
        if (target != null)
            return $"Attacking {target}";

        var lord = pawn.GetLord()?.LordJob?.GetReport(pawn);
        var job = pawn.jobs?.curDriver?.GetReport();

        string activity;
        if (lord == null) activity = job;
        else activity = job == null ? lord : $"{lord} ({job})";

        if (ResearchJobDefNames.Contains(pawn.CurJob?.def.defName))
        {
            ResearchProjectDef project = Find.ResearchManager.GetProject();
            if (project != null)
            {
                float progress = Find.ResearchManager.GetProgress(project);
                float percentage = (progress / project.baseCost) * 100f;
                activity += $" (Project: {project.label} - {percentage:F0}%)";
            }
        }

        return activity;
    }
    
    public static string GetPrisonerSlaveStatus(Pawn pawn)
    {
        string result = "";

        if (pawn.IsPrisoner)
        {
            // === Resistance (for recruitment) ===
            float resistance = pawn.guest.resistance;
            result += $"Resistance: {resistance:0.0} ({DescribeResistance(resistance)})\n";

            // === Will (for enslavement) ===
            float will = pawn.guest.will;
            result += $"Will: {will:0.0} ({DescribeWill(will)})\n";
        }

        // === Suppression (slave compliance, if applicable) ===
        else if (pawn.IsSlave)
        {
            var suppressionNeed = pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppressionNeed != null)
            {
                float suppression = suppressionNeed.CurLevelPercentage * 100f;
                result += $"Suppression: {suppression:0.0}% ({DescribeSuppression(suppression)})\n";
            }
        }

        return result.TrimEnd();
    }

    private static string DescribeResistance(float value)
    {
        if (value <= 0f) return "Completely broken, ready to join";
        if (value < 2f) return "Barely resisting, close to giving in";
        if (value < 6f) return "Weakened, but still cautious";
        if (value < 12f) return "Strong-willed, requires effort";
        return "Extremely defiant, will take a long time";
    }

    private static string DescribeWill(float value)
    {
        if (value <= 0f) return "No will left, ready for slavery";
        if (value < 2f) return "Weak-willed, easy to enslave";
        if (value < 6f) return "Moderate will, may resist a little";
        if (value < 12f) return "Strong will, difficult to enslave";
        return "Unyielding, very hard to enslave";
    }

    private static string DescribeSuppression(float value)
    {
        if (value < 20f) return "Openly rebellious, likely to resist or escape";
        if (value < 50f) return "Unstable, may push boundaries";
        if (value < 80f) return "Generally obedient, but watchful";
        return "Completely cowed, unlikely to resist";
    }
}