using RimTalk.Data;
using Verse;

namespace RimTalk.Compatibility;

// Switch to GameComponent, which has a reliable FinalizeInit()
public class ChattinessCompatibilityTracker: GameComponent
{
    public ChattinessCompatibilityTracker(Game game)
    {
    }
    
    private bool _wasRun;

    public override void ExposeData()
    {
        // No base.ExposeData() needed for GameComponent usually, but good practice if added later
        Scribe_Values.Look(ref _wasRun, "rimTalk_ChattinessHalved", false);
    }

    // This runs after the map and world are fully loaded
    public override void FinalizeInit()
    {
        if (!_wasRun)
        {
            HalveAllExistingWeights();
            _wasRun = true;
        }
    }

    private void HalveAllExistingWeights()
    {
        var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail("RimTalk_PersonaData");
        if (hediffDef == null) return;

        int count = 0;

        // 1. Iterate over all Maps (Colonists, Prisoners, Slaves)
        if (Find.Maps != null)
        {
            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (TryPatchPawn(pawn, hediffDef)) count++;
                }
            }
        }

        // 2. Iterate over World Pawns (Caravans, faction leaders, kidnapped colonists)
        if (Find.WorldPawns != null)
        {
            foreach (var pawn in Find.WorldPawns.AllPawnsAlive)
            {
                if (TryPatchPawn(pawn, hediffDef)) count++;
            }
        }

        Log.Message($"Compatibility patch run. Updated {count} personas.");
    }

    private bool TryPatchPawn(Pawn pawn, HediffDef def)
    {
        if (pawn?.health?.hediffSet == null) return false;

        // Use GetFirstHediffOfDef to find the persona
        if (pawn.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_Persona hediff)
        {
            hediff.TalkInitiationWeight /= 2f;
            return true;
        }
        return false;
    }
}