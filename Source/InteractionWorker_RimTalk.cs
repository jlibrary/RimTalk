using Verse;
using RimWorld;


namespace RimTalk
{
    public class InteractionWorker_RimTalk : InteractionWorker_Chitchat
    {

        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            return 0f;
        }
    }
}