using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimLife
{
 // Adds developer-only gizmos on pawns to print PawnPro info
 [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
 public static class PawnProGizmoPatch
 {
 public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
 {
 // Only show in Dev Mode and when the pawn is spawned
 if (!Prefs.DevMode || __instance == null || !__instance.Spawned)
 return;

 var list = (__result ?? Enumerable.Empty<Gizmo>()).ToList();

 // Lite
 var lite = new Command_Action
 {
 defaultLabel = "PawnPro: Print Lite (JSON)",
 defaultDesc = "Print PawnPro.ToStringLite() JSON to the log for this pawn.",
 action = () =>
 {
 try
 {
 var pro = new PawnPro(__instance);
 string text = pro.ToStringLite();
 Log.Message($"[RimLife][PawnPro][Lite] {__instance.LabelShort} ({__instance.ThingID}):\n{text}");
 }
 catch (System.Exception ex)
 {
 Log.Warning($"[RimLife][PawnPro] Exception printing Lite for {__instance?.LabelShort}: {ex}");
 }
 }
 };

 // Full
 var full = new Command_Action
 {
 defaultLabel = "PawnPro: Print Full (JSON)",
 defaultDesc = "Print PawnPro.ToStringFull() JSON to the log for this pawn.",
 action = () =>
 {
 try
 {
 var pro = new PawnPro(__instance);
 string text = pro.ToStringFull();
 Log.Message($"[RimLife][PawnPro][Full] {__instance.LabelShort} ({__instance.ThingID}):\n{text}");
 }
 catch (System.Exception ex)
 {
 Log.Warning($"[RimLife][PawnPro] Exception printing Full for {__instance?.LabelShort}: {ex}");
 }
 }
 };

 list.Add(lite);
 list.Add(full);

 __result = list;
 }
 }
}
