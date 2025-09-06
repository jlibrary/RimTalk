using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

public static class RelationsService
{
    private const float FriendOpinionThreshold = 20f;

    public static string GetRelationsString(Pawn pawn)
    {
        if (pawn?.relations == null) return "";

        StringBuilder relationsSb = new StringBuilder();
        HashSet<Pawn> processedPawns = new HashSet<Pawn>();

        // --- Loop 1: Important Family Relationships (Very Fast) ---
        // Iterates a small pre-filtered list of relatives.
        foreach (Pawn relative in pawn.relations.PotentiallyRelatedPawns)
        {
            if (relative.Dead || relative.relations.hidePawnRelations) continue;

            PawnRelationDef mostImportantRelation = pawn.GetMostImportantRelation(relative);
            if (mostImportantRelation != null && mostImportantRelation.familyByBloodRelation)
            {
                relationsSb.Append($"{mostImportantRelation.GetGenderSpecificLabel(relative)}: {OpinionString(pawn, relative)}, ");
                processedPawns.Add(relative);
            }
        }

        // --- Loop 2: Direct Social Relations and Friendships (Also Fast) ---
        // Iterates a small list of lovers, rivals, etc.
        foreach (DirectPawnRelation relation in pawn.relations.DirectRelations)
        {
            Pawn otherPawn = relation.otherPawn;
            if (processedPawns.Contains(otherPawn) || otherPawn.Dead || otherPawn.relations.hidePawnRelations) continue;

            string relationLabel = relation.def.GetGenderSpecificLabel(otherPawn);
            relationsSb.Append($"{relationLabel}: {OpinionString(pawn, otherPawn)}, ");
            processedPawns.Add(otherPawn);
        }

        if (relationsSb.Length > 0)
        {
            // Remove the trailing comma and space
            relationsSb.Length -= 2;
            return "Relations: " + relationsSb.ToString();
        }

        return "";
    }

    private static string OpinionString(Pawn pawn, Pawn otherPawn)
    {
        var opinion = pawn.relations.OpinionOf(otherPawn).ToStringWithSign();
        return otherPawn.RaceProps.Humanlike
            ? $"{otherPawn.Name.ToStringShort} {opinion}"
            : $"{otherPawn.Name.ToStringShort}(Animal)";
    }
}