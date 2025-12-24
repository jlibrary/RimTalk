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
    /// <summary>
    /// fallback
    /// </summary>
    private const int ClickRadiusCells = 1;

#if V1_5
    [HarmonyPostfix]
    public static void Postfix(Vector3 clickPos, Pawn pawn, ref List<FloatMenuOption> __result)
    {
        TryAddTalkOption(__result, pawn, clickPos);
    }
#else
    [HarmonyPostfix]
    public static void Postfix(
        List<Pawn> selectedPawns,
        Vector3 clickPos,
        FloatMenuContext context,
        ref List<FloatMenuOption> __result)
    {
        Pawn pawn = (selectedPawns is { Count: 1 }) ? selectedPawns[0] : null;
        TryAddTalkOption(__result, pawn, clickPos);
    }
#endif

    /// <summary>
    /// Decide whether add talk item to float menu.
    /// </summary>
    private static void TryAddTalkOption(List<FloatMenuOption> result, Pawn selectedPawn, Vector3 clickPos)
    {
        if (result == null) return;
        if (!Settings.Get().AllowCustomConversation) return;

        if (selectedPawn == null || selectedPawn.Drafted) return;
        if (!selectedPawn.Spawned || selectedPawn.Dead) return;

        Map map = selectedPawn.Map;
        IntVec3 clickCell = IntVec3.FromVector3(clickPos);
        
        List<Pawn> pawns = Cache.GetPawnsNear(map, clickCell, ClickRadiusCells).ToList();

        foreach (var p in pawns)
        {
            IntVec3 pos = p.Position;
            int x = clickCell.x;
            int z = clickCell.z;
            Pawn initiator;
            Pawn target;


            if (pos.x<x-ClickRadiusCells||pos.x>x+ClickRadiusCells||pos.z<z-ClickRadiusCells||pos.z>z+ClickRadiusCells)
                continue;

            if (TryResolveForHitPawn(selectedPawn, p, out initiator, out target))
            {
                AddTalkOption(result, initiator, target);
            }
        }
    }

    /// <summary>
    /// Check User->Pawn talk or Pawn->Pawn talk 
    /// </summary>
    private static bool TryResolveForHitPawn(
        Pawn selectedPawn,
        Pawn hitPawn,
        out Pawn initiator,
        out Pawn target)
    {
        initiator = null;
        target = null;

        if (selectedPawn == null || hitPawn == null) return false;

        // User->Pawn
        if (hitPawn == selectedPawn)
        {
            if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.Disabled)
                return false;

            var playerPawn = Cache.GetPlayer();
            if (playerPawn == null)
                return false;

            initiator = playerPawn;  
            target = selectedPawn;    
            return true;
        }

        // Pawn → Pawn
        if (!IsValidPawnToPawnConversation(selectedPawn, hitPawn))
            return false;

        initiator = selectedPawn;
        target = hitPawn;
        return true;
    }

    /// <summary>
    /// Check if Pawn->Pawn talk is legal.
    /// </summary>
    private static bool IsValidPawnToPawnConversation(Pawn initiator, Pawn target)
    {
        if (initiator == null || target == null) return false;

        if (!initiator.Spawned || initiator.Dead) return false;
        if (!target.Spawned || target.Dead) return false;

        // Initiator
        if (!initiator.IsTalkEligible())
            return false;

        // Target
        if (!(target.RaceProps?.Humanlike ?? false) && !target.HasVocalLink())
            return false;

        if (initiator == Cache.GetPlayer())
            return true;

        // Could add path to reach
        if (!initiator.CanReach(target, PathEndMode.Touch, Danger.None))
            return false;

        return true;
    }

    /// <summary>
    /// Add item to float menu
    /// </summary>
    private static void AddTalkOption(List<FloatMenuOption> result, Pawn initiator, Pawn target)
    {
        if (initiator == null || target == null) return;

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