using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Patch;

#if V1_5
[HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ChoicesAtFor))]
#else
[HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
#endif
public static class FloatMenuPatch
{
    private const float ClickRadius = 1.2f; // Radius in cells to check around click position
    
#if V1_5
    public static void Postfix(Vector3 clickPos, Pawn pawn, ref List<FloatMenuOption> __result)
    {
#else
    public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, FloatMenuContext context,
        ref List<FloatMenuOption> __result)
    {
        if (selectedPawns is not { Count: 1 }) return;

        Pawn pawn = selectedPawns[0];
#endif
        if (!Settings.Get().AllowCustomConversation) return;
        if (pawn == null || pawn.Drafted) return;
        
        IntVec3 cell = IntVec3.FromVector3(clickPos);
        
        // Check if clicked on or near the selected pawn (player talking to pawn)
        float distanceToSelf = pawn.Position.DistanceTo(cell);
        if (distanceToSelf <= ClickRadius)
        {
            AddTalkOption(__result, Cache.GetPlayer(), pawn);
            return; // Don't check for other pawns if we're clicking on ourselves
        }

        // Check for other pawns in a radius around click position
        List<Thing> thingsInRadius = GenRadial.RadialDistinctThingsAround(cell, pawn.Map, ClickRadius, true).ToList();
        
        foreach (Thing thing in thingsInRadius)
        {
            if (thing is Pawn targetPawn && 
                targetPawn != pawn && 
                (targetPawn.RaceProps.Humanlike || targetPawn.HasVocalLink()))
            {
                if (pawn.IsTalkEligible() && pawn.CanReach(targetPawn, PathEndMode.Touch, Danger.None))
                {
                    AddTalkOption(__result, pawn, targetPawn);
                }
                break;
            }
        }
    }

    private static void AddTalkOption(List<FloatMenuOption> result, Pawn initiator, Pawn target)
    {
        result.Add(new FloatMenuOption(
            "RimTalk.FloatMenu.ChatWith".Translate(target.LabelShortCap),
            delegate 
            { 
                Find.WindowStack.Add(new CustomDialogueWindow(initiator, target)); 
            },
            MenuOptionPriority.Default,
            null,
            target
        ));
    }
}