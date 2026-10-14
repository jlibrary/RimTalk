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
}
