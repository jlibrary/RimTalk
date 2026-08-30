using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Service;
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
        if (pawn == null) return false;
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
        // Being unable to walk is a condition, not a danger - genuine danger to an immobile
        // pawn is already covered by the hostile/bleeding/pain/burning/hediff checks below.
        if (pawn.InMentalState && includeMentalState) return true;
        if (pawn.IsBurning()) return true;
        if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
        if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;
        if (pawn.CurJobDef == JobDefOf.Flee || pawn.CurJobDef == JobDefOf.FleeAndCower) return true;
        if (pawn.IsInCombat()) return true;

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

        // enemyTarget is sticky - RimWorld doesn't reliably clear it when a fight ends and it
        // survives save/reload, so a raw null check marks anyone who's ever fought as permanently
        // in combat. Only count it while the target is still a live threat.
        var target = pawn.mindState?.enemyTarget;
        if (IsLiveThreat(pawn, target)) return true;

        if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null)
            return true;

        Pawn hostilePawn = pawn.GetHostilePawnNearBy();
        return hostilePawn != null && pawn.Position.DistanceTo(hostilePawn.Position) <= 20f;
    }

    /// <summary>A remembered target only counts while it is still there and still a threat.</summary>
    private static bool IsLiveThreat(Pawn pawn, Thing target)
    {
        if (target == null || target.Destroyed || !target.Spawned) return false;
        if (target.Map != pawn.Map) return false;
        if (target is Pawn tp && (tp.Dead || tp.Downed)) return false;
        return pawn.Position.DistanceTo(target.Position) <= 30f;
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
        return pawn?.Faction != null && Faction.OfPlayer != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner;
    }

    public static string GetTitle(this Pawn pawn)
    {
        if (pawn == null) return "";

        RoyalTitleDef titleDef = null;
        Faction titleFaction = null;
        if (pawn.royalty != null)
        {
            var mostSenior = pawn.royalty.MostSeniorTitle;
            if (mostSenior != null)
            {
                titleDef = mostSenior.def;
                titleFaction = mostSenior.faction;
            }

            if (titleDef == null && Faction.OfEmpire != null)
            {
                titleDef = pawn.royalty.GetCurrentTitle(Faction.OfEmpire);
                titleFaction = titleDef != null ? Faction.OfEmpire : null;
            }

            if (titleDef == null && Faction.OfPlayer != null && pawn.Faction != null)
            {
                titleDef = pawn.royalty.GetCurrentTitle(pawn.Faction);
                titleFaction = titleDef != null ? pawn.Faction : null;
            }
        }

        if (titleDef != null)
        {
            var titleLabel = titleDef.GetLabelFor(pawn);
            return titleFaction != null ? $"{titleFaction.Name}: {titleLabel}" : titleLabel;
        }

        return pawn.story?.title ?? "";
    }

    public static bool IsEnemy(this Pawn pawn)
    {
        return pawn?.Faction != null && Faction.OfPlayer != null && pawn.Faction != Faction.OfPlayer && pawn.HostileTo(Faction.OfPlayer) && !pawn.IsPrisoner;
    }

    public static bool IsBaby(this Pawn pawn)
    {
        return pawn.ageTracker?.CurLifeStage?.developmentalStage < DevelopmentalStage.Child;
    }

    public static (string, bool) GetPawnStatusFull(this Pawn pawn, List<Pawn> nearbyPawns)
    {
        return GetPawnStatusFull(pawn, nearbyPawns, false);
    }

    public static (string, bool) GetPawnStatusFull(this Pawn pawn, List<Pawn> nearbyPawns, bool isAnnouncement)
    {
        var settings = Settings.Get();
        if (pawn == null) return (null, false);
        if (pawn.IsPlayer() && !isAnnouncement) return (settings.PlayerName, false);

        bool isInDanger = false;
        var lines = new List<string>();
        var relevantPawns = CollectRelevantPawns(pawn, nearbyPawns);
        bool useOptimization = settings.Context.EnableContextOptimization;

        if (pawn.IsPlayer())
        {
            lines.Add(settings.PlayerName);
        }
        else
        {
            string pawnLabel = GetPawnLabel(pawn, relevantPawns, useOptimization);
            string pawnActivity = GetPawnActivity(pawn, relevantPawns, useOptimization);
            if (pawn.IsInDanger())
            {
                lines.Add($"{pawnLabel} {pawnActivity} [IN DANGER]");
                isInDanger = true;
            }
            else
            {
                lines.Add($"{pawnLabel} {pawnActivity}");
            }
        }

        if (nearbyPawns != null && nearbyPawns.Any())
        {
            int maxCount = isAnnouncement
                ? Math.Max(settings.Context.MaxPawnContextCount, nearbyPawns.Count)
                : settings.Context.MaxPawnContextCount;

            string nearbyList = GetCombinedNearbyList(pawn, nearbyPawns, relevantPawns,
                useOptimization, maxCount, ref isInDanger);

            lines.Add("Nearby: " + nearbyList);
        }
        else
        {
            lines.Add("Nearby people: none");
        }

        AddContextualInfo(pawn.IsPlayer() ? nearbyPawns?.FirstOrDefault(p => !p.IsPlayer()) ?? pawn : pawn, lines, ref isInDanger);
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
            if (p == null || p.IsPlayer()) continue;

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
                if (talkRequest != null && !p.HostileTo(mainPawn))
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
            if (pawn.HasActiveHostiles())
                lines.Add("You are away from colony, attacking to capture enemy settlement");
            else
            {
                lines.Add("You secured/captured enemy settlement; destroying remaining enemy assets or gathering loot");
                return;
            }
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
                if (pawn.HasActiveHostiles())
                    lines.Add("Fighting to protect your home from being captured");
                else
                {
                    lines.Add("Defended settlement; secured victory over invaders (destroying remnants/loot)");
                    return;
                }
            }
        }

        // Check for hostiles and threat scale
        var (threatCount, nearestHostile, threatSummary, dangerAssessment, isSevere) = pawn.GetHostileThreatInfo();
        if (nearestHostile != null)
        {
            float distance = pawn.Position.DistanceTo(nearestHostile.Position);
            string scaleLabel = threatCount == 1 ? "1 Hostile" : $"{threatCount} Hostiles";

            if (distance <= 10f)
            {
                lines.Add($"Combat ({dangerAssessment}): Engaging in battle with {GetThreatLabel(nearestHostile)}!");
                isInDanger = true;
            }
            else if (distance <= 20f)
            {
                lines.Add($"Threat ({dangerAssessment} - {scaleLabel}): {threatSummary} dangerously close!");
                if (isSevere) isInDanger = true;
            }
            else
            {
                lines.Add($"Alert ({dangerAssessment} - {scaleLabel}): {threatSummary} in the area (distant, not engaged yet)");
            }
        }
    }

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

            string label = GetThreatLabel(threatPawn);
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

        string summary = string.Join(", ", threatCounts.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key));
        return (totalCount, closestPawn, summary, dangerAssessment, isSevere);
    }

    public static Pawn GetHostilePawnNearBy(this Pawn pawn)
    {
        return pawn.GetHostileThreatInfo().nearest;
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

    private static string GetThreatLabel(Pawn threat)
    {
        if (threat == null) return "unknown threat";

        if (threat.RaceProps.Humanlike)
        {
            if (ModsConfig.BiotechActive && threat.genes?.Xenotype != null)
                return threat.genes.XenotypeLabel;

            return threat.def.LabelCap.RawText;
        }

        return threat.KindLabel;
    }

    private static readonly HashSet<string> ResearchJobDefNames =
    [
        "Research",
        "RR_Analyse",
        "RR_AnalyseInPlace",
        "RR_AnalyseTerrain",
        "RR_Research",
        "RR_InterrogatePrisoner",
        "RR_LearnRemotely"
    ];

    private static readonly string[] MovementJobPatterns = ["Goto", "Flee", "Wait", "Wander"];

    internal static string GetActivity(this Pawn pawn)
    {
        if (pawn == null) return null;

        if (pawn.InMentalState)
            return pawn.MentalState?.InspectLine;

        if (pawn.CurJobDef is null)
            return null;

        var targetThing = pawn.IsAttacking() ? pawn.TargetCurrentlyAimingAt.Thing : null;
        if (targetThing != null)
        {
            string targetLabel = Describer.StripConditionSuffix(targetThing.LabelShortCap);
            if (targetThing.Faction != null && targetThing.Faction != pawn.Faction)
            {
                bool isTargetPlayer = targetThing.Faction == Faction.OfPlayer;
                string ownerPrefix = isTargetPlayer ? "invader's" : $"{targetThing.Faction.Name}'s";
                return $"Attacking {ownerPrefix} {targetLabel}";
            }
            return $"Attacking {targetLabel}";
        }

        var lord = Describer.StripConditionSuffix(pawn.GetLord()?.LordJob?.GetReport(pawn));
        var job = Describer.StripConditionSuffix(pawn.jobs?.curDriver?.GetReport());

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
            // One flowing phrase, not a disconnected "(traveling to)" tag stapled onto a gerund.
            activity = $"traveling to {activity}";
        }

        return activity;
    }

    private static string AppendResearchProgress(string activity)
    {
        ResearchProjectDef project = Find.ResearchManager.GetProject();
        if (project == null) return activity;

        float progress = Find.ResearchManager.GetProgress(project);
        float percentage = (progress / project.baseCost) * 100f;
        return $"{activity} (Project: {project.label} - {Describer.Progress(percentage)})";
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

        if (mapFaction == pawn.Faction || (map.IsPlayerHome && Faction.OfPlayer != null && pawn.Faction == Faction.OfPlayer))
            return MapRole.Defending;

        if (pawn.Faction == null || mapFaction == null)
            return MapRole.Visiting;

        if (pawn.Faction.HostileTo(mapFaction))
            return MapRole.Invading;

        return MapRole.Visiting;
    }

    public static string GetPrisonerSlaveStatus(this Pawn pawn, PromptService.InfoLevel infoLevel = PromptService.InfoLevel.Normal)
    {
        if (pawn == null) return null;

        var lines = new List<string>();
        bool showRaw = infoLevel == PromptService.InfoLevel.Full;

        if (pawn.IsPrisoner)
        {
            float resistance = pawn.guest.resistance;
            lines.Add(showRaw
                ? $"Resistance: {resistance:0.0} ({Describer.Resistance(resistance)})"
                : $"Resistance: {Describer.Resistance(resistance)}");

            float will = pawn.guest.will;
            lines.Add(showRaw
                ? $"Will: {will:0.0} ({Describer.Will(will)})"
                : $"Will: {Describer.Will(will)}");
        }
        else if (pawn.IsSlave)
        {
            var suppressionNeed = pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppressionNeed != null)
            {
                float suppression = suppressionNeed.CurLevelPercentage * 100f;
                lines.Add(showRaw
                    ? $"Suppression: {suppression:0.0}% ({Describer.Suppression(suppression)})"
                    : $"Suppression: {Describer.Suppression(suppression)}");
            }
        }

        return lines.Any() ? string.Join("\n", lines) : null;
    }

    public static bool IsPrisonBreaking(this Pawn pawn)
    {
        if (pawn == null || pawn.IsPlayer()) return false;
        return PrisonBreakUtility.IsPrisonBreaking(pawn);
    }

    public static bool IsPlayer(this Pawn pawn)
    {
        // Cache.GetPlayer() is null until the invisible player pawn is created, so without
        // the null guard every null pawn would count as "the player".
        return pawn != null && pawn == Cache.GetPlayer();
    }

    public static bool HasVocalLink(this Pawn pawn)
    {
        return Settings.Get().AllowNonHumanToTalk &&
               pawn?.health?.hediffSet != null &&
               pawn.health.hediffSet.HasHediff(Constant.VocalLinkDef);
    }
}
