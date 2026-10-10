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
}
