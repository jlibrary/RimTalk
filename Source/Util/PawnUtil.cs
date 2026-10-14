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

        // Hunting and ability casts also put a verb in Stance_Busy; only firing at something hostile is combat.
        if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null &&
            busy.focusTarg.Thing is { } focus && focus.HostileTo(pawn))
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
        // A captured raider, tamed animal or recovered berserker stays a remembered target but is no longer hostile.
        if (!target.HostileTo(pawn)) return false;
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

    private static readonly TargetIndex[] AllTargetIndices = [TargetIndex.A, TargetIndex.B, TargetIndex.C];

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
