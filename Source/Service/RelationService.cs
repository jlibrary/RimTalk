using System.Text;
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

        foreach (Pawn otherPawn in PawnSelector.GetAllNearByPawns(pawn))
        {
            if (otherPawn == pawn || !otherPawn.RaceProps.Humanlike || otherPawn.Dead ||
                otherPawn.relations.hidePawnRelations) continue;

            string label = null;

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
            if (string.IsNullOrEmpty(label))
            {
                float opinion = pawn.relations.OpinionOf(otherPawn);
                if (opinion >= FriendOpinionThreshold)
                {
                    label = "Friend".Translate();
                }
                else if (opinion <= RivalOpinionThreshold)
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
                string opinion = pawn.relations.OpinionOf(otherPawn).ToStringWithSign();
                relationsSb.Append($"{pawnName}({label}) {opinion}, ");
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

    private static string GetStatusLabel(Pawn pawn, Pawn otherPawn)
    {
        // Master relationship
        if ((pawn.IsPrisoner || pawn.IsSlave) && otherPawn.IsColonist)
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
}