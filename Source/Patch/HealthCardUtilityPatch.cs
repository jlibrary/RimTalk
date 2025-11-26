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
        if (pawn == null)
        {
            return;
        }
        if (!diffs.Any(h => h.def == Constant.VocalLinkDef))
        {
            return;
        }
        float leftWidth = rect.width * 0.375f;
        float textWidth = rect.width - leftWidth;

        float simulatedCurY = curY;

        foreach (var group in diffs.GroupBy(h => h.UIGroupKey))
        {
            int count = 0;
            Hediff first = null;

            foreach (var hediff in group)
            {
                if (count == 0)
                {
                    first = hediff;
                }
                count++;
            }
            if (first == null)
            {
                continue;
            }
            string label = first.LabelCap;
            if (count != 1)
            {
                label = $"{label} x{count.ToStringCached()}";
            }

            float rowHeight = Verse.Text.CalcHeight(label, textWidth);
            Rect rowRect = new Rect(leftWidth, simulatedCurY, textWidth, rowHeight);
            if (group.Any(h => h.def == Constant.VocalLinkDef))
            {
                if (Widgets.ButtonInvisible(rowRect, false))
                {
                    Find.WindowStack.Add(new PersonaEditorWindow(pawn));
                    Event.current.Use(); 
                }
            }

            simulatedCurY += rowHeight;
        }
    }
}
