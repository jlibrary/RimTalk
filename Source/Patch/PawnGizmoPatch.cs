using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using Verse;

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

            // Only add for pawns that are on-map and alive
            if (!__instance.Spawned || __instance.Dead) return;

            // Target must be eligible to talk
            if (!PawnUtil.IsTalkEligible(__instance)) return;

            var list = (__result != null) ? __result.ToList() : new List<Gizmo>();

            var cmd = new Command_Action
            {
                defaultLabel = "RimTalk.Gizmo.ChatWithTarget".Translate(__instance.LabelShort),
                defaultDesc = "RimTalk.Gizmo.ChatWithTargetDesc".Translate(),
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/ChatGizmo", true),
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
