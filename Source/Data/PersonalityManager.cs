using System.Collections.Generic;
using Verse;

namespace RimTalk.Data
{
    public class PersonalityManager : GameComponent
    {
        private Dictionary<int, string> pawnPersonalities = new Dictionary<int, string>();

        public PersonalityManager(Game game) : base() { }

        public string GetPersonality(Pawn pawn)
        {
            if (pawnPersonalities.TryGetValue(pawn.thingIDNumber, out string personality))
            {
                return personality;
            }
        
            personality = Constant.Personalities.RandomElement();
            pawnPersonalities[pawn.thingIDNumber] = personality;
            return personality;
        }

        public void SetPersonality(Pawn pawn, string personality)
        {
            pawnPersonalities[pawn.thingIDNumber] = personality;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pawnPersonalities, "pawnPersonalities", LookMode.Value, LookMode.Value);
            if (pawnPersonalities == null)
                pawnPersonalities = new Dictionary<int, string>();
        }
    }
}