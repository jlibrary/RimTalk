using System.Collections.Generic;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Data;

public class Hediff_Persona : Hediff
{
    private const string RimtalkHediff = "RimTalk_PersonaData";
    private Dictionary<string, int> _spokenThoughtTicks = new();
    public string Personality;
    public float TalkInitiationWeight = 1.0f;
    public override bool Visible => false;
    
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref Personality, "Personality");
        Scribe_Values.Look(ref TalkInitiationWeight, "TalkInitiationWeight", 1.0f);
        Scribe_Collections.Look(ref _spokenThoughtTicks, "SpokenThoughtTicks", LookMode.Value, LookMode.Value);
        
        if (_spokenThoughtTicks == null)
        {
            _spokenThoughtTicks = new Dictionary<string, int>();
        }
    }
    
    public static Hediff_Persona GetOrAddNew(Pawn pawn)
    {
        var def = DefDatabase<HediffDef>.GetNamedSilentFail(RimtalkHediff);
        if (pawn?.health?.hediffSet == null || def == null) return null;

        if (pawn.health.hediffSet.GetFirstHediffOfDef(def) is not Hediff_Persona hediff)
        {
            hediff = (Hediff_Persona)HediffMaker.MakeHediff(def, pawn);
        
            // Assign a random personality on creation
            PersonalityData randomPersonalityData =
                pawn.RaceProps.Humanlike ? Constant.Personalities.RandomElement()
                : pawn.RaceProps.Animal ? Constant.PersonaAnimal
                : pawn.RaceProps.IsMechanoid ? Constant.PersonaMech
                : Constant.PersonaNonHuman;
            hediff.Personality = randomPersonalityData.Persona;
        
            if (pawn.IsSlave || pawn.IsPrisoner || pawn.IsVisitor() || pawn.IsEnemy())
            {
                hediff.TalkInitiationWeight = 0.2f;
            }
            else
            {
                hediff.TalkInitiationWeight = randomPersonalityData.Chattiness;
            }
        
            pawn.health.AddHediff(hediff);
        }
    
        // Ensure dictionary is initialized (for both new and existing hediffs)
        hediff._spokenThoughtTicks ??= new Dictionary<string, int>();
    
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
    
        if (_spokenThoughtTicks.TryGetValue(key, out int lastTick))
        {
            if (currentTick - lastTick < randomInterval)
            {
                return false; // Already spoken recently
            }
        }
    
        _spokenThoughtTicks[key] = currentTick;

        // Also mark for nearby pawns so they don't talk about the same thing
        var nearbyPawns = PawnSelector.GetAllNearByPawns(thought.pawn);
        foreach (var p in nearbyPawns)
        {
            if (p == thought.pawn) continue; 
            var hediff = GetOrAddNew(p);
            if (hediff != null)
            {
                hediff._spokenThoughtTicks[key] = currentTick;
            }
        }

        return true;
    }
}