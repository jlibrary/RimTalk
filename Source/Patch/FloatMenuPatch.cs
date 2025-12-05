using System.Collections.Generic;
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
    private const int ClickRadiusCells = 1;
    
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
        
        IntVec3 clickCell = IntVec3.FromVector3(clickPos);
        
        // Check for pawns in a square around click position
        for (int x = clickCell.x - ClickRadiusCells; x <= clickCell.x + ClickRadiusCells; x++)
        {
            for (int z = clickCell.z - ClickRadiusCells; z <= clickCell.z + ClickRadiusCells; z++)
            {
                IntVec3 checkCell = new IntVec3(x, 0, z);
                
                if (!checkCell.InBounds(pawn.Map)) continue;
                
                Pawn targetPawn = checkCell.GetFirstPawn(pawn.Map);
                
                if (targetPawn == null) continue;
                
                // Check if clicked on the selected pawn (player talking to pawn)
                if (targetPawn == pawn)
                {
                    if (Settings.Get().PlayerDialogueMode != Settings.PlayerDialogueMode.Disabled)
                        AddTalkOption(__result, Cache.GetPlayer(), pawn);
                    
                    return; // Don't check for other pawns if we found ourselves
                }
                
                // Check if target is eligible for conversation
                if ((targetPawn.RaceProps.Humanlike || targetPawn.HasVocalLink()) &&
                    pawn.IsTalkEligible() && 
                    pawn.CanReach(targetPawn, PathEndMode.Touch, Danger.None))
                {
                    AddTalkOption(__result, pawn, targetPawn);
                    return;
                }
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