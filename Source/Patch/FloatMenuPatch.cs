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
    [HarmonyPostfix]
    public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> __result)
    {
        if (pawn == null || __result == null) return;
        TryAddTalkOption(__result, pawn, clickPos);
    }
#else
    [HarmonyPostfix]
    public static void Postfix(
        List<FloatMenuOption> __result,
        List<Pawn> selectedPawns,
        Vector3 clickPos,
        ref FloatMenuContext context)
    {
        if (__result == null || selectedPawns == null || selectedPawns.Count != 1) return;

        Pawn pawn = selectedPawns[0];
        if (pawn == null) return;

        TryAddTalkOption(__result, pawn, clickPos);
    }
#endif

    private static void TryAddTalkOption(List<FloatMenuOption> result, Pawn pawn, Vector3 clickPos)
    {
        if (!pawn.Spawned) return;
        if (pawn.Dead) return;

        Map map = pawn.Map;
        IntVec3 clickCell = IntVec3.FromVector3(clickPos);

        if (clickCell.InBounds(map))
        {
            Pawn targetOnCell = clickCell.GetFirstPawn(map);
            if (targetOnCell != null)
            {
                // 1.1 Selected Pawn
                if (targetOnCell == pawn)
                {
                    var playerPawn = Cache.GetPlayer();
                    if (playerPawn != null && playerPawn.IsTalkEligible())
                    {
                        AddTalkOption(result, playerPawn, pawn);
                    }
                    return;
                }

                // 1.2 Nearby Pawn
                if (IsValidConversationTarget(pawn, targetOnCell))
                {
                    AddTalkOption(result, pawn, targetOnCell);
                    return;
                }
            }
        }

        // ===== 2. Search Nearby Pawn =====
        for (int dx = -ClickRadiusCells; dx <= ClickRadiusCells; dx++)
        {
            for (int dz = -ClickRadiusCells; dz <= ClickRadiusCells; dz++)
            {
                if (dx == 0 && dz == 0) continue;

                IntVec3 checkCell = new IntVec3(clickCell.x + dx, 0, clickCell.z + dz);
                if (!checkCell.InBounds(map)) continue;

                Pawn target = checkCell.GetFirstPawn(map);
                if (target == null) continue;

                // 2.1 fallback
                if (target == pawn)
                {
                    var playerPawn = Cache.GetPlayer();
                    if (playerPawn != null && playerPawn.IsTalkEligible())
                    {
                        AddTalkOption(result, playerPawn, pawn);
                    }
                    return;
                }

                // 2.2 nearby pawn
                if (IsValidConversationTarget(pawn, target))
                {
                    AddTalkOption(result, pawn, target);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Check if Pawn can talk 
    /// </summary>
    private static bool IsValidConversationTarget(Pawn initiator, Pawn target)
    {
        if (initiator == null || target == null) return false;
        if (!target.Spawned || target.Dead) return false;

        if (!(target.RaceProps?.Humanlike ?? false) && !target.HasVocalLink())
            return false;

        if (!initiator.CanReach(target, PathEndMode.Touch, Danger.None))
            return false;

        if (!initiator.IsTalkEligible()) // Maybe user want to force pawns to talk? or we can use settings to control
            return false;

        return true;
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
