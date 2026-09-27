using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.UI;

public static class UIUtil
{
    private static readonly Color[] ConversationPalette =
    [
        new Color(0.35f, 0.75f, 0.95f), // Sky Blue
        new Color(0.96f, 0.75f, 0.28f), // Soft Amber
        new Color(0.78f, 0.52f, 0.92f), // Lavender Purple
        new Color(0.32f, 0.85f, 0.58f), // Mint Emerald
        new Color(0.96f, 0.48f, 0.65f), // Soft Rose Pink
        new Color(0.88f, 0.92f, 0.98f)  // Soft Silver White
    ];

    /// <summary>
    /// Gets a texture safely with Unity lifecycle protection, reloading it if destroyed by a language/content change.
    /// </summary>
    public static Texture2D GetTexture(ref Texture2D cache, string path)
    {
        return cache == null ? (cache = ContentFinder<Texture2D>.Get(path, false)) : cache;
    }

    /// <summary>
    /// Returns a distinct accent color for a given conversation ID.
    /// </summary>
    public static Color GetConversationColor(int conversationId, bool fallbackMuted = false)
    {
        if (conversationId < 0)
            return fallbackMuted ? new Color(0.4f, 0.4f, 0.45f, 0.4f) : Color.clear;
        return ConversationPalette[conversationId % ConversationPalette.Length];
    }

    /// <summary>
    /// Draws a pawn's name that is clickable to jump to their location.
    /// The name is color-coded based on the pawn's status (e.g., dead, colonist).
    /// </summary>
    /// <param name="rect">The rectangle area to draw in.</param>
    /// <param name="pawnName">The name of the pawn to display.</param>
    /// <param name="pawn">An optional direct reference to the pawn.</param>
    /// <param name="includeBrackets">Whether to include square brackets around the name.</param>
    public static void DrawClickablePawnName(Rect rect, string pawnName, Pawn pawn = null, bool includeBrackets = true)
    {
        string label = includeBrackets ? $"[{pawnName}]" : pawnName;

        if (pawn != null)
        {
            var originalColor = GUI.color;
            Widgets.DrawHighlightIfMouseover(rect);

            GUI.color =
                pawn.IsPlayer() ? new Color(1f, 0.75f, 0.8f) :
                pawn.Dead ? Color.gray :
                PawnNameColorUtility.PawnNameColorOf(pawn);

            Widgets.Label(rect, label);

            if (Widgets.ButtonInvisible(rect))
            {
                CameraJumper.TryJumpAndSelect(pawn.Dead && pawn.Corpse != null ? pawn.Corpse : pawn);
            }

            GUI.color = originalColor;
        }
        else
        {
            Widgets.Label(rect, label);
        }
    }
    
    /// <summary>
    /// Exports the provided API logs to a CSV file located on the user's desktop.
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
            var categoryColumns = new List<string>();
            var rows = new List<(ApiLog log, Dictionary<string, string> categories)>(apiLogs.Count);

            foreach (var log in apiLogs)
            {
                var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var segments = DebugWindow.ResolvePromptSegments(log);
                if (segments != null)
                {
                    foreach (var seg in segments)
                    {
                        if (IsExcludedExportEntry(seg)) continue;

                        string name = !string.IsNullOrWhiteSpace(seg.EntryName) ? seg.EntryName.Trim() : "Entry";
                        if (!categoryColumns.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            categoryColumns.Add(name);
                        }

                        if (!string.IsNullOrEmpty(seg.Content))
                        {
                            if (categoryMap.TryGetValue(name, out var existing))
                                categoryMap[name] = existing + "\n" + seg.Content;
                            else
                                categoryMap[name] = seg.Content;
                        }
                    }
                }

                rows.Add((log, categoryMap));
            }

            var sb = new StringBuilder();

