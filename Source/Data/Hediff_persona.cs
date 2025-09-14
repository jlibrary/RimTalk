using Verse;

namespace RimTalk.Data
{
    public class Hediff_Persona : Hediff, IExposable
    {
        public string Personality;
        public float TalkInitiationWeight = 1.0f;
        
        public override string Label => "RimTalk Persona Data"; // Not visible to player

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref Personality, "Personality");
            Scribe_Values.Look(ref TalkInitiationWeight, "TalkInitiationWeight", 1.0f);
        }
    }
}