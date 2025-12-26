using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.UI;
using RimTalk.Util;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class PawnGizmoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null) return;
            if (!Settings.Get().AllowCustomConversation) return;
            if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.Disabled) return;
            if (!__instance.Spawned || __instance.Dead) return;
            if (!__instance.IsTalkEligible()) return;

            var selector = Find.Selector;
            if (selector.SelectedPawns.Count != 1) return;

            var list = (__result != null) ? __result.ToList() : new List<Gizmo>();

            var cmd = new Command_Action
            {
                defaultLabel = "RimTalk.Gizmo.ChatWithTarget".Translate(__instance.LabelShort),
                defaultDesc = "RimTalk.Gizmo.ChatWithTargetDesc".Translate(__instance.LabelShort),
                icon = ContentFinder<Texture2D>.Get("UI/ChatGizmo", true),
                action = () =>
                {
                    Pawn player = Cache.GetPlayer();
                    if (player == null) return;
                    Find.WindowStack.Add(new CustomDialogueWindow(player, __instance));
                }
            };

            list.Add(cmd);
            __result = list;
        }
    }
}