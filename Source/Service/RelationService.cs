using System;
using System.Linq;
using System.Text;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Service;

public static class RelationsService
{
    private const float FriendOpinionThreshold = 20f;
    private const float RivalOpinionThreshold = -20f;

    public static string GetRelationsString(Pawn pawn)
    {
        if (pawn?.relations == null) return "";

        StringBuilder relationsSb = new StringBuilder();

        foreach (Pawn otherPawn in PawnSelector.GetAllNearByPawns(pawn).Take(Settings.Get().Context.MaxPawnContextCount - 1))
        {
            if (otherPawn == pawn || (!otherPawn.RaceProps.Humanlike && !otherPawn.HasVocalLink()) || otherPawn.Dead ||
                otherPawn.relations is { hidePawnRelations: true }) continue;

            string label = null;

            try
            {
                float opinionValue = pawn.relations.OpinionOf(otherPawn);

                // --- Step 1: Check for the most important direct or family relationship ---
                PawnRelationDef mostImportantRelation = pawn.GetMostImportantRelation(otherPawn);
                if (mostImportantRelation != null)
                {
                    label = mostImportantRelation.GetGenderSpecificLabelCap(otherPawn);
                }

                // --- Step 2: If no family relation, check for an overriding status (master, slave, etc.) ---
                if (string.IsNullOrEmpty(label))
                {
                    label = GetStatusLabel(pawn, otherPawn);
                }

                // --- Step 3: If no other label found, fall back to opinion-based relationship ---
                if (string.IsNullOrEmpty(label) && !pawn.IsVisitor() && !pawn.IsEnemy())
                {
                    if (opinionValue >= FriendOpinionThreshold)
                    {
                        label = "Friend".Translate();
                    }
                    else if (opinionValue <= RivalOpinionThreshold)
                    {
                        label = "Rival".Translate();
                    }
                    else
                    {
                        label = "Acquaintance".Translate();
                    }
                }

                // If we found any relevant relationship, add it to the string.
                if (!string.IsNullOrEmpty(label))
                {
                    string pawnName = otherPawn.LabelShort;
                    string opinion = opinionValue.ToStringWithSign();
                    relationsSb.Append($"{pawnName}({label}) {opinion}, ");
                }
            }
            catch (Exception)
            {
                // Skip this pawn if opinion calculation fails due to mod conflicts
            }
        }

        if (relationsSb.Length > 0)
        {
            // Remove the trailing comma and space
            relationsSb.Length -= 2;
            return "Relations: " + relationsSb;
        }

        return "";
    }

    public static string GetAllSocialString(Pawn pawn)
    {
        if (pawn?.relations == null) return "";

        var others = new HashSet<Pawn>();
        if (pawn.Map?.mapPawns?.AllPawnsSpawned != null)
        {
            foreach (var p in pawn.Map.mapPawns.AllPawnsSpawned)
                if (p != null) others.Add(p);
        }

        if (Find.WorldPawns != null)
        {
            foreach (var p in Find.WorldPawns.AllPawnsAlive)
                if (p != null) others.Add(p);
        }

        foreach (var p in pawn.relations.RelatedPawns)
        {
            if (p != null) others.Add(p);
        }

        others.Remove(pawn);

        var relationsSb = new StringBuilder();
        foreach (var otherPawn in others.OrderBy(p => p.LabelShort))
        {
            if ((!otherPawn.RaceProps.Humanlike && !otherPawn.HasVocalLink()) || otherPawn.Dead ||
                otherPawn.relations is { hidePawnRelations: true }) continue;

            if (TryGetSocialLabel(pawn, otherPawn, out var label, out var opinionValue))
            {
                string pawnName = otherPawn.LabelShort;
                string opinion = opinionValue.ToStringWithSign();
                relationsSb.Append($"{pawnName}({label}) {opinion}, ");
            }
        }

        if (relationsSb.Length > 0)
        {
            relationsSb.Length -= 2;
            return "Social: " + relationsSb;
        }

        return "";
    }

    public static string GetAllRelationsString(Pawn pawn)
    {
        if (pawn?.relations == null) return "";

        var related = pawn.relations.RelatedPawns?.ToList();
        if (related.NullOrEmpty()) return "";

        var sb = new StringBuilder();
        foreach (var otherPawn in related.Where(p => p != null && p != pawn).OrderBy(p => p.LabelShort))
        {
            var relationLabels = pawn.GetRelations(otherPawn)
                .Select(r => r?.GetGenderSpecificLabelCap(otherPawn).ToString())
                .Where(label => !string.IsNullOrEmpty(label))
                .ToList();

            if (relationLabels.Count == 0) continue;

            sb.Append($"{otherPawn.LabelShort}({string.Join("/", relationLabels)}), ");
        }

        if (sb.Length > 0)
        {
            sb.Length -= 2;
            return "Relations: " + sb;
        }

        return "";
    }

    private static string GetStatusLabel(Pawn pawn, Pawn otherPawn)
    {
        // Master relationship
        if ((pawn.IsPrisoner || pawn.IsSlave) && otherPawn.IsFreeNonSlaveColonist)
        {
            return "Master".Translate();
        }

        // Prisoner or slave labels
        if (otherPawn.IsPrisoner) return "Prisoner".Translate();
        if (otherPawn.IsSlave) return "Slave".Translate();

        // Hostile relationship
        if (pawn.Faction != null && otherPawn.Faction != null && pawn.Faction.HostileTo(otherPawn.Faction))
        {
            return "Enemy".Translate();
        }

        // No special status found
        return null;
    }

    private static bool TryGetSocialLabel(Pawn pawn, Pawn otherPawn, out string label, out float opinionValue)
    {
        label = null;
        opinionValue = 0f;

        try
        {
            opinionValue = pawn.relations.OpinionOf(otherPawn);
        }
        catch (Exception)
        {
            return false;
        }

        var mostImportantRelation = pawn.GetMostImportantRelation(otherPawn);
        if (mostImportantRelation != null)
        {
            label = mostImportantRelation.GetGenderSpecificLabelCap(otherPawn);
        }

        if (string.IsNullOrEmpty(label))
        {
            label = GetStatusLabel(pawn, otherPawn);
        }

        if (string.IsNullOrEmpty(label) && !pawn.IsVisitor() && !pawn.IsEnemy())
        {
            if (opinionValue >= FriendOpinionThreshold)
            {
                label = "Friend".Translate();
            }
            else if (opinionValue <= RivalOpinionThreshold)
            {
                label = "Rival".Translate();
            }
            else
            {
                label = "Acquaintance".Translate();
            }
        }

        return !string.IsNullOrEmpty(label);
    }
}
