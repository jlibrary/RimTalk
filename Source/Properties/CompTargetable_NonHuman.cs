using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimTalk.Properties;

public class CompTargetable_NonHuman : CompTargetable
{
    protected override bool PlayerChoosesTarget => true;

    protected override TargetingParameters GetTargetingParameters()
    {
        return new TargetingParameters()
        {
            canTargetPawns = true,
            canTargetBuildings = false,
            canTargetAnimals = true,
            canTargetMechs = true,
            canTargetSelf = true
        };
    }

    public override IEnumerable<Thing> GetTargets(Thing targetChosenByPlayer = null)
    {
        yield return targetChosenByPlayer;
    }
}
