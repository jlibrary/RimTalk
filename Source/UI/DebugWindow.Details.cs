using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.PawnMemory;
using RimTalk.Prompt;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Cache = RimTalk.Data.Cache;
using Logger = RimTalk.Util.Logger;
using Object = UnityEngine.Object;
using State = RimTalk.Data.ApiLog.State;

namespace RimTalk.UI;

public partial class DebugWindow
{
    private void DrawDetailsPanel(Rect rect)
    {
        if (_viewMode == DebugViewMode.MemoryLog)
        {
            DrawMemoryLogDetails(rect);
            return;
        }

        Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f, 0.8f));
        InitializeContextStyle();

        var inner = rect.ContractedBy(4f);
        GUI.BeginGroup(inner);

        float y = 0f;

        if (_selectedLog == null)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inner.width, 50f), "RimTalk.DebugWindow.SelectRowHint".Translate());
            GUI.color = Color.white;
            GUI.EndGroup();
            return;
        }

        // Initialize or update temp strings if a new row is selected OR if the selected row is still generating
        bool isGenerating = _selectedLog.Response == null || AIService.IsBusy(); 
        if (_selectedLog.Id != _selectedRequestIdForTemp || isGenerating)
        {
            if (_selectedLog.Id != _selectedRequestIdForTemp)
            {
                _selectedRequestIdForTemp = _selectedLog.Id;
                _expandedPromptSegmentIndices.Clear();
            }
            
            _tempResponse = _selectedLog.Response ?? string.Empty;
            _tempPromptSegments = ResolvePromptSegments(_selectedLog);
            _tempPromptSegmentsText = FormatPromptSegments(_tempPromptSegments);
        }

        var header = new StringBuilder();
        header.Append(_selectedLog.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        header.Append("  |  ");
        header.Append((_selectedLog.Name ?? "-").Trim());
        if (_selectedLog.InteractionType != null)
        {
            header.Append("  |  ");
            header.Append(_selectedLog.InteractionType);
        }

        var payload = ApiHistory.GetPayload(_selectedLog);
        if (!string.IsNullOrEmpty(payload?.Model) && payload.Model != "Canceled")
        {
            var pair = AIProviderRegistry.Defs.FirstOrDefault(d => payload.URL?.StartsWith(d.Value.EndpointUrl) == true);
            string prov = pair.Value.EndpointUrl != null ? pair.Key.GetLabel() : null;
            string tag = string.IsNullOrEmpty(prov) || payload.Model.StartsWith(prov) ? payload.Model : $"{prov} {payload.Model}";
            header.Append("  |  ");
            header.Append(tag.Colorize(new Color(0.70f, 0.73f, 0.77f)));
        }

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(0f, y, inner.width, 24f), header.ToString());
        GUI.color = Color.white;
        y += 24f;

        float buttonsRowH = 24f;
        float btnW = 88f;
        float btnX = 0f;

        // Copy All Button
        if (UIUtil.ButtonText(new Rect(btnX, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyAll".Translate()))
        {
            GUIUtility.systemCopyBuffer = _selectedLog.ToString();
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        // API Log Button
        if (_selectedLog.GetState() != State.None)
        {
            btnX += btnW + 6f;
            Rect reportRect = new Rect(btnX, y, btnW, buttonsRowH);
            GUI.enabled = payload != null;
            if (UIUtil.ButtonText(reportRect, "RimTalk.DebugWindow.ApiLog".Translate()))
            {
                if (payload != null)
                {
                    Find.WindowStack.Add(new Dialog_ApiLog(payload, _selectedLog, _selectedLog.Channel != Channel.User ? Resend : null));
                }
            }
            GUI.enabled = true;
        }

        // Resend Button
        GUI.enabled = _selectedLog.Channel != Channel.User;
        btnX += btnW + 6f;
        var prevResendColor = GUI.color;
        GUI.color = new Color(0.6f, 0.9f, 0.6f);
        Rect resendRect = new Rect(btnX, y, btnW, buttonsRowH);
        if (UIUtil.ButtonText(resendRect, "RimTalk.DebugWindow.Resend".Translate()))
        {
            Resend();
        }

        TooltipHandler.TipRegion(resendRect, "RimTalk.DebugWindow.ResendTooltip".Translate());
        GUI.color = prevResendColor;
        GUI.enabled = true;

        y += buttonsRowH + 8f;

        if (_selectedLog.GetState() == State.Failed)
        {
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.5f, 0.5f);
            string failedMsg = "RimTalk.DebugWindow.FailMsg".Translate();
            Widgets.Label(new Rect(0f, y, inner.width, 20f), failedMsg);
            GUI.color = prevColor;
            y += 30f;
        }

        // Scrollable selectable areas
        var scrollOuter = new Rect(0f, y, inner.width, inner.height - y);
        float blockSpacing = 10f;
        float headerH = 18f;

        UpdateCachedImage();

        // Calculate heights dynamically based on current content
        float viewWidth = scrollOuter.width - 16f;
        float textAreaWidth = viewWidth - 8f;
        float respH = Mathf.Max(40f,
            _monoTinyStyle.CalcHeight(new GUIContent(_tempResponse), textAreaWidth) + 10f);
        float imgH = _cachedImageTexture != null ? 140f : 0f;
        float msgH = CalculatePromptSegmentsHeight(_tempPromptSegments, viewWidth);

        var viewH = headerH + respH + blockSpacing +
                    (imgH > 0f ? imgH + blockSpacing : 0f) +
                    msgH + 10f;

        var view = new Rect(0f, 0f, scrollOuter.width - 16f, viewH);

        Widgets.BeginScrollView(scrollOuter, ref _detailsScrollPosition, view);
        float yy = 0f;

        // Response Block
        DrawSelectableBlock(ref yy, view.width, "RimTalk.DebugWindow.Response".Translate(),
            ref _tempResponse, respH, ControlNameDetailResponse,
            onCopy: () =>
            {
                GUIUtility.systemCopyBuffer = _tempResponse;
                Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            },
            readOnly: true);
        yy += blockSpacing;

        // Attached Image Block
        if (_cachedImageTexture != null)
        {
            DrawImageAttachmentBlock(ref yy, view.width, _cachedImageTexture);
            yy += blockSpacing;
        }

        // Prompt Messages Block (segmented, collapsible)
        DrawPromptMessagesBlock(ref yy, view.width, "RimTalk.DebugWindow.PromptMessages".Translate(),
            _tempPromptSegments, _tempPromptSegmentsText);

        Widgets.EndScrollView();

        GUI.EndGroup();
    }

    private void UpdateCachedImage()
    {
        if (_selectedLog == null)
        {
            ClearCachedImage();
            return;
        }

        string imageBase64 = _selectedLog.TalkRequest?.ImageBase64;

        if (string.IsNullOrEmpty(imageBase64) && _selectedLog.ConversationId >= 0)
        {
            foreach (var item in ApiHistory.GetAll())
            {
                if (item.ConversationId == _selectedLog.ConversationId && !string.IsNullOrEmpty(item.TalkRequest?.ImageBase64))
                {
                    imageBase64 = item.TalkRequest.ImageBase64;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(imageBase64))
        {
            ClearCachedImage();
            return;
        }

        if (_selectedLog.Id != _cachedImageLogId)
        {
            ClearCachedImage();

            try
            {
                byte[] bytes = Convert.FromBase64String(imageBase64);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (tex.LoadImage(bytes))
                {
                    _cachedImageTexture = tex;
                    _cachedImageBase64 = imageBase64;
                    _cachedImageLogId = _selectedLog.Id;
                }
                else
                {
                    Object.Destroy(tex);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to decode ImageBase64 for DebugWindow: {ex.Message}");
            }
        }
    }

    private void ClearCachedImage()
    {
        if (_cachedImageTexture != null)
        {
            Object.Destroy(_cachedImageTexture);
            _cachedImageTexture = null;
        }
        _cachedImageBase64 = null;
        _cachedImageLogId = Guid.Empty;
    }

    private void DrawImageAttachmentBlock(ref float y, float width, Texture2D texture)
    {
        float headerHeight = 18f;
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.4f, 0.8f, 1f);

        string labelText = "RimTalk.DebugWindow.AttachedImage".Translate();
        Vector2 labelSize = Text.CalcSize(labelText);
        Rect labelRect = new Rect(0f, y, labelSize.x, headerHeight);
        Widgets.Label(labelRect, labelText);

        // Draw Copy Icon next to label
        Rect copyRect = new Rect(labelRect.xMax + 8f, y, 16f, 16f);
        if (Widgets.ButtonImage(copyRect, TexButton.Copy))
        {
            if (VisionUtil.CopyImageToClipboard(texture, _cachedImageBase64))
            {
                Messages.Message("RimTalk.DebugWindow.ImageCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            else if (!string.IsNullOrEmpty(_cachedImageBase64))
            {
                GUIUtility.systemCopyBuffer = _cachedImageBase64;
                Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
        }
        TooltipHandler.TipRegion(copyRect, "RimTalk.DebugWindow.CopyImage".Translate());

        GUI.color = Color.white;
        y += headerHeight;

        float imgHeight = 110f;
        float imgWidth = Mathf.Min(width - 8f, imgHeight * ((float)texture.width / Mathf.Max(1, texture.height)));
        var boxRect = new Rect(0f, y, width, imgHeight + 8f);
        Widgets.DrawBoxSolid(boxRect, new Color(0.05f, 0.05f, 0.05f, 0.55f));

        var drawRect = new Rect(4f, y + 4f, imgWidth, imgHeight);
        GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit);

        if (Widgets.ButtonInvisible(boxRect))
        {
            Find.WindowStack.Add(new Dialog_ImageViewer(texture));
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
        TooltipHandler.TipRegion(boxRect, "RimTalk.DebugWindow.ClickToExpandImage".Translate());

        y += imgHeight + 8f;
    }

    private void DrawSelectableBlock(ref float y, float width, string title, ref string content, float contentHeight,
        string controlName, Action onCopy, Action onReset = null, bool readOnly = false)
    {
        // Header Row: Label + Icons
        float headerHeight = 18f;

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;

        Vector2 labelSize = Text.CalcSize(title);
        Rect labelRect = new Rect(0f, y, labelSize.x, headerHeight);
        Widgets.Label(labelRect, title);

        // Draw Copy Icon next to label
        Rect copyRect = new Rect(labelRect.xMax + 8f, y, 16f, 16f);
        if (Widgets.ButtonImage(copyRect, TexButton.Copy))
        {
            onCopy?.Invoke();
        }

        TooltipHandler.TipRegion(copyRect, "RimTalk.DebugWindow.Copy".Translate());

        // Draw Reset Icon next to Copy Icon
        if (onReset != null)
        {
            Rect resetRect = new Rect(copyRect.xMax + 4f, y, 16f, 16f);
            if (Widgets.ButtonImage(resetRect, TexButton.HotReloadDefs))
            {
                onReset.Invoke();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            TooltipHandler.TipRegion(resetRect, "RimTalk.DebugWindow.Undo".Translate());
        }

        GUI.color = Color.white;
        y += headerHeight;

        // Content Box (Editable)
        var box = new Rect(0f, y, width, contentHeight);

        bool isFocused = GUI.GetNameOfFocusedControl() == controlName;
        Color colorUnfocused = new Color(0.05f, 0.05f, 0.05f, 0.55f);
        Color colorFocused = new Color(0.15f, 0.15f, 0.15f, 0.4f);
        Widgets.DrawBoxSolid(box, isFocused && !readOnly ? colorFocused : colorUnfocused);

        var textRect = box.ContractedBy(4f);
        GUI.SetNextControlName(controlName);

        if (readOnly)
            GUI.TextArea(textRect, content, _monoTinyStyle);
        else
            content = GUI.TextArea(textRect, content, _monoTinyStyle);

        y += contentHeight;
    }

    private void DrawPromptMessagesBlock(ref float y, float width, string title,
        List<PromptMessageSegment> segments, string combinedText)
    {
        float headerHeight = 18f;

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;

        Vector2 labelSize = Text.CalcSize(title);
        Rect labelRect = new Rect(0f, y, labelSize.x, headerHeight);
        Widgets.Label(labelRect, title);

        Rect copyRect = new Rect(labelRect.xMax + 8f, y, 16f, 16f);
        if (Widgets.ButtonImage(copyRect, TexButton.Copy))
        {
            GUIUtility.systemCopyBuffer = combinedText ?? "";
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        TooltipHandler.TipRegion(copyRect, "RimTalk.DebugWindow.Copy".Translate());

        GUI.color = Color.white;
        y += headerHeight;

        if (segments == null || segments.Count == 0)
        {
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, width, 20f), "-");
            GUI.color = Color.white;
            y += 20f;
            return;
        }

        const float messageHeaderHeight = 24f;
        const float messageSpacing = 6f;

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            bool isExpanded = _expandedPromptSegmentIndices.Contains(i);
            string preview = GetPromptMessagePreview(segment.Content);
            string roleLabel = GetRoleLabel(segment.Role);
            Color roleColor = GetRoleColor(segment.Role);
            string entryName = string.IsNullOrWhiteSpace(segment.EntryName) ? "Entry" : segment.EntryName;

            var headerRect = new Rect(0f, y, width, messageHeaderHeight);
            bool isMouseOverHeader = Mouse.IsOver(headerRect);

            // Distinct background for expanded vs collapsed (with hover feedback)
            if (isExpanded)
            {
                Widgets.DrawBoxSolid(headerRect, new Color(0.18f, 0.24f, 0.35f, 0.95f));
                // Left accent bar
                Widgets.DrawBoxSolid(new Rect(0f, y, 3.5f, messageHeaderHeight), roleColor);
            }
            else
            {
                Color collapsedBg = isMouseOverHeader
                    ? new Color(0.18f, 0.20f, 0.25f, 0.85f)
                    : new Color(0.12f, 0.13f, 0.16f, 0.70f);
                Widgets.DrawBoxSolid(headerRect, collapsedBg);
            }

            // Copy button for individual segment (on right edge of header)
            Rect copyBtnRect = new Rect(headerRect.xMax - 22f, y + (messageHeaderHeight - 16f) / 2f, 16f, 16f);
            if (Widgets.ButtonImage(copyBtnRect, TexButton.Copy))
            {
                GUIUtility.systemCopyBuffer = segment.Content ?? "";
                Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            TooltipHandler.TipRegion(copyBtnRect, "RimTalk.DebugWindow.Copy".Translate());

            // Header Text components (Single line)
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float currentX = 8f;
            float maxTextRight = copyBtnRect.x - 6f;

            // 1. Index & Entry Name
            string titleText = $"{i + 1}. {entryName}";
            Vector2 titleSize = Text.CalcSize(titleText);
            var titleRect = new Rect(currentX, y, titleSize.x, messageHeaderHeight);
            GUI.color = Color.white;
            Widgets.Label(titleRect, titleText);
            currentX += titleSize.x + 5f;

            // 2. Role Badge
            string roleText = $"({roleLabel})";
            Vector2 roleSize = Text.CalcSize(roleText);
            var roleRect = new Rect(currentX, y, roleSize.x, messageHeaderHeight);
            GUI.color = roleColor;
            Widgets.Label(roleRect, roleText);
            currentX += roleSize.x + 2f;

            // 3. Separator
            string sep = ": ";
            Vector2 sepSize = Text.CalcSize(sep);
            var sepRect = new Rect(currentX, y, sepSize.x, messageHeaderHeight);
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(sepRect, sep);
            currentX += sepSize.x;

            // 4. Preview Text (Single line)
            float previewWidth = Mathf.Max(0f, maxTextRight - currentX);
            if (previewWidth > 10f)
            {
                var previewRect = new Rect(currentX, y, previewWidth, messageHeaderHeight);
                GUI.color = isExpanded ? new Color(0.85f, 0.88f, 0.92f) : new Color(0.70f, 0.73f, 0.77f);
                Widgets.Label(previewRect, preview);
            }

            // Reset text settings
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Header toggle click region (excluding copy button)
            Rect clickRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 24f, messageHeaderHeight);
            if (Widgets.ButtonInvisible(clickRect))
            {
                if (isExpanded)
                    _expandedPromptSegmentIndices.Remove(i);
                else
                    _expandedPromptSegmentIndices.Add(i);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            y += messageHeaderHeight;

            // Expanded Body Section
            if (isExpanded)
            {
                string safeContent = segment.Content ?? "";
                float bodyHeight = Mathf.Max(45f,
                    _monoTinyStyle.CalcHeight(new GUIContent(safeContent), width - 16f) + 14f);
                var bodyRect = new Rect(0f, y, width, bodyHeight);

                // Body background with left role-colored accent line
                Widgets.DrawBoxSolid(bodyRect, new Color(0.06f, 0.07f, 0.09f, 0.90f));
                Widgets.DrawBoxSolid(new Rect(0f, y, 3.5f, bodyHeight), new Color(roleColor.r, roleColor.g, roleColor.b, 0.5f));

                var textRect = new Rect(bodyRect.x + 8f, bodyRect.y + 6f, bodyRect.width - 14f, bodyRect.height - 12f);
                string newContent = GUI.TextArea(textRect, safeContent, _monoTinyStyle);
                if (newContent != safeContent)
                {
                    segment.Content = newContent;
                    _tempPromptSegmentsText = FormatPromptSegments(segments);
                }
                y += bodyHeight;
            }

            y += messageSpacing;
        }
    }

    internal static List<PromptMessageSegment> ResolvePromptSegments(ApiLog log)
    {
        var targetLog = log;
        if (targetLog != null && targetLog.TalkRequest?.PromptMessageSegments == null &&
            targetLog.TalkRequest?.PromptMessages == null && targetLog.ConversationId >= 0)
        {
            foreach (var item in ApiHistory.GetAll())
            {
                if (item.ConversationId == targetLog.ConversationId && item.Channel != Channel.User &&
                    (item.TalkRequest?.PromptMessageSegments != null || item.TalkRequest?.PromptMessages != null))
                {
                    targetLog = item;
                    break;
                }
            }
        }

        var request = targetLog?.TalkRequest;
        string imageBase64 = request?.ImageBase64;
        if (string.IsNullOrEmpty(imageBase64) && log != null && log.ConversationId >= 0)
        {
            foreach (var item in ApiHistory.GetAll())
            {
                if (item.ConversationId == log.ConversationId && !string.IsNullOrEmpty(item.TalkRequest?.ImageBase64))
                {
                    imageBase64 = item.TalkRequest.ImageBase64;
                    break;
                }
            }
        }

        if (request?.PromptMessageSegments != null && request.PromptMessageSegments.Count > 0)
        {
            var result = request.PromptMessageSegments.ToList();
            if (!string.IsNullOrEmpty(imageBase64) && !result.Any(s => s.EntryId == "attached-image-base64"))
            {
                result.Add(new PromptMessageSegment(
                    "attached-image-base64",
                    "RimTalk.DebugWindow.AttachedImageBase64".Translate(),
                    Role.User,
                    $"data:image/jpeg;base64,{imageBase64}"));
            }
            return result;
        }

        if (request?.PromptMessages != null && request.PromptMessages.Count > 0)
        {
            var segments = new List<PromptMessageSegment>();
            for (int i = 0; i < request.PromptMessages.Count; i++)
            {
                var (role, content) = request.PromptMessages[i];
                segments.Add(new PromptMessageSegment($"message-{i}", $"{"RimTalk.DebugWindow.FormatEntry".Translate()} {i + 1}", role, content));
            }

            if (!string.IsNullOrEmpty(imageBase64))
            {
                segments.Add(new PromptMessageSegment(
                    "attached-image-base64",
                    "RimTalk.DebugWindow.AttachedImageBase64".Translate(),
                    Role.User,
                    $"data:image/jpeg;base64,{imageBase64}"));
            }
            return segments;
        }

        var fallbackSegments = new List<PromptMessageSegment>();
        var history = UIUtil.ExtractHistory(request);
        if (!string.IsNullOrEmpty(history))
        {
            fallbackSegments.Add(new PromptMessageSegment(BuiltInPromptIds.ChatHistory, BuiltInPromptNames.ChatHistory, Role.User, history) { IsHistory = true });
        }
        if (!string.IsNullOrEmpty(request?.Context))
        {
            fallbackSegments.Add(new PromptMessageSegment(BuiltInPromptIds.Context, BuiltInPromptNames.Context, Role.System, request.Context));
        }
        if (!string.IsNullOrEmpty(request?.Prompt))
        {
            fallbackSegments.Add(new PromptMessageSegment(BuiltInPromptIds.DialoguePrompt, BuiltInPromptNames.DialoguePrompt, Role.User, request.Prompt));
        }

        if (!string.IsNullOrEmpty(imageBase64))
        {
            fallbackSegments.Add(new PromptMessageSegment(
                "attached-image-base64",
                "RimTalk.DebugWindow.AttachedImageBase64".Translate(),
                Role.User,
                $"data:image/jpeg;base64,{imageBase64}"));
        }

        return fallbackSegments;
    }

    private float CalculatePromptSegmentsHeight(List<PromptMessageSegment> segments, float width)
    {
        float height = 18f;
        if (segments == null || segments.Count == 0)
            return height + 20f;

        const float messageHeaderHeight = 24f;
        const float messageSpacing = 6f;

        for (int i = 0; i < segments.Count; i++)
        {
            height += messageHeaderHeight;
            if (_expandedPromptSegmentIndices.Contains(i))
            {
                string content = segments[i].Content ?? "";
                height += Mathf.Max(45f,
                    _monoTinyStyle.CalcHeight(new GUIContent(content), width - 16f) + 14f);
            }
            height += messageSpacing;
        }

        return height;
    }

    private static string GetPromptMessagePreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "(empty)";

        var firstLine = content.Trim().Replace("\r", "").Split('\n')[0].Trim();
        firstLine = Regex.Replace(firstLine, @"\s+", " ");
        if (firstLine.Length > 90)
            firstLine = firstLine.Substring(0, 87) + "...";

        return string.IsNullOrEmpty(firstLine) ? "(empty)" : firstLine;
    }

    private static string GetRoleLabel(Role role)
    {
        return role == Role.AI ? "Assistant" : role.ToString();
    }

    private static Color GetRoleColor(Role role)
    {
        return role switch
        {
            Role.System => new Color(0.95f, 0.78f, 0.35f),
            Role.User => new Color(0.40f, 0.80f, 1.0f),
            Role.AI => new Color(0.45f, 0.90f, 0.55f),
            _ => new Color(0.85f, 0.85f, 0.85f)
        };
    }

    /// <summary>
    /// Formats prompt segments into a readable string with entry and role headers.
    /// Example output:
    /// Entry: Base Instruction
    /// Role: system
    /// [content]
    /// </summary>
    private static string FormatPromptSegments(List<PromptMessageSegment> segments)
    {
        if (segments == null || segments.Count == 0)
            return $"({"RimTalk.DebugWindow.SelectRowHint".Translate()})";

        var sb = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var roleLabel = segment.Role == Role.AI ? "assistant" : segment.Role.ToString().ToLowerInvariant();
            var entryName = string.IsNullOrWhiteSpace(segment.EntryName) ? "RimTalk.DebugWindow.FormatEntry".Translate().Resolve() : segment.EntryName;
            sb.Append("RimTalk.DebugWindow.FormatEntry".Translate());
            sb.Append(": ");
            sb.AppendLine(entryName);
            sb.Append("RimTalk.DebugWindow.FormatRole".Translate());
            sb.Append(": ");
            sb.AppendLine(roleLabel);
            sb.AppendLine(segment.Content ?? "");
            
            // Add blank line between messages (except after the last one)
            if (i < segments.Count - 1)
                sb.AppendLine();
        }
        return sb.ToString();
    }

    private void Resend()
    {
        if (_selectedLog == null) return;

        TalkRequest debugRequest = _selectedLog.TalkRequest.Clone();
        
        // If we have modified segments in the UI, apply them to the resent request
        if (_tempPromptSegments is { Count: > 0 })
        {
            debugRequest.PromptMessageSegments = _tempPromptSegments.Select(s => new PromptMessageSegment(s.EntryId, s.EntryName, s.Role, s.Content) { IsHistory = s.IsHistory }).ToList();
            debugRequest.PromptMessages = debugRequest.PromptMessageSegments.Select(s => (s.Role, s.Content)).ToList();
        }

        var channel = _selectedLog.Channel;

        // If AI is busy with a background request, cancel it to give priority to this resend
        if (AIService.IsBusy())
        {
            AIService.CancelCurrent();
        }

        // Cancel all pending speech queues across the colony and clear floating bubbles
        foreach (var pawnState in Cache.GetAll())
        {
            pawnState.IgnoreAllTalkResponses();
        }
        SpeechBubbleDrawer.Clear();

        Task.Run(async () =>
        {
            int waited = 0;
            while (AIService.IsBusy() && waited < 2000)
            {
                await Task.Delay(30);
                waited += 30;
            }

            if (channel == Channel.Stream)
                TalkService.GenerateTalkDebug(debugRequest);
            else if (channel == Channel.Query)
                _ = AIService.Query<PersonalityData>(debugRequest);
        });

        Messages.Message("RimTalk.DebugWindow.ResendSuccess".Translate(), MessageTypeDefOf.TaskCompletion);
    }
}
