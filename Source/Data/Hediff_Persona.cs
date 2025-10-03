using System.Collections.Generic;
using Verse;

namespace RimTalk.Data;

public class Hediff_Persona : Hediff
{
    public const string RimtalkHediff = "RimTalk_PersonaData";
    public string Personality;
    public float TalkInitiationWeight = 1.0f;
    public Dictionary<string, int> SpokenThoughtTicks = new();
    
    public override bool Visible => false;
    public override string Label => "RimTalk Persona Data";

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref Personality, "Personality");
        Scribe_Values.Look(ref TalkInitiationWeight, "TalkInitiationWeight", 1.0f);
        Scribe_Collections.Look(ref SpokenThoughtTicks, "SpokenThoughtTicks", LookMode.Value, LookMode.Value);
        
        if (SpokenThoughtTicks == null)
        {
            SpokenThoughtTicks = new Dictionary<string, int>();
        }
    }
}