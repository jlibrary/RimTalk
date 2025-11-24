using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(HealthCardUtility), "DrawHediffRow")]
public static class Patch_HealthCardUtility_DrawHediffRow
{
    public static void Prefix(Rect rect, Pawn pawn, IEnumerable<Hediff> diffs, ref float curY)
    {
        if (diffs.Any(h => h.def == Constant.VocalLinkDef))
        {
            Rect rowRect = new Rect(0f, curY, rect.xMax, 22f);

            if (Widgets.ButtonInvisible(rowRect, false))
            {
                Find.WindowStack.Add(new PersonaEditorWindow(pawn));
                Event.current.Use();
            }
        }
    }
}