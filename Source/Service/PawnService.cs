using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

public static class PawnService
{
    public static bool IsTalkEligible(this Pawn pawn)
    {
        if (pawn.DestroyedOrNull() || !pawn.Spawned || pawn.Dead)
            return false;

        if (!pawn.RaceProps.Humanlike)
            return false;

        if (pawn.RaceProps.intelligence < Intelligence.Humanlike)
            return false;

        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            return false;

        if (pawn.skills?.GetSkill(SkillDefOf.Social) == null)
            return false;

        RimTalkSettings settings = Settings.Get();
        return pawn.IsFreeColonist ||
               (settings.AllowSlavesToTalk && pawn.IsSlave) ||
               (settings.AllowPrisonersToTalk && pawn.IsPrisoner) ||
               (settings.AllowOtherFactionsToTalk && pawn.IsVisitor()) ||
               (settings.AllowEnemiesToTalk && pawn.IsEnemy());
    }

    public static HashSet<Hediff> GetHediffs(this Pawn pawn)
    {
        return pawn?.health.hediffSet.hediffs.Where(hediff => hediff.Visible).ToHashSet();
    }

    public static bool IsInDanger(this Pawn pawn, bool includeMentalState = false)
    {
        if (pawn == null) return false;
        if (pawn.Dead) return true;
        if (pawn.Downed) return true;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
        if (pawn.InMentalState && includeMentalState) return true;
        if (pawn.IsBurning()) return true;
        if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
        if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;
        if (pawn.IsInCombat()) return true;
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

    public static bool IsInCombat(this Pawn pawn)
    {
        if (pawn == null) return false;

        // 1. MindState target
        if (pawn.mindState.enemyTarget != null) return true;

        // 2. Stance busy with attack verb
        if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null)
            return true;

        Pawn hostilePawn = pawn.GetHostilePawnNearBy();
        return hostilePawn != null && pawn.Position.DistanceTo(hostilePawn.Position) <= 20f;
    }

    public static string GetRole(this Pawn pawn, bool includeFaction = false)
    {
        if (pawn == null) return null;
        if (pawn.IsPrisoner) return "Prisoner";
        if (pawn.IsSlave) return "Slave";
        if (pawn.IsEnemy())
            if (pawn.GetMapRole() == MapRole.Invading)
                return includeFaction && pawn.Faction != null ? $"Enemy Group({pawn.Faction.Name})" : "Enemy";
            else
                return "Enemy Defender";
        if (pawn.IsVisitor())
            return includeFaction && pawn.Faction != null ? $"Visitor Group({pawn.Faction.Name})" : "Visitor";
        if (pawn.IsQuestLodger()) return "Lodger";
        if (pawn.IsFreeColonist) return pawn.GetMapRole() == MapRole.Invading ? "Invader" : "Colonist";
        return "Unknown";
    }

    public static bool IsVisitor(this Pawn pawn)
    {
        return pawn?.Faction != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer);
    }

    public static bool IsEnemy(this Pawn pawn)
    {
        return pawn != null && pawn.HostileTo(Faction.OfPlayer);
    }

    public static (string, bool) GetPawnStatusFull(this Pawn pawn, List<Pawn> nearbyPawns)
    {
        if (pawn == null) return (null, false);

        bool isInDanger = false;

        List<string> parts = new List<string>();

        // --- 1. Add status ---
        parts.Add($"{pawn.LabelShort} ({pawn.GetActivity()})");

        if (IsInDanger(pawn))
        {
            isInDanger = true;
        }

        // --- 2. Nearby pawns ---
        if (nearbyPawns.Any())
        {
            // Collect critical statuses of nearby pawns
            var nearbyNotableStatuses = nearbyPawns
                .Where(nearbyPawn => nearbyPawn.Faction == pawn.Faction && nearbyPawn.IsInDanger(true))
                .Take(2)
                .Select(other => $"{other.LabelShort} in {other.GetActivity().Replace("\n", "; ")}")
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
                    string name = $"{nearbyPawn.LabelShort}({nearbyPawn.GetRole()})";
                    if (Cache.Get(nearbyPawn) is not null)
                    {
                        name = $"{name} ({nearbyPawn.GetActivity().StripTags()})";
                    }

                    return name;
                })
                .ToList();

            string nearbyText = nearbyNames.Count == 0
                ? "none"
                : nearbyNames.Count > 3
                    ? string.Join(", ", nearbyNames.Take(3)) + ", and others"
                    : string.Join(", ", nearbyNames);

            parts.Add($"Nearby: {nearbyText}");
        }
        else
        {
            parts.Add("Nearby people: none");
        }

        if (pawn.IsVisitor())
        {
            parts.Add("Visiting user colony");
        }

        if (pawn.IsFreeColonist && pawn.GetMapRole() == MapRole.Invading)
        {
            parts.Add("You are away from colony, attacking to capture enemy settlement");
        }
        else if (pawn.IsEnemy())
        {
            if (pawn.GetMapRole() == MapRole.Invading)
            {
                if (pawn.GetLord()?.LordJob is LordJob_StageThenAttack || pawn.GetLord()?.LordJob is LordJob_Siege)
                {
                    parts.Add("waiting to invade user colony");
                }
                else
                {
                    parts.Add("invading user colony");
                }
            }
            else
            {
                parts.Add("Fighting to protect your home from being captured");
            }

            return (string.Join("\n", parts), isInDanger);
        }

        // --- 3. Enemy proximity / combat info ---
        Pawn nearestHostile = GetHostilePawnNearBy(pawn);
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

    public static Pawn GetHostilePawnNearBy(this Pawn pawn)
    {
        if (pawn == null) return null;

        // Get all targets on the map that are hostile to the player faction
        var hostileTargets = pawn.Map.attackTargetsCache?.TargetsHostileToFaction(pawn.Faction);

        Pawn closestPawn = null;
        float closestDistSq = float.MaxValue;

        if (hostileTargets == null) return null;

        foreach (var target in hostileTargets.Where(target => GenHostility.IsActiveThreatTo(target, pawn.Faction)))
        {
            if (target.Thing is not Pawn threatPawn) continue;
            if (threatPawn.Downed) continue;
            if (pawn.IsPrisoner)
            {
                // Prevent prisoners from recognizing host faction or other prisoner from hostile faction as a threat
                if (threatPawn.Faction == pawn.HostFaction || threatPawn.IsPrisoner) continue;
            }

            Lord lord = threatPawn.GetLord();

            // === 1. EXCLUDE TACTICALLY RETREATING PAWNS ===
            if (lord != null && (lord.CurLordToil is LordToil_ExitMapFighting ||
                                 lord.CurLordToil is LordToil_ExitMap))
            {
                continue;
            }

            // === 2. EXCLUDE ROAMING MECH CLUSTER PAWNS ===
            if (threatPawn.RaceProps.IsMechanoid && lord != null &&
                lord.CurLordToil is LordToil_DefendPoint)
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

    private static string GetActivity(this Pawn pawn)
    {
        if (pawn == null) return null;
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

    public static MapRole GetMapRole(this Pawn pawn)
    {
        if (pawn?.Map == null)
            return MapRole.None;

        Map map = pawn.Map;
        Faction mapFaction = map.ParentFaction;

        if (mapFaction == pawn.Faction || (map.IsPlayerHome && pawn.Faction == Faction.OfPlayer))
            return MapRole.Defending; // player colonist

        if (pawn.Faction.HostileTo(mapFaction))
            return MapRole.Invading;

        return MapRole.Visiting; // friendly trader or visitor
    }

    public static string GetPrisonerSlaveStatus(this Pawn pawn)
    {
        if (pawn == null) return null;

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