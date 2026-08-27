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
            if (__instance.IsPlayer()) return;

            var selector = Find.Selector;
            if (selector.SelectedPawns.Count != 1) return;

            var list = (__result != null) ? __result.ToList() : new List<Gizmo>();

            // Chat gizmo — player talks to this pawn
            var chatCmd = new Command_Action
            {
                defaultLabel = "RimTalk.Gizmo.ChatWithTarget".Translate(__instance.LabelShort),
                defaultDesc = "RimTalk.Gizmo.ChatWithTargetDesc".Translate(__instance.LabelShort),
                icon = ContentFinder<Texture2D>.Get("UI/ChatGizmo", true),
                action = () =>
                {
                    Pawn player = Cache.GetPlayer();
                    if (player == null) return;
                    Find.WindowStack.Add(new CustomDialogueWindow(player, __instance, DialogueMode.Direct));
                }
            };

            // Announce gizmo — pawn announces to nearby, no player involved
            // Player uses tab-toggle inside the chat window instead
            var announceCmd = new Command_Action
            {
                defaultLabel = "RimTalk.Gizmo.Announce".Translate(),
                defaultDesc = "RimTalk.Gizmo.AnnounceDesc".Translate(__instance.LabelShort),
                icon = ContentFinder<Texture2D>.Get("UI/AnnounceGizmo", true),
                action = () =>
                {
                    Find.WindowStack.Add(new CustomDialogueWindow(__instance, __instance, DialogueMode.Announce));
                }
            };

            list.Add(chatCmd);
            if (Settings.Get().AllowAnnouncement)
            {
                list.Add(announceCmd);
            }
            __result = list;
        }
    }
}
