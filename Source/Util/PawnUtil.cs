using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Util;

public static class PawnUtil
{
    public static bool IsTalkEligible(this Pawn pawn)
    {
        if (pawn.IsPlayer()) return true;
        if (pawn.HasVocalLink()) return true;
        if (pawn.DestroyedOrNull() || !pawn.Spawned || pawn.Dead) return false;
        if (!pawn.RaceProps.Humanlike) return false;
        if (pawn.RaceProps.intelligence < Intelligence.Humanlike) return false;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) return false;
        if (pawn.skills?.GetSkill(SkillDefOf.Social) == null) return false;

        RimTalkSettings settings = Settings.Get();
        if (!settings.AllowBabiesToTalk && pawn.IsBaby()) return false;

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
        if (pawn == null || pawn.IsPlayer()) return false;
        if (pawn.Dead) return true;
        if (pawn.Downed) return true;
        if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
        if (pawn.InMentalState && includeMentalState) return true;
        if (pawn.IsBurning()) return true;
        if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
        if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;
        if (pawn.IsInCombat()) return true;
        if (pawn.CurJobDef == JobDefOf.Flee || pawn.CurJobDef == JobDefOf.FleeAndCower) return true;

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

        if (pawn.mindState.enemyTarget != null) return true;

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
        {
            if (pawn.GetMapRole() == MapRole.Invading)
                return includeFaction && pawn.Faction != null ? $"Enemy Group({pawn.Faction.Name})" : "Enemy";
            return "Enemy Defender";
        }