            // Header
            var headers = new List<string>
            {
                "CreatedTime",
                "SpokenTime",
                "ConversationId",
                "Pawn",
                "Target",
                "Response",
                "State",
                "TalkType",
                "InteractionType",
                "Model",
                "Tokens",
                "ElapsedMs"
            };

            foreach (var cat in categoryColumns)
            {
                headers.Add($"\"{EscapeCsv(cat)}\"");
            }

            sb.AppendLine(string.Join(",", headers));

            // Rows
            foreach (var (log, categoryMap) in rows)
            {
                var createdTime = (log.TalkRequest != null ? log.TalkRequest.CreatedTime : log.Timestamp).ToString("yyyy-MM-dd HH:mm:ss");
                var spokenTime = log.SpokenTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                var target = log.TargetName;
                if (string.IsNullOrEmpty(target) && log.TalkRequest != null)
                {
                    var recipient = log.TalkRequest.Recipient?.LabelShort;
                    var initiator = log.TalkRequest.Initiator?.LabelShort;
                    if (!string.IsNullOrEmpty(recipient) && recipient != log.Name)
                        target = recipient;
                    else if (!string.IsNullOrEmpty(initiator) && initiator != log.Name)
                        target = initiator;
                    else
                        target = "";
                }
                target ??= "";
                var state = log.GetState().ToString();
                var talkType = log.TalkRequest?.TalkType.ToString() ?? "";
                var payload = ApiHistory.GetPayload(log) ?? log.Payload;
                var model = payload?.Model ?? "";
                var tokenCount = payload?.TokenCount.ToString() ?? "";

                var rowValues = new List<string>
                {
                    $"\"{EscapeCsv(createdTime)}\"",
                    $"\"{EscapeCsv(spokenTime)}\"",
                    log.ConversationId.ToString(),
                    $"\"{EscapeCsv(log.Name)}\"",
                    $"\"{EscapeCsv(target)}\"",
                    $"\"{EscapeCsv(log.Response)}\"",
                    $"\"{EscapeCsv(state)}\"",
                    $"\"{EscapeCsv(talkType)}\"",
                    $"\"{EscapeCsv(log.InteractionType)}\"",
                    $"\"{EscapeCsv(model)}\"",
                    tokenCount,
                    log.ElapsedMs.ToString()
                };

                foreach (var cat in categoryColumns)
                {
                    categoryMap.TryGetValue(cat, out var content);
                    rowValues.Add($"\"{EscapeCsv(content ?? "")}\"");
                }

                sb.AppendLine(string.Join(",", rowValues));
            }

            string fileName = $"RimTalk_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

