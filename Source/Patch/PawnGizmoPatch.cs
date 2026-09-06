using System.Collections.Generic;
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
        private static Texture2D _chatGizmoIcon, _announceGizmoIcon;
        private static Texture2D ChatGizmoIcon => UIUtil.GetTexture(ref _chatGizmoIcon, "UI/ChatGizmo");
        private static Texture2D AnnounceGizmoIcon => UIUtil.GetTexture(ref _announceGizmoIcon, "UI/AnnounceGizmo");

        private static TaggedString CachedAnnounceLabel => "RimTalk.Gizmo.Announce".Translate();

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null) return;

            var selector = Find.Selector;
            if (selector.SelectedPawns.Count != 1 || selector.SingleSelectedThing != __instance) return;

            var settings = Settings.Get();
            if (!settings.AllowCustomConversation) return;
            if (settings.PlayerDialogueMode == Settings.PlayerDialogueMode.Disabled) return;
            if (!__instance.Spawned || __instance.Dead) return;
            if (__instance.IsPlayer()) return;
            if (!__instance.IsTalkEligible()) return;

            __result = AddRimTalkGizmos(__result, __instance, settings.AllowAnnouncement);
        }

        private static IEnumerable<Gizmo> AddRimTalkGizmos(IEnumerable<Gizmo> original, Pawn targetPawn, bool allowAnnouncement)
        {
            if (original != null)
            {
                foreach (var gizmo in original)
                {
                    yield return gizmo;
                }
            }

            // Chat gizmo — player talks to this pawn
            yield return new Command_Action
            {
                defaultLabel = "RimTalk.Gizmo.ChatWithTarget".Translate(targetPawn.LabelShort),
                defaultDesc = "RimTalk.Gizmo.ChatWithTargetDesc".Translate(targetPawn.LabelShort),
                icon = ChatGizmoIcon,
                action = () =>
                {
                    Pawn player = Cache.GetPlayer();
                    if (player == null) return;
                    Find.WindowStack.Add(new CustomDialogueWindow(player, targetPawn, DialogueMode.Direct));
                }
            };

            // Announce gizmo — pawn announces to nearby, no player involved
            if (allowAnnouncement)
            {
                yield return new Command_Action
                {
                    defaultLabel = CachedAnnounceLabel,
                    defaultDesc = "RimTalk.Gizmo.AnnounceDesc".Translate(targetPawn.LabelShort),
                    icon = AnnounceGizmoIcon,
                    action = () =>
                    {
                        Find.WindowStack.Add(new CustomDialogueWindow(targetPawn, targetPawn, DialogueMode.Announce));
                    }
                };
            }
        }
    }
}
