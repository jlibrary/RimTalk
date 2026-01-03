using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.UI;
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
    
    /// <summary>
    /// Exports the provided API logs to a CSV file located in the user's config folder.
    /// </summary>
    /// <param name="apiLogs">The list of conversation logs to export.</param>
    public static void ExportLogs(List<ApiLog> apiLogs)
    {
        if (apiLogs == null || !apiLogs.Any())
        {
            Messages.Message("No conversations to export.", MessageTypeDefOf.RejectInput, false);
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Pawn,Response,Type,Tokens,ElapsedMs,Prompt,Contexts");

            foreach (var log in apiLogs)
            {
                sb.AppendLine(
                    $"\"{log.Timestamp}\",\"{log.Name}\",\"{log.Response}\",\"{log.InteractionType}\",{log.Payload?.TokenCount},{log.ElapsedMs},\"{log.TalkRequest.Prompt}\",\"{log.TalkRequest.Context}\"");
            }

            string fileName = $"RimTalk_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            File.WriteAllText(path, sb.ToString());

            Messages.Message($"Exported to: {path}", MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to export logs: {ex.Message}");
            Messages.Message("Export failed. Check logs.", MessageTypeDefOf.NegativeEvent, false);
        }
    }
}