            Messages.Message($"Exported to: {path}", MessageTypeDefOf.TaskCompletion, false);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to export logs: {ex.Message}");
            Messages.Message("Export failed. Check logs.", MessageTypeDefOf.NegativeEvent, false);
        }
    }

    private static bool IsExcludedExportEntry(PromptMessageSegment segment)
    {
        if (segment == null) return true;

        var id = segment.EntryId;
        if (!string.IsNullOrEmpty(id))
        {
            if (string.Equals(id, BuiltInPromptIds.BaseInstruction, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, BuiltInPromptIds.JsonFormat, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, BuiltInPromptIds.FormatReminder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var name = segment.EntryName;
        if (!string.IsNullOrEmpty(name))
        {
            if (name.IndexOf(BuiltInPromptNames.BaseInstruction, StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf(BuiltInPromptNames.JsonFormat, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string EscapeCsv(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\"", "\"\"");
    }

    internal static string ExtractHistory(TalkRequest request)
    {
        if (request == null) return "";

        if (request.PromptMessageSegments != null)
        {
            var historySegments = new List<string>();
            foreach (var s in request.PromptMessageSegments)
            {
                if ((s.IsHistory ||
                     string.Equals(s.EntryId, BuiltInPromptIds.ChatHistory, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(s.EntryName, BuiltInPromptNames.ChatHistory, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(s.EntryName, "History", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(s.Content))
                {
                    historySegments.Add(s.Content);
                }
            }

            if (historySegments.Count > 0)
                return string.Join("\n", historySegments);
        }

        if (request.PromptMessages != null)
        {
            foreach (var (_, content) in request.PromptMessages)
            {
                if (content != null && content.StartsWith(Constant.ChatHistoryHeader))
                    return content;
            }
        }

        return "";
    }

    /// <summary>
    /// Draws a left-aligned checkbox with an attached label, full-row mouseover highlight, and unified row clicking.
    /// </summary>
    public static bool CheckboxLabeledLeft(Rect rect, string label, ref bool checkOn, string tooltip = null, bool disabled = false, Color? labelColor = null)
    {
        Widgets.DrawHighlightIfMouseover(rect);
        if (!string.IsNullOrEmpty(tooltip))
        {
            TooltipHandler.TipRegion(rect, tooltip);
        }

        const float checkSize = 24f;
        Vector2 checkPos = new Vector2(rect.x, rect.y + (rect.height - checkSize) / 2f);
        Widgets.CheckboxDraw(checkPos.x, checkPos.y, checkOn, disabled, checkSize);

        TextAnchor prevAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Rect labelRect = new Rect(rect.x + checkSize + 6f, rect.y, rect.width - checkSize - 6f, rect.height);
        var prevColor = GUI.color;
        if (labelColor.HasValue) GUI.color = labelColor.Value;
        Widgets.Label(labelRect, label);
        GUI.color = prevColor;
        Text.Anchor = prevAnchor;

        if (!disabled && Widgets.ButtonInvisible(rect))
        {
            checkOn = !checkOn;
            if (checkOn)
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Draws a left-aligned multi-state checkbox with an attached label, full-row mouseover highlight, and unified row clicking.
    /// </summary>
    public static MultiCheckboxState CheckboxMultiLabeledLeft(Rect rect, string label, MultiCheckboxState state, Color? labelColor = null, bool disabled = false)
    {
        Widgets.DrawHighlightIfMouseover(rect);

        const float checkSize = 24f;
        Rect checkRect = new Rect(rect.x, rect.y + (rect.height - checkSize) / 2f, checkSize, checkSize);

        MultiCheckboxState newState = Widgets.CheckboxMulti(checkRect, state, disabled);

        TextAnchor prevAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Rect labelRect = new Rect(rect.x + checkSize + 6f, rect.y, rect.width - checkSize - 6f, rect.height);
        var prevColor = GUI.color;
        if (labelColor.HasValue) GUI.color = labelColor.Value;
        Widgets.Label(labelRect, label);
        GUI.color = prevColor;
        Text.Anchor = prevAnchor;

        if (!disabled && Widgets.ButtonInvisible(labelRect))
        {
            newState = state == MultiCheckboxState.On ? MultiCheckboxState.Off : MultiCheckboxState.On;
            if (newState == MultiCheckboxState.On)
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
        }

        return newState;
    }

    /// <summary>
    /// Listing_Standard extension for left-aligned checkbox with full-row highlight and clicking.
    /// </summary>
    public static bool CheckboxLabeledLeft(this Listing_Standard listing, string label, ref bool checkOn, string tooltip = null, bool disabled = false, Color? labelColor = null, float height = 24f)
    {
        Rect rect = listing.GetRect(height);
        return CheckboxLabeledLeft(rect, label, ref checkOn, tooltip, disabled, labelColor);
    }

    /// <summary>
    /// Extension/helper for Widgets.ButtonText that ensures a click sound is played on click.
    /// </summary>
    public static bool ButtonText(Rect rect, string label, bool drawBackground = true, bool doMouseoverSound = true, bool active = true, TextAnchor? overrideTextAnchor = null)
    {
        if (Widgets.ButtonText(rect, label, drawBackground, doMouseoverSound, active, overrideTextAnchor))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            return true;
        }
        return false;
    }
}

