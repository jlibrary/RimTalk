using System.Collections.Generic;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
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
    
    public static Hediff_Persona GetOrAddNew(Pawn pawn)
    {
        var def = DefDatabase<HediffDef>.GetNamedSilentFail(RimtalkHediff);
        if (pawn?.health?.hediffSet == null || def == null) return null;
    
        var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_Persona;
    
        if (hediff == null)
        {
            hediff = (Hediff_Persona)HediffMaker.MakeHediff(def, pawn);
        
            // Assign a random personality on creation
            PersonalityData randomPersonalityData = Constant.Personalities.RandomElement();
            hediff.Personality = randomPersonalityData.Persona;
        
            if (pawn.IsSlave || pawn.IsPrisoner || PawnService.IsVisitor(pawn) || PawnService.IsInvader(pawn))
            {
                hediff.TalkInitiationWeight = 0.3f;
            }
            else
            {
                hediff.TalkInitiationWeight = randomPersonalityData.Chattiness;
            }
        
            pawn.health.AddHediff(hediff);
        }
    
        // Ensure dictionary is initialized (for both new and existing hediffs)
        if (hediff.SpokenThoughtTicks == null)
        {
            hediff.SpokenThoughtTicks = new Dictionary<string, int>();
        }
    
        return hediff;
    }
    
    // Check if thought was spoken recently, if not mark it as spoken
    // Returns true if successfully marked (was not spoken recently)
    // Returns false if already spoken recently (within intervalTicks)
    public bool TryMarkAsSpoken(Thought thought)
    {
        string key = $"{thought.def.defName}_{thought.CurStageIndex}";
        int currentTick = Find.TickManager.TicksGame;
    
        // Randomize interval from 1 to 2.5 days
        int randomInterval = Random.Range(60000, 150000);
    
        if (SpokenThoughtTicks.TryGetValue(key, out int lastTick))
        {
            if (currentTick - lastTick < randomInterval)
            {
                return false; // Already spoken recently
            }
        }
    
        SpokenThoughtTicks[key] = currentTick;

        // Also mark for nearby pawns so they don't talk about the same thing
        var nearbyPawns = PawnSelector.GetAllNearByPawns(thought.pawn);
        foreach (var p in nearbyPawns)
        {
            if (p == thought.pawn) continue; 
            var hediff = GetOrAddNew(p);
            if (hediff != null)
            {
                hediff.SpokenThoughtTicks[key] = currentTick;
            }
        }

        return true;
    }
}