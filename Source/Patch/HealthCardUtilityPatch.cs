using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(HealthCardUtility), "EntryClicked")]
public static class HealthCardUtilityPatch
{
    public static bool Prefix(IEnumerable<Hediff> diffs, Pawn pawn)
    {
        if (diffs == null || diffs.All(h => h.def != Constant.VocalLinkDef)) return true;
        if (pawn == null) return false;
        Find.WindowStack.Add(new PersonaEditorWindow(pawn));
                
        if (Event.current != null) Event.current.Use();

        return false;
    }
}

// Fallback for health overhaul mods that do not call EntryClicked.
[HarmonyPatch(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion), typeof(Rect), typeof(TipSignal))]
public static class HealthCardTooltipPatch
{
    public static void Prefix(Rect rect, TipSignal tip)
    {
        if (Event.current?.type != EventType.MouseDown || !Mouse.IsOver(rect)) return;

        Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
        if (pawn == null || !pawn.HasVocalLink()) return;

        string label = Constant.VocalLinkDef?.LabelCap;
        if (label != null && (tip.textGetter?.Invoke() ?? tip.text)?.Contains(label) == true)
        {
            Find.WindowStack.Add(new PersonaEditorWindow(pawn));
            Event.current.Use();
        }
    }
}