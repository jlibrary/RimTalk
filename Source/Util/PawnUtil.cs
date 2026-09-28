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

public static class PawnUtil
{
    private const float SeverePainThreshold = 0.4f;
    private const float DangerBleedRateThreshold = 0.3f;
    private const float DangerLethalSeverityThreshold = 0.8f;

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
               (settings.AllowEnemiesToTalk && pawn.IsEnemy()) ||
               (pawn.Faction == null && !pawn.HostileTo(Faction.OfPlayer));
    }

    public static HashSet<Hediff> GetHediffs(this Pawn pawn)
    {
        var result = new HashSet<Hediff>();
        var list = pawn?.health?.hediffSet?.hediffs;
        if (list == null) return result;
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (h.Visible) result.Add(h);
        }
        return result;
    }

    public static bool IsInDanger(this Pawn pawn, bool includeMentalState = false)
    {
        if (pawn == null || pawn.IsPlayer()) return false;
        if (pawn.Dead) return true;
        // Immobility or being downed is a condition, not acute danger - genuine danger (threats, bleeding, pain shock, lethal illness)
        // is evaluated directly by the criteria below.
        if (pawn.InMentalState && includeMentalState) return true;
        if (pawn.IsBurning()) return true;
        if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
        if (pawn.health.hediffSet.BleedRateTotal > DangerBleedRateThreshold) return true;
        if (pawn.CurJobDef == JobDefOf.Flee || pawn.CurJobDef == JobDefOf.FleeAndCower) return true;
        if (pawn.IsInCombat()) return true;
        if (IsLiveThreat(pawn, pawn.mindState?.meleeThreat)) return true;

        // Check severe Hediffs
        foreach (var h in pawn.health.hediffSet.hediffs)
        {
            if (h.Visible && (h.CurStage?.lifeThreatening == true ||
                              h.def.lethalSeverity > 0 && h.Severity > h.def.lethalSeverity * DangerLethalSeverityThreshold))
                return true;
        }

        return false;
    }

    public static bool IsDownedInPain(this Pawn pawn)
    {
        if (pawn == null || !pawn.Downed) return false;
        return pawn.health?.InPainShock == true || (pawn.health?.hediffSet != null && pawn.health.hediffSet.PainTotal >= SeverePainThreshold);
    }

    public static bool IsInCombatOrFire(this Pawn pawn)
    {
        if (pawn == null || pawn.Dead || pawn.Downed || pawn.IsPlayer()) return false;
        return pawn.IsBurning() || pawn.IsInCombat() || pawn.CurJobDef == JobDefOf.Flee || pawn.CurJobDef == JobDefOf.FleeAndCower;
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

        if (pawn.IsBrawlingWithAlly(out _))
            return true;

        Pawn hostilePawn = pawn.GetHostilePawnNearBy();
        return hostilePawn != null && pawn.Position.DistanceToSquared(hostilePawn.Position) <= 400f;
    }

    /// <summary>A remembered target only counts while it is still there and still a threat.</summary>
    private static bool IsLiveThreat(Pawn pawn, Thing target)
    {
        if (target == null || target.Destroyed || !target.Spawned) return false;
        if (target.Map != pawn.Map) return false;
        if (target is Pawn tp && (tp.Dead || tp.Downed)) return false;
        return pawn.Position.DistanceToSquared(target.Position) <= 900f;
    }

    public static string GetRole(this Pawn pawn, bool includeFaction = false)
    {
        if (pawn == null || pawn.IsPlayer()) return null;
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
        if (pawn.Faction == null && !pawn.IsColonist) return "Stranger";
        return null;
    }

    public static bool IsVisitor(this Pawn pawn)
    {
        if (pawn?.Faction == null || pawn.Faction.IsPlayer)
            return false;

        return !pawn.IsPrisoner && !IsHostileToPlayer(pawn);
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
        if (pawn?.Faction == null || pawn.Faction.IsPlayer)
            return false;

        return !pawn.IsPrisoner && IsHostileToPlayer(pawn);
    }

    private static bool IsHostileToPlayer(Pawn pawn)
    {
        if (pawn?.Faction == null || Faction.OfPlayer == null || pawn.Faction == Faction.OfPlayer)
            return false;

        FactionRelation relation = pawn.Faction.RelationWith(Faction.OfPlayer, false);
        if (relation != null)
            return relation.kind == FactionRelationKind.Hostile;

        return pawn.Faction.def.permanentEnemy;
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
        var (fullStatus, _, isInDanger) = GetPawnStatus(pawn, nearbyPawns, isAnnouncement);
        return (fullStatus, isInDanger);
    }

    public static (string fullStatus, string bareStatus, bool isInDanger) GetPawnStatus(this Pawn pawn, List<Pawn> nearbyPawns, bool isAnnouncement)
    {
        var settings = Settings.Get();
        if (pawn == null) return (null, null, false);
        if (pawn.IsPlayer() && !isAnnouncement) return (settings.PlayerName, settings.PlayerName, false);

        bool isInDanger = false;
        var relevantPawns = CollectRelevantPawns(pawn, nearbyPawns);
        bool useOptimization = settings.Context.EnableContextOptimization;

        string fullFirstLine;
        string bareFirstLine;

        if (pawn.IsPlayer())
        {
            fullFirstLine = settings.PlayerName;
            bareFirstLine = settings.PlayerName;
        }
        else
        {
            string pawnLabel = GetPawnLabel(pawn, relevantPawns, useOptimization);
            string pawnActivity = GetPawnActivity(pawn, relevantPawns, useOptimization);
            if (pawn.IsInDanger())
            {
                fullFirstLine = !string.IsNullOrEmpty(pawnActivity)
                    ? $"{pawnLabel} {pawnActivity} [IN DANGER]"
                    : $"{pawnLabel} [IN DANGER]";
                bareFirstLine = $"{pawnLabel} [IN DANGER]";
                isInDanger = true;
            }
            else
            {
                fullFirstLine = !string.IsNullOrEmpty(pawnActivity)
                    ? $"{pawnLabel} {pawnActivity}"
                    : pawnLabel;
                bareFirstLine = pawnLabel;
            }
        }

        var lines = new List<string>();
        if (nearbyPawns != null && nearbyPawns.Any())
        {
            int maxCount = isAnnouncement
                ? Math.Max(settings.Context.MaxPawnContextCount, nearbyPawns.Count)
                : settings.Context.MaxPawnContextCount;

            string nearbySection = GetCombinedNearbyList(pawn, nearbyPawns, relevantPawns,
                useOptimization, maxCount, ref isInDanger);

            lines.Add(nearbySection);
        }
        else
        {
            lines.Add("Nearby people: none");
        }

        AddContextualInfo(pawn.IsPlayer() ? nearbyPawns?.FirstOrDefault(p => !p.IsPlayer()) ?? pawn : pawn, lines, ref isInDanger);
        string rest = string.Join("\n", lines);
        string fullStatus = rest.Length > 0 ? $"{fullFirstLine}\n{rest}" : fullFirstLine;
        string bareStatus = rest.Length > 0 ? $"{bareFirstLine}\n{rest}" : bareFirstLine;

        return (fullStatus, bareStatus, isInDanger);
    }

    // Strips verbose age/stats noise during combat, preserving only the name and essential status (e.g. Slave, Prisoner)
    private static string GetCompactCombatLabel(Pawn p)
    {
        if (p.IsSlave) return $"{p.LabelShort}(Slave)";
        if (p.IsPrisoner) return $"{p.LabelShort}(Prisoner)";
        if (p.IsEnemy()) return $"{p.LabelShort}(Enemy)";
        if (p.IsVisitor()) return $"{p.LabelShort}(Visitor)";
        if (p.IsQuestLodger()) return $"{p.LabelShort}(Lodger)";
        if (p.Faction == null && !p.IsColonist) return $"{p.LabelShort}(Stranger)";
        return p.LabelShort;
    }

    private static string GetCombinedNearbyList(Pawn mainPawn, List<Pawn> nearbyPawns,
        HashSet<Pawn> relevantPawns, bool useOptimization, int maxCount, ref bool situationIsCritical)
    {
        if (nearbyPawns == null || nearbyPawns.Count == 0)
            return "Nearby: none";

        int count = Math.Min(nearbyPawns.Count, maxCount);
        var mainTarget = mainPawn.GetAttackTarget();

        var sameTargetPawns = new List<Pawn>();
        var otherDescriptions = new List<string>();
        bool localDangerFound = false;

        for (int i = 0; i < count; i++)
        {
            var p = nearbyPawns[i];
            if (p == null || p.IsPlayer()) continue;

            if (p.IsInDanger(true) && p.Faction == mainPawn.Faction)
                localDangerFound = true;

            // Separate allies focusing the speaker's current target to group them together
            if (mainTarget != null && p.GetAttackTarget() == mainTarget)
            {
                sameTargetPawns.Add(p);
                continue;
            }

            string label = GetPawnLabel(p, relevantPawns, useOptimization);

            string entry;
            var pawnState = Cache.Get(p);
            if (pawnState != null)
            {
                string activity = GetPawnActivity(p, relevantPawns, useOptimization);
                string talkRequestStr = "";
                // Do not leak combat battle logs or consume urgent requests of nearby pawns during combat
                if (!situationIsCritical && !p.IsInDanger() && !mainPawn.IsInCombat() && !p.IsInCombat())
                {
                    var talkRequest = pawnState.GetNextTalkRequest();
                    if (talkRequest != null && talkRequest.TalkType != TalkType.Urgent && !p.HostileTo(mainPawn) &&
                        SleepDialogueTracker.TryRefreshRequest(talkRequest))
                    {
                        pawnState.MarkRequestSpoken(talkRequest);
                        talkRequestStr = $" - {talkRequest.Prompt}";
                    }
                }
                entry = $"{label} {activity.StripTags()}{talkRequestStr}";
            }
            else
            {
                entry = label;
            }

            otherDescriptions.Add(entry);
        }

        if (localDangerFound)
            situationIsCritical = true;

        if (sameTargetPawns.Count == 0 && otherDescriptions.Count == 0)
            return "Nearby: none";

        // Group allies attacking the same target into a single concise line
        if (sameTargetPawns.Count > 0)
        {
            var targetLabels = new List<string>(sameTargetPawns.Count);
            for (int i = 0; i < sameTargetPawns.Count; i++)
                targetLabels.Add(GetCompactCombatLabel(sameTargetPawns[i]));
            string targets = string.Join(", ", targetLabels);
            if (otherDescriptions.Count == 0)
                return $"Nearby fighting same target: {targets}";

            otherDescriptions.Insert(0, $"Fighting same target: {targets}");
        }

        return "Nearby:\n- " + string.Join("\n- ", otherDescriptions);
    }

    private static HashSet<Pawn> CollectRelevantPawns(Pawn mainPawn, List<Pawn> nearbyPawns)
    {
        var relevantPawns = new HashSet<Pawn> { mainPawn };

        if (mainPawn.CurJob != null)
            AddJobTargetsToRelevantPawns(mainPawn.CurJob, relevantPawns);

        if (nearbyPawns != null)
        {
            for (int i = 0; i < nearbyPawns.Count; i++)
            {
                var nearby = nearbyPawns[i];
                if (nearby == null) continue;
                relevantPawns.Add(nearby);

                if (nearby.CurJob != null)
                    AddJobTargetsToRelevantPawns(nearby.CurJob, relevantPawns);
            }
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

        return DecorateText(activity, relevantPawns, pawn);
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
            float distSq = pawn.Position.DistanceToSquared(nearestHostile.Position);
            Faction referenceFaction = GetReferenceFaction(pawn);
            bool isMentalBreak = threatCount == 1 && nearestHostile.InMentalState && referenceFaction != null && nearestHostile.Faction == referenceFaction;
            string scaleLabel = isMentalBreak
                ? "Mental Break"
                : (threatCount == 1 ? "1 Hostile" : $"{threatCount} Hostiles");

            if (distSq <= 100f)
            {
                lines.Add($"Combat ({dangerAssessment}): Engaging in battle with {GetThreatLabel(nearestHostile, referenceFaction)}!");
                isInDanger = true;
            }
            else if (distSq <= 400f)
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
    private static string DecorateText(string text, HashSet<Pawn> relevantPawns, Pawn selfPawn = null)
    {
        if (string.IsNullOrEmpty(text) || relevantPawns == null || relevantPawns.Count == 0)
            return text;

        List<KeyValuePair<string, string>> replacements = null;
        string selfLabel = null;
        foreach (var p in relevantPawns)
        {
            if (p == null) continue;
            string key = p.LabelShort;
            if (string.IsNullOrEmpty(key)) continue;

            string replacement = selfPawn != null && p == selfPawn
                ? selfLabel ??= "RimTalk.PawnUtil.Self".Translate().ToString()
                : ContextHelper.GetDecoratedName(p);

            replacements ??= new List<KeyValuePair<string, string>>(relevantPawns.Count);
            replacements.Add(new KeyValuePair<string, string>(key, replacement));
        }

        if (replacements == null || replacements.Count == 0)
            return text;

        // Longer names first to avoid partial matches
        replacements.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

        foreach (var kv in replacements)
        {
            if (text.Contains(kv.Key))
            {
                text = text.Replace(kv.Key, kv.Value);
            }
        }

        return text;
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

    // Resolves current attack target across ranged aiming stances and melee attack jobs
    internal static Thing GetAttackTarget(this Pawn pawn)
    {
        if (pawn == null) return null;
        if (pawn.IsAttacking()) return pawn.TargetCurrentlyAimingAt.Thing;
        if (pawn.CurJob != null && (pawn.CurJob.def == JobDefOf.AttackMelee || pawn.CurJob.def == JobDefOf.AttackStatic || pawn.CurJob.def == JobDefOf.SocialFight))
            return pawn.CurJob.targetA.Thing;
        return null;
    }

    internal static bool IsBrawlingWithAlly(this Pawn pawn, out Pawn targetPawn)
    {
        targetPawn = null;
        if (pawn == null) return false;
        if (pawn.CurJobDef == JobDefOf.Hunt || pawn.CurJobDef == JobDefOf.Slaughter) return false;

        if (pawn.CurJobDef == JobDefOf.SocialFight)
        {
            targetPawn = pawn.CurJob?.targetA.Thing as Pawn;
            return true;
        }

        if (pawn.GetAttackTarget() is Pawn target && target.RaceProps?.Humanlike == true &&
            target.Faction != null && target.Faction == pawn.Faction)
        {
            targetPawn = target;
            return true;
        }

        return false;
    }

    internal static bool IsCaringOrCustodialJob(this Pawn pawn, out Pawn targetPawn)
    {
        targetPawn = null;
        if (pawn?.CurJob == null) return false;

        var def = pawn.CurJob.def;
        if (def == JobDefOf.Rescue ||
            def == JobDefOf.TendPatient ||
            def == JobDefOf.FeedPatient ||
            def == JobDefOf.Capture ||
            def == JobDefOf.Arrest ||
            def == JobDefOf.TakeWoundedPrisonerToBed ||
            def == JobDefOf.EscortPrisonerToBed)
        {
            targetPawn = pawn.CurJob.targetA.Thing as Pawn;
            return targetPawn != null;
        }

        string name = def?.defName;
        if (name != null && (name.Contains("Tend") || name.Contains("Feed") || name.Contains("Rescue") || name.Contains("Warden")))
        {
            targetPawn = pawn.CurJob.targetA.Thing as Pawn;
            return targetPawn != null;
        }

        return false;
    }

    internal static bool HasRelationalFriction(this Pawn pawn, Pawn target)
    {
        return HasRelationalFriction(pawn, target, out _);
    }

    internal static bool HasRelationalFriction(this Pawn pawn, Pawn target, out bool isSevere)
    {
        isSevere = false;
        if (pawn == null || target == null || pawn == target) return false;

        if (target.IsEnemy())
        {
            isSevere = true;
            return true;
        }

        float opinion = pawn.relations?.OpinionOf(target) ?? 0f;

        if (opinion <= -40f)
            isSevere = true;

        MemoryHookService.SyncActiveSocialThoughts(pawn, target);

        var hediff = Hediff_Persona.GetOrAddNew(pawn);
        bool hasFrictionFromMemory = false;
        if (hediff?.Memories != null)
        {
            int currentTick = Current.ProgramState == ProgramState.Playing ? GenTicks.TicksGame : 0;
            const float halfLife = PawnMemoryTracker.DefaultHalfLifeDays;

            for (int i = 0; i < hediff.Memories.Count; i++)
            {
                var m = hediff.Memories[i];
                if (m != null && m.TargetPawnId == target.thingIDNumber)
                {
                    float score = m.GetRecallScore(currentTick, halfLife);
                    if (score <= -30f)
                    {
                        isSevere = true;
                        return true;
                    }
                    if (score <= -15f)
                    {
                        hasFrictionFromMemory = true;
                    }
                }
            }
        }

        if (isSevere)
            return true;

        return opinion <= -20f || hasFrictionFromMemory;
    }

    internal static string GetActivity(this Pawn pawn)
    {
        if (pawn == null) return null;

        if (pawn.InMentalState)
            return pawn.MentalState?.InspectLine;

        if (pawn.CurJobDef is null)
            return null;

        var targetThing = pawn.GetAttackTarget();
        if (targetThing != null)
        {
            string targetLabel = Describer.StripConditionSuffix(targetThing.LabelShortCap);
            // Only prepend faction owner for structures/items, not for pawns
            if (targetThing is not Pawn && targetThing.Faction != null && targetThing.Faction != pawn.Faction)
            {
                bool isTargetPlayer = targetThing.Faction == Faction.OfPlayer;
                string ownerPrefix = isTargetPlayer ? "invader's" : $"{targetThing.Faction.Name}'s";
                return $"Attacking {ownerPrefix} {targetLabel}";
            }
            return $"Attacking {targetLabel}";
        }

        var lord = Describer.StripConditionSuffix(pawn.GetLord()?.LordJob?.GetReport(pawn));
        var job = Describer.StripConditionSuffix(pawn.jobs?.curDriver?.GetReport());
        if (pawn.CurJobDef == JobDefOf.Wait_Combat && !pawn.HasActiveHostiles())
        {
            job = "on alert";
        }

        string activity = lord == null ? job :
            job == null ? lord :
            $"{lord} ({job})";

        if (ResearchJobDefNames.Contains(pawn.CurJob?.def.defName))
        {
            activity = AppendResearchProgress(activity);
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

    private static readonly TargetIndex[] AllTargetIndices = [TargetIndex.A, TargetIndex.B, TargetIndex.C];

    private static void AddJobTargetsToRelevantPawns(Job job, HashSet<Pawn> relevantPawns)
    {
        if (job == null) return;

        for (int i = 0; i < AllTargetIndices.Length; i++)
        {
            TargetIndex index = AllTargetIndices[i];
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
