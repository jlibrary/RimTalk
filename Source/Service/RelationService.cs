using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimTalk.Service
{
    public static class RelationsService
    {
        private const float FriendOpinionThreshold = 20f;
        private const float RivalOpinionThreshold = -20f;

        public static string GetRelationsString(Pawn pawn, List<Pawn> nearbyPawns)
        {
            if (pawn?.relations == null) return "";

            StringBuilder relationsSb = new StringBuilder();

            foreach (Pawn otherPawn in nearbyPawns)
            {
                if (otherPawn == pawn || !otherPawn.RaceProps.Humanlike || otherPawn.Dead || otherPawn.relations.hidePawnRelations) continue;

                string label = null;

                // --- Step 1: Check for the most important direct or family relationship ---
                PawnRelationDef mostImportantRelation = pawn.GetMostImportantRelation(otherPawn);

                if (mostImportantRelation != null)
                {
                    // If a specific relation exists (e.g., Son, Father, Lover), use its label.
                    label = mostImportantRelation.GetGenderSpecificLabelCap(otherPawn);
                }
                else
                {
                    // --- Step 2: If no specific relation, check for opinion-based ones ---
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
                
                // If we found any relevant relationship, add it to the string in the new format.
                if (!string.IsNullOrEmpty(label))
                {
                    string pawnName = otherPawn.Name.ToStringShort;
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
    }
}