        if (pawn.IsVisitor())
            return includeFaction && pawn.Faction != null ? $"Visitor Group({pawn.Faction.Name})" : "Visitor";
        if (pawn.IsQuestLodger()) return "Lodger";
        if (pawn.IsFreeColonist) return pawn.GetMapRole() == MapRole.Invading ? "Invader" : "Colonist";
        return null;
    }

    public static bool IsVisitor(this Pawn pawn)
    {
        return pawn?.Faction != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner;
    }

    public static bool IsEnemy(this Pawn pawn)
    {
        return pawn != null && pawn.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner;
    }

    public static bool IsBaby(this Pawn pawn)
    {
        return pawn.ageTracker?.CurLifeStage?.developmentalStage < DevelopmentalStage.Child;
    }

    public static (string, bool) GetPawnStatusFull(this Pawn pawn, List<Pawn> nearbyPawns)
    {
        var settings = Settings.Get();

        if (pawn == null)
            return (null, false);

        if (pawn.IsPlayer())
            return (settings.PlayerName, false);

        bool isInDanger = false;
        var lines = new List<string>();

        // 1. Collect Context
        var relevantPawns = CollectRelevantPawns(pawn, nearbyPawns);
        bool useOptimization = settings.Context.EnableContextOptimization;

        // 2. Main Pawn Line
        string pawnLabel = GetPawnLabel(pawn, relevantPawns, useOptimization);
        string pawnActivity = GetPawnActivity(pawn, relevantPawns, useOptimization);

        // Check if main pawn is in danger (Panic/Combat/Health)
        if (pawn.IsInDanger())
        {
            lines.Add($"{pawnLabel} {pawnActivity} [IN DANGER]");
            isInDanger = true;
        }
        else
        {
            lines.Add($"{pawnLabel} {pawnActivity}");
        }

        // 3. Combined Nearby List
        if (nearbyPawns != null && nearbyPawns.Any())
        {
            // Update ref situationIsCritical inside this method
            string nearbyList = GetCombinedNearbyList(pawn, nearbyPawns, relevantPawns,
                useOptimization, settings.Context.MaxPawnContextCount, ref isInDanger);

            lines.Add("Nearby: " + nearbyList);
        }
        else
        {
            lines.Add("Nearby people: none");
        }

        // 4. Global Contextual Info
        AddContextualInfo(pawn, lines, ref isInDanger);

        return (string.Join("\n", lines), isInDanger);
    }

    private static string GetCombinedNearbyList(Pawn mainPawn, List<Pawn> nearbyPawns,
        HashSet<Pawn> relevantPawns, bool useOptimization, int maxCount, ref bool situationIsCritical)
    {
        if (nearbyPawns == null || !nearbyPawns.Any())
            return "none";

        var descriptions = new List<string>();
        bool localDangerFound = false;

        var pawnsToScan = nearbyPawns.Take(maxCount);

        foreach (var p in pawnsToScan)
        {
            string label = GetPawnLabel(p, relevantPawns, useOptimization);
            string extraStatus = "";

            if (p.IsInDanger(true))
            {
                if (p.Faction == mainPawn.Faction)
                    localDangerFound = true;

                extraStatus = " [!]";
            }

            string entry;
            var pawnState = Cache.Get(p);
            if (pawnState != null)
            {
                string activity = GetPawnActivity(p, relevantPawns, useOptimization);
                string talkRequestStr = "";
                var talkRequest = pawnState.GetNextTalkRequest();
                if (talkRequest != null)
                {
                    pawnState.MarkRequestSpoken(talkRequest);
                    talkRequestStr = $" - {talkRequest.Prompt}";
                }
                entry = $"{label} {activity.StripTags()}{extraStatus}{talkRequestStr}";
            }
            else
            {
                entry = $"{label}{extraStatus}";
            }

            descriptions.Add(entry);
        }

        if (localDangerFound)
            situationIsCritical = true;

        string result = "\n- " + string.Join("\n- ", descriptions);

        return result;
    }

    private static HashSet<Pawn> CollectRelevantPawns(Pawn mainPawn, List<Pawn> nearbyPawns)
    {
        var relevantPawns = new HashSet<Pawn> { mainPawn };

        if (mainPawn.CurJob != null)
            AddJobTargetsToRelevantPawns(mainPawn.CurJob, relevantPawns);

        if (nearbyPawns != null)
        {
            relevantPawns.UnionWith(nearbyPawns);

            foreach (var nearby in nearbyPawns.Where(p => p.CurJob != null))
                AddJobTargetsToRelevantPawns(nearby.CurJob, relevantPawns);
        }

        return relevantPawns;
    }

    private static string GetPawnLabel(Pawn pawn, HashSet<Pawn> relevantPawns, bool useOptimization)
    {
        if (useOptimization)
            return pawn.LabelShort;

        return relevantPawns.Contains(pawn)
            ? ContextHelper.GetDecoratedName(pawn)
            : pawn.LabelShort;
    }

    private static string GetPawnActivity(Pawn pawn, HashSet<Pawn> relevantPawns, bool useOptimization)
    {
        string activity = pawn.GetActivity();

        if (useOptimization || string.IsNullOrEmpty(activity))
            return activity;

        return DecorateText(activity, relevantPawns);
    }

    private static void AddContextualInfo(Pawn pawn, List<string> lines, ref bool isInDanger)
    {
        if (pawn.IsVisitor())
        {
            lines.Add("Visiting user colony");
            return;
        }

        if (pawn.IsFreeColonist && pawn.GetMapRole() == MapRole.Invading)
        {
            lines.Add("You are away from colony, attacking to capture enemy settlement");
            return;
        }

        if (pawn.IsEnemy())
        {
            if (pawn.GetMapRole() == MapRole.Invading)
            {
                var lord = pawn.GetLord()?.LordJob;
                if (lord is LordJob_StageThenAttack || lord is LordJob_Siege)
                    lines.Add("waiting to invade user colony");
                else
                    lines.Add("invading user colony");
            }
            else
            {
                lines.Add("Fighting to protect your home from being captured");
            }

            return;
        }

        // Check for nearby hostiles
        Pawn nearestHostile = pawn.GetHostilePawnNearBy();
        if (nearestHostile != null)
        {
            float distance = pawn.Position.DistanceTo(nearestHostile.Position);

            if (distance <= 10f)
                lines.Add("Threat: Engaging in battle!");
            else if (distance <= 20f)
                lines.Add("Threat: Hostiles are dangerously close!");
            else
                lines.Add("Alert: hostiles in the area");

            isInDanger = true;
        }
    }

    /// <summary>
    /// Decorates text by replacing pawn names with their decorated versions
    /// </summary>
    private static string DecorateText(string text, HashSet<Pawn> relevantPawns)
    {
        if (string.IsNullOrEmpty(text) || relevantPawns == null || !relevantPawns.Any())
            return text;

        // Build replacement map
        var replacements = relevantPawns
            .Select(p => new { Key = p.LabelShort, Value = ContextHelper.GetDecoratedName(p) })
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .OrderByDescending(x => x.Key.Length) // Longer names first to avoid partial matches
            .ToList();

        // Apply replacements
        return replacements.Aggregate(text, (current, replacement) =>
            current.Replace(replacement.Key, replacement.Value));
    }

    public static Pawn GetHostilePawnNearBy(this Pawn pawn)
    {
        if (pawn?.Map == null) return null;

        Faction referenceFaction = GetReferenceFaction(pawn);
        if (referenceFaction == null) return null;

        var hostileTargets = pawn.Map.attackTargetsCache?.TargetsHostileToFaction(referenceFaction);
        if (hostileTargets == null) return null;

        return FindClosestValidThreat(pawn, referenceFaction, hostileTargets);
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

    private static Pawn FindClosestValidThreat(Pawn pawn, Faction referenceFaction,
        IEnumerable<IAttackTarget> hostileTargets)
    {
        Pawn closestPawn = null;
        float closestDistSq = float.MaxValue;

        foreach (var target in hostileTargets)
        {
            if (!GenHostility.IsActiveThreatTo(target, referenceFaction))
                continue;

            if (target.Thing is not Pawn threatPawn || threatPawn.Downed)
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

    private static bool IsValidThreat(Pawn observer, Pawn threat)
    {
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

    private static readonly HashSet<string> ResearchJobDefNames = new()
    {
        "Research",
        "RR_Analyse",
        "RR_AnalyseInPlace",
        "RR_AnalyseTerrain",
        "RR_Research",
        "RR_InterrogatePrisoner",
        "RR_LearnRemotely"
    };

    private static readonly string[] MovementJobPatterns = { "Goto", "Flee", "Wait", "Wander" };

    internal static string GetActivity(this Pawn pawn)
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

        string activity = lord == null ? job :
            job == null ? lord :
            $"{lord} ({job})";

        if (ResearchJobDefNames.Contains(pawn.CurJob?.def.defName))
        {
            activity = AppendResearchProgress(activity);
        }

        bool Near(LocalTargetInfo t) => t.IsValid && pawn.Position.InHorDistOf(t.Cell, 5f);

        if (pawn.pather?.Moving == true
            && pawn.CurJob != null
            && !Near(pawn.CurJob.targetA)
            && !Near(pawn.CurJob.targetB)
            && !Near(pawn.CurJob.targetC)
            && !MovementJobPatterns.Any(p => pawn.CurJob.def.defName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) 
        {
            activity = $"(traveling to) {activity}";
        }

        return activity;
    }

    private static string AppendResearchProgress(string activity)
    {
        ResearchProjectDef project = Find.ResearchManager.GetProject();
        if (project == null) return activity;

        float progress = Find.ResearchManager.GetProgress(project);
        float percentage = (progress / project.baseCost) * 100f;
        return $"{activity} (Project: {project.label} - {percentage:F0}%)";
    }

    private static void AddJobTargetsToRelevantPawns(Job job, HashSet<Pawn> relevantPawns)
    {
        if (job == null) return;

        foreach (TargetIndex index in Enum.GetValues(typeof(TargetIndex)))
        {
            try
            {
                var target = job.GetTarget(index);
                if (target == (LocalTargetInfo)(Thing)null)
                    continue;

                if (target.HasThing && target.Thing is Pawn pawn && relevantPawns.Add(pawn))
                {
                    // Recursively add targets from this pawn's job
                    if (pawn.CurJob != null)
                        AddJobTargetsToRelevantPawns(pawn.CurJob, relevantPawns);
                }
            }
            catch
            {
                // Ignore invalid indices
            }
        }
    }

    public static MapRole GetMapRole(this Pawn pawn)
    {
        if (pawn?.Map == null || pawn.IsPrisonerOfColony)
            return MapRole.None;

        Map map = pawn.Map;
        Faction mapFaction = map.ParentFaction;

        if (mapFaction == pawn.Faction || (map.IsPlayerHome && pawn.Faction == Faction.OfPlayer))
            return MapRole.Defending;

        if (pawn.Faction.HostileTo(mapFaction))
            return MapRole.Invading;

        return MapRole.Visiting;
    }

    public static string GetPrisonerSlaveStatus(this Pawn pawn)
    {
        if (pawn == null) return null;

        var lines = new List<string>();

        if (pawn.IsPrisoner)
        {
            float resistance = pawn.guest.resistance;
            lines.Add($"Resistance: {resistance:0.0} ({Describer.Resistance(resistance)})");

            float will = pawn.guest.will;
            lines.Add($"Will: {will:0.0} ({Describer.Will(will)})");
        }
        else if (pawn.IsSlave)
        {
            var suppressionNeed = pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppressionNeed != null)
            {
                float suppression = suppressionNeed.CurLevelPercentage * 100f;
                lines.Add($"Suppression: {suppression:0.0}% ({Describer.Suppression(suppression)})");
            }
        }

        return lines.Any() ? string.Join("\n", lines) : null;
    }

    public static bool IsPlayer(this Pawn pawn)
    {
        return pawn == Cache.GetPlayer();
    }

    public static bool HasVocalLink(this Pawn pawn)
    {
        return Settings.Get().AllowNonHumanToTalk &&
               pawn.health.hediffSet.HasHediff(Constant.VocalLinkDef);
    }
}