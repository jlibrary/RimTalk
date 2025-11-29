using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk.UI
{
    public static class UIUtil
    {
        /// <summary>
        /// Draws a pawn's name that is clickable to jump to their location.
        /// The name is color-coded based on the pawn's status (e.g., dead, colonist).
        /// </summary>
        /// <param name="rect">The rectangle area to draw in.</param>
        /// <param name="pawnName">The name of the pawn to display.</param>
        /// <param name="pawn">An optional direct reference to the pawn.</param>
        public static void DrawClickablePawnName(Rect rect, string pawnName, Pawn pawn = null)
        {
            if (pawn != null)
            {
                var originalColor = GUI.color;
                Widgets.DrawHighlightIfMouseover(rect);

                GUI.color =
                    pawn.IsPlayer() ? new Color(1f, 0.75f, 0.8f) :
                    pawn.Dead ? Color.gray :
                    PawnNameColorUtility.PawnNameColorOf(pawn);

                Widgets.Label(rect, $"[{pawnName}]");

                if (Widgets.ButtonInvisible(rect))
                {
                    if (pawn.Dead && pawn.Corpse != null && pawn.Corpse.Spawned)
                    {
                        CameraJumper.TryJump(pawn.Corpse);
                    }
                    else if (!pawn.Dead && pawn.Spawned)
                    {
                        CameraJumper.TryJump(pawn);
                    }
                }

                GUI.color = originalColor;
            }
            else
            {
                Widgets.Label(rect, $"[{pawnName}]");
            }
        }
    }
}