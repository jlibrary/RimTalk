using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class Overlay : MapComponent
{
    public static bool SuppressForScreenshot = false;
    public static event Action OnLogUpdated;
    public static void NotifyLogUpdated()
    {
        OnLogUpdated?.Invoke();
    }

    private class CachedMessageLine
    {
        // Kept for compatibility with extensions which inspect RimTalk's overlay cache.
        public string PawnName;
        public Pawn PawnInstance;

        public string SpeakerName;
        public string SpeakerLabel;
        public string TargetName;
        public string TargetLabel;
        public Pawn TargetPawnInstance;
        public string RawDialogue;
        public string Dialogue;
        public float LeftBracketWidth;
        public float SpeakerWidth;
        public float DirectionWidth;
        public float TargetWidth;
        public float RightBracketWidth;
        public float NameWidth;
        public float LineHeight;
        public TalkType TalkType;
        public bool IsUserEntered;
        public int ConversationId;
    }

    private bool _isDragging;
    private bool _isResizing;
    private Vector2 _dragStartOffset;
    private bool _showSettingsDropdown;

    private Rect _gearIconScreenRect;
    private Rect _settingsDropdownRect;
    private Rect _dragHandleRect;
    private Rect _localResizeHandleRect;
    private Rect _screenResizeHandleRect;

    private List<CachedMessageLine> _cachedMessagesForLog;
    private bool _isCacheDirty = true;
    private float _statusDotFade;
    private string _lastStatusTooltipKey;

    private const float OptionsBarHeight = 30f;
    private const float ResizeHandleSize = 24f;
    private const float DropdownWidth = 200f;
    private const float DropdownHeight = 350f;
    private const int MaxMessagesInLog = 10;
    private const float TextPadding = 5f;
    private const float MaxNameColumnFraction = 0.45f;
    private const float MinimumDialogueWidth = 120f;
    private const float LineVerticalPadding = 2f;
    private const string LeftBracket = "[";
    private const string RightBracket = "]";

    private static readonly Color AnnounceBgColor = new(0.8f, 0.5f, 0.0f, 0.18f);
    private static readonly Color AnnounceNameColor = new(1.0f, 0.78f, 0.2f);
    private static readonly Color AnnounceTextColor = new(1.0f, 0.92f, 0.65f);
    private static readonly Color UserNameColor = new(1.0f, 0.85f, 0.40f);
    private static readonly Color UserTextColor = new(0.98f, 0.93f, 0.78f);

    public Overlay(Map map) : base(map)
    {
        OnLogUpdated += MarkCacheAsDirty;
    }

    private void MarkCacheAsDirty()
    {
        _isCacheDirty = true;
    }

    private static string ExtractSpeakerName(string combinedName)
    {
        string candidate = TrimOuterBrackets(combinedName);

        int separatorIndex = candidate.IndexOf("->", StringComparison.Ordinal);
        int separatorLength = 2;
        if (separatorIndex < 0)
        {
            separatorIndex = candidate.IndexOf('→');
            separatorLength = 1;
        }

        string speakerName = separatorIndex > 0 && separatorIndex + separatorLength < candidate.Length
            ? candidate[..separatorIndex].Trim()
            : candidate;

        return string.IsNullOrWhiteSpace(speakerName) ? "Unknown" : speakerName;
    }

    private static void SplitParticipantNames(string combinedName, string explicitTargetName,
        out string speakerName, out string targetName)
    {
        speakerName = ExtractSpeakerName(combinedName);
        targetName = null;
    }

    private static string TrimOuterBrackets(string value)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length >= 2 && value[0] == '[' && value[^1] == ']'
            ? value[1..^1].Trim()
            : value;
    }

    private static Pawn FindPawn(string pawnName, TalkRequest talkRequest = null)
    {
        if (string.IsNullOrWhiteSpace(pawnName)) return null;

        return talkRequest?.ResolvePawnState(pawnName)?.Pawn ??
               Cache.GetByName(pawnName)?.Pawn ??
               Find.CurrentMap?.mapPawns?.AllPawns?.FirstOrDefault(p =>
                   p?.Name?.ToStringShort == pawnName) ??
               Find.WorldPawns?.AllPawnsAliveOrDead.FirstOrDefault(p =>
                   p?.Name?.ToStringShort == pawnName);
    }

    private static float CalcRichTextHeight(string text, float width)
    {
        // Verse.Text.CalcHeight strips tags first. Calling the active GUIStyle directly
        // makes measurement honor rich-text styles such as <b> exactly as GUI.Label does.
        return Text.CurFontStyle.CalcHeight(new GUIContent(text ?? string.Empty), Mathf.Max(1f, width));
    }

    private static string ClampSingleLineWithEllipsis(string text, float maxWidth)
    {
        text ??= string.Empty;
        if (Text.CalcSize(text).x <= maxWidth) return text;

        const string ellipsis = "…";
        if (Text.CalcSize(ellipsis).x > maxWidth) return string.Empty;

        int low = 0;
        int high = text.Length;
        int bestLength = 0;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            int safeLength = GetSafeSubstringLength(text, middle);
            string candidate = text[..safeLength].TrimEnd() + ellipsis;
            if (Text.CalcSize(candidate).x <= maxWidth)
            {
                bestLength = safeLength;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return text[..bestLength].TrimEnd() + ellipsis;
    }

    private static int GetSafeSubstringLength(string text, int length)
    {
        if (length > 0 && length < text.Length &&
            char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
        {
            return length - 1;
        }

        return length;
    }

    private static string FitDialogueToHeight(string rawDialogue, float width, float maxHeight)
    {
        rawDialogue ??= string.Empty;
        if (CalcRichTextHeight(rawDialogue, width) <= maxHeight) return rawDialogue;

        const string ellipsis = "…";
        string bestDialogue = ellipsis;
        int low = 0;
        int high = rawDialogue.Length;

        while (low <= high)
        {
            int middle = (low + high) / 2;
            int safeLength = GetSafeSubstringLength(rawDialogue, middle);
            string candidate = rawDialogue[..safeLength].TrimEnd() + ellipsis;

            if (CalcRichTextHeight(candidate, width) <= maxHeight)
            {
                bestDialogue = candidate;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return bestDialogue;
    }

    private static void CalculateParticipantLayout(string speakerName, float maxNameWidth,
        out string speakerLabel, out float leftBracketWidth, out float speakerWidth,
        out float rightBracketWidth, out float nameWidth)
    {
        leftBracketWidth = Text.CalcSize(LeftBracket).x;
        rightBracketWidth = Text.CalcSize(RightBracket).x;

        float availableNameWidth = Mathf.Max(1f, maxNameWidth - leftBracketWidth - rightBracketWidth);
        speakerLabel = ClampSingleLineWithEllipsis(speakerName, availableNameWidth);
        speakerWidth = Text.CalcSize(speakerLabel).x;
        nameWidth = leftBracketWidth + speakerWidth + rightBracketWidth;
    }

    public override void MapRemoved()
    {
        base.MapRemoved();
        OnLogUpdated -= MarkCacheAsDirty;
    }

    private void UpdateAndRecalculateCache()
    {
        var settings = Settings.Get();
        var allRequests = ApiHistory.GetAll().ToList();

        var originalFont = Text.Font;
        var originalAnchor = Text.Anchor;
        var gameFont = GameFont.Small;
        var originalFontSize = Text.fontStyles[(int)gameFont].fontSize;

        try
        {
            Text.Font = gameFont;
            Text.fontStyles[(int)gameFont].fontSize = (int)settings.OverlayFontSize;
            float contentWidth = settings.OverlayRectNonDebug.width - 10f;
            float contentHeight = Mathf.Max(1f, settings.OverlayRectNonDebug.height - 10f);
            float maxNameWidth = Mathf.Max(1f, Mathf.Min(
                contentWidth * MaxNameColumnFraction,
                contentWidth - MinimumDialogueWidth - TextPadding));

            var newCache = new List<CachedMessageLine>();
            var messages = allRequests
                .Where(r => r.SpokenTick > 0)
                .Reverse()
                .OrderByDescending(r => r.SpokenTick)
                .Take(MaxMessagesInLog);

            foreach (var message in messages)
            {
                string speakerName = ExtractSpeakerName(message.Name);

                CalculateParticipantLayout(speakerName, maxNameWidth,
                    out string speakerLabel,
                    out float leftBracketWidth, out float speakerWidth,
                    out float rightBracketWidth, out float nameWidth);

                newCache.Add(new CachedMessageLine
                {
                    PawnName = speakerName,
                    PawnInstance = FindPawn(speakerName, message.TalkRequest),
                    SpeakerName = speakerName,
                    SpeakerLabel = speakerLabel,
                    TargetName = null,
                    TargetLabel = null,
                    TargetPawnInstance = null,
                    RawDialogue = message.Response ?? string.Empty,
                    LeftBracketWidth = leftBracketWidth,
                    SpeakerWidth = speakerWidth,
                    DirectionWidth = 0f,
                    TargetWidth = 0f,
                    RightBracketWidth = rightBracketWidth,
                    NameWidth = nameWidth,
                    TalkType = message.TalkRequest?.TalkType ?? TalkType.Other,
                    IsUserEntered = message.Channel == Channel.User,
                    ConversationId = message.ConversationId
                });
            }

            if (newCache.Count > 0)
            {
                // If aligned name column is enabled, align all rows to the widest name column.
                // Otherwise, let dialogue immediately follow each individual name.
                float maxNameWidthFound = settings.OverlayAlignNameColumn ? newCache.Max(l => l.NameWidth) : 0f;
                float totalHeight = 0f;
                float maxLatestDialogueHeight = Mathf.Max(1f, contentHeight - LineVerticalPadding);

                for (int i = 0; i < newCache.Count; i++)
                {
                    var line = newCache[i];
                    float activeNameWidth = settings.OverlayAlignNameColumn ? maxNameWidthFound : line.NameWidth;
                    float dialogueWidth = Mathf.Max(1f, contentWidth - activeNameWidth - TextPadding);

                    // The newest message gets the whole bubble first. Only truncate that
                    // message when it cannot fit even with every older row omitted.
                    line.Dialogue = i == 0
                        ? FitDialogueToHeight(line.RawDialogue, dialogueWidth, maxLatestDialogueHeight)
                        : line.RawDialogue;

                    float dialogueHeight = CalcRichTextHeight(line.Dialogue, dialogueWidth);
                    float nameHeight = Text.CalcSize(LeftBracket + line.SpeakerLabel + RightBracket).y;
                    line.LineHeight = Mathf.Max(dialogueHeight, nameHeight) + LineVerticalPadding;

                    if (i == 0) line.LineHeight = Mathf.Min(line.LineHeight, contentHeight);

                    totalHeight += line.LineHeight;
                    if (i > 0 && totalHeight > contentHeight)
                    {
                        newCache.RemoveRange(i, newCache.Count - i);
                        break;
                    }
                }

                if (settings.OverlayAlignNameColumn)
                {
                    for (int i = 0; i < newCache.Count; i++)
                    {
                        newCache[i].NameWidth = maxNameWidthFound;
                    }
                }
            }

            _cachedMessagesForLog = newCache;
        }
        finally
        {
            Text.fontStyles[(int)gameFont].fontSize = originalFontSize;
            Text.Font = originalFont;
            Text.Anchor = originalAnchor;
        }

        _isCacheDirty = false;
    }

    public override void MapComponentOnGUI()
    {
        if (SuppressForScreenshot) return;
        if (Current.ProgramState != ProgramState.Playing) return;

        var settings = Settings.Get();
        if (!settings.OverlayEnabled) return;

        ref Rect currentOverlayRect = ref settings.OverlayRectNonDebug;

        if (currentOverlayRect.width <= 0 || currentOverlayRect.height <= 0)
        {
            currentOverlayRect = new Rect(20, 20, 400, 250);
        }

        ClampRectToScreen(ref currentOverlayRect);

        float iconSize = OptionsBarHeight - 4f;
        _dragHandleRect.Set(currentOverlayRect.x, currentOverlayRect.y, currentOverlayRect.width, OptionsBarHeight);
        _gearIconScreenRect.Set(currentOverlayRect.xMax - iconSize - 5f, currentOverlayRect.y + 2f, iconSize, iconSize);

        float dropdownY = _gearIconScreenRect.yMax;

        // Check if the dropdown would go off the bottom of the screen
        if (dropdownY + DropdownHeight > Verse.UI.screenHeight)
        {
            // If so, position it above the gear icon instead
            dropdownY = _gearIconScreenRect.y - DropdownHeight;
        }

        _settingsDropdownRect.Set(
            _gearIconScreenRect.x - DropdownWidth + _gearIconScreenRect.width,
            dropdownY,
            DropdownWidth,
            DropdownHeight
        );

        _screenResizeHandleRect.Set(currentOverlayRect.xMax - ResizeHandleSize,
            currentOverlayRect.yMax - ResizeHandleSize,
            ResizeHandleSize, ResizeHandleSize);

        HandleInput(ref currentOverlayRect);

        bool isMouseOver = Mouse.IsOver(currentOverlayRect);

        GUI.BeginGroup(currentOverlayRect);
        var inRect = new Rect(Vector2.zero, currentOverlayRect.size);

        Widgets.DrawBoxSolid(inRect, new Color(0.1f, 0.1f, 0.1f, settings.OverlayOpacity));

        var contentRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);

        DrawMessageLog(contentRect);

        DrawStatusIndicator(inRect);

        if (isMouseOver)
        {
            var optionsRect = new Rect(inRect.x, inRect.y, inRect.width, OptionsBarHeight);
            DrawOptionsBar(optionsRect);

            _localResizeHandleRect.Set(inRect.width - ResizeHandleSize, inRect.height - ResizeHandleSize,
                ResizeHandleSize, ResizeHandleSize);
            GUI.DrawTexture(_localResizeHandleRect, TexUI.WinExpandWidget);
            TooltipHandler.TipRegion(_localResizeHandleRect, "Drag to resize");
        }
        GUI.EndGroup();

        if (_showSettingsDropdown)
        {
            DrawSettingsDropdown();
        }
    }


    private void DrawStatusIndicator(Rect inRect)
    {
        var settings = Settings.Get();
        if (settings.OverlayIndicatorMode == RimTalkSettings.OverlayIndicatorType.Disabled)
            return;
        if (Event.current.type is not EventType.Repaint) return;

        bool isBusy = AIService.IsBusy();
        int pendingCount = isBusy ? 0 : TalkService.PendingTalksCount;
        bool hasPending = pendingCount > 0;
        bool isActive = isBusy || hasPending;

        float targetFade = isActive ? 1f : 0f;
        _statusDotFade = Mathf.MoveTowards(_statusDotFade, targetFade, Time.unscaledDeltaTime * 6f);

        const int numSegments = 3;
        const float segWidth = 8f;
        const float segHeight = 2.5f;
        const float segGap = 3f;
        float startX = inRect.x;
        float startY = inRect.height - segHeight;

        var hitRect = new Rect(inRect.x, inRect.height - 10f, 36f, 10f);

        if (isBusy)
        {
            _lastStatusTooltipKey = "RimTalk.Overlay.StatusGenerating";
        }
        else if (hasPending)
        {
            _lastStatusTooltipKey = "RimTalk.Overlay.StatusPendingTalks";
        }

        // 1) Always draw 3 dim chassis slots (housing frame)
        Color slotHousingColor = new Color(1f, 1f, 1f, 0.15f);
        for (int i = 0; i < numSegments; i++)
        {
            Rect segRect = new Rect(startX + i * (segWidth + segGap), startY, segWidth, segHeight);
            Widgets.DrawBoxSolid(segRect, slotHousingColor);
        }

        // 2) Draw active glowing lights
        for (int i = 0; i < numSegments; i++)
        {
            Rect segRect = new Rect(startX + i * (segWidth + segGap), startY, segWidth, segHeight);

            if (isBusy)
            {
                // Sine chase wave across the 3 slots
                float time = Time.realtimeSinceStartup * 4.5f;
                float phase = time - i * 1.05f;
                float wave = Mathf.Sin(phase);
                float segAlpha = Mathf.Clamp01(Mathf.Max(0f, wave)) * _statusDotFade;
                if (segAlpha > 0.01f)
                {
                    Widgets.DrawBoxSolid(segRect, new Color(0.35f, 0.70f, 1.0f, segAlpha));
                }
            }
            else if (hasPending)
            {
                // Fixed 3-slot queue buffer: immediate 1:1 visual on talk consume
                if (i < pendingCount)
                {
                    Widgets.DrawBoxSolid(segRect, new Color(0.35f, 0.85f, 0.45f, 0.85f * _statusDotFade));
                }
            }
        }

        if (_statusDotFade > 0.1f && !string.IsNullOrEmpty(_lastStatusTooltipKey))
        {
            string tipText = _lastStatusTooltipKey.Translate();
            if (hasPending)
            {
                tipText += $" ({pendingCount})";
            }
            TooltipHandler.TipRegion(hitRect, tipText);
        }
    }

    private void HandleInput(ref Rect windowRect)
    {
        Event currentEvent = Event.current;

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            if (_showSettingsDropdown)
            {
                if (_settingsDropdownRect.Contains(currentEvent.mousePosition))
                {
                    return;
                }

                if (!_gearIconScreenRect.Contains(currentEvent.mousePosition))
                {
                    _showSettingsDropdown = false;
                    currentEvent.Use();
                    return;
                }
            }

            if (_screenResizeHandleRect.Contains(currentEvent.mousePosition))
            {
                _isResizing = true;
                currentEvent.Use();
            }
            else if (_dragHandleRect.Contains(currentEvent.mousePosition) &&
                     !_gearIconScreenRect.Contains(currentEvent.mousePosition))
            {
                _isDragging = true;
                _dragStartOffset = currentEvent.mousePosition - windowRect.position;
                currentEvent.Use();
            }
        }
        else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
        {
            if (_isDragging || _isResizing)
            {
                Settings.Get().Write();
            }

            _isDragging = false;
            _isResizing = false;
        }
        else if (currentEvent.type == EventType.MouseDrag)
        {
            if (_isResizing)
            {
                float desiredWidth = currentEvent.mousePosition.x - windowRect.x;
                float desiredHeight = currentEvent.mousePosition.y - windowRect.y;

                float maxWidth = Verse.UI.screenWidth - windowRect.x;
                float maxHeight = Verse.UI.screenHeight - windowRect.y;

                windowRect.width = Mathf.Clamp(desiredWidth, 350f, maxWidth);
                windowRect.height = Mathf.Clamp(desiredHeight, 50f, maxHeight);

                _isCacheDirty = true;

                currentEvent.Use();
            }
            else if (_isDragging)
            {
                windowRect.position = currentEvent.mousePosition - _dragStartOffset;
                currentEvent.Use();
            }

            ClampRectToScreen(ref windowRect);
        }
    }

    private void ClampRectToScreen(ref Rect rect)
    {
        rect.x = Mathf.Clamp(rect.x, 0, Verse.UI.screenWidth - rect.width);
        rect.y = Mathf.Clamp(rect.y, 0, Verse.UI.screenHeight - rect.height);
    }

    private void DrawOptionsBar(Rect rect)
    {
        float iconSize = rect.height - 4f;
        var localIconRect = new Rect(rect.width - iconSize - 2f, 2f, iconSize, iconSize);

        var settings = Settings.Get();

        const float minIconOpacity = 0.3f;
        float effectiveOpacity = Mathf.Max(settings.OverlayOpacity, minIconOpacity);

        var iconTexture = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");

        var iconColor = Color.white;
        iconColor.a = effectiveOpacity;

        var mouseoverColor = GenUI.MouseoverColor;
        mouseoverColor.a = effectiveOpacity;

        if (Widgets.ButtonImage(localIconRect, iconTexture, iconColor, mouseoverColor))
        {
            _showSettingsDropdown = !_showSettingsDropdown;
        }

        TooltipHandler.TipRegion(localIconRect, "RimTalk.Overlay.Option".Translate());
    }

    private static bool DrawTinyButtonText(Rect rect, string label)
    {
        bool clicked = Widgets.ButtonText(rect, string.Empty);
        var prevAnchor = Text.Anchor;
        var prevFont = Text.Font;
        Text.Anchor = TextAnchor.MiddleCenter;
        Text.Font = GameFont.Tiny;
        Widgets.Label(rect, label);
        Text.Font = prevFont;
        Text.Anchor = prevAnchor;
        return clicked;
    }

    private void DrawSettingsCheckbox(Listing_Standard listing, string label, bool initialValue, Action<bool> onValueChanged, string tooltipKey = null)
    {
        Text.Font = GameFont.Tiny;
        var rowRect = listing.GetRect(24f);
        if (Mouse.IsOver(rowRect))
        {
            Widgets.DrawHighlight(rowRect);
        }

        if (!string.IsNullOrEmpty(tooltipKey))
        {
            TooltipHandler.TipRegion(rowRect, tooltipKey.Translate());
        }

        bool currentValue = initialValue;
        Widgets.CheckboxLabeled(rowRect, label, ref currentValue);
        if (currentValue != initialValue)
        {
            onValueChanged(currentValue);
        }
    }

    private void DrawSettingsDropdown()
    {
        var settings = Settings.Get();

        Widgets.DrawBoxSolid(_settingsDropdownRect, new Color(0.15f, 0.15f, 0.15f, 0.95f));

        var originalFont = Text.Font;

        try
        {
            var listing = new Listing_Standard();
            listing.Begin(_settingsDropdownRect.ContractedBy(10f));
            Text.Font = GameFont.Tiny;

            DrawSettingsCheckbox(listing, "RimTalk.DebugWindow.EnableRimTalk".Translate(), settings.IsEnabled, value =>
            {
                settings.IsEnabled = value;
                settings.Write();
            }, "RimTalk.Overlay.EnableRimTalkTooltip");

            listing.Gap(6);

            DrawSettingsCheckbox(listing, "RimTalk.Overlay.DrawAboveUI".Translate(), settings.OverlayDrawAboveUI, value =>
            {
                settings.OverlayDrawAboveUI = value;
                settings.Write();
            }, "RimTalk.Overlay.DrawAboveUITooltip");

            listing.Gap(6);

            DrawSettingsCheckbox(listing, "RimTalk.Overlay.ShowGroupColors".Translate(), settings.OverlayShowGroupColors, value =>
            {
                settings.OverlayShowGroupColors = value;
                settings.Write();
            }, "RimTalk.Overlay.ShowGroupColorsTooltip");

            listing.Gap(6);

            DrawSettingsCheckbox(listing, "RimTalk.Overlay.AlignNameColumn".Translate(), settings.OverlayAlignNameColumn, value =>
            {
                settings.OverlayAlignNameColumn = value;
                _isCacheDirty = true;
                settings.Write();
            }, "RimTalk.Overlay.AlignNameColumnTooltip");

            listing.Gap(6);

            bool indicatorEnabled = settings.OverlayIndicatorMode != RimTalkSettings.OverlayIndicatorType.Disabled;
            DrawSettingsCheckbox(listing, "RimTalk.Overlay.StatusIndicator".Translate(), indicatorEnabled, value =>
            {
                settings.OverlayIndicatorMode = value 
                    ? RimTalkSettings.OverlayIndicatorType.BottomLedChase 
                    : RimTalkSettings.OverlayIndicatorType.Disabled;
                settings.Write();
            }, "RimTalk.Overlay.StatusIndicatorTooltip");

            listing.Gap(10);

            Text.Font = GameFont.Tiny;
            listing.Label("RimTalk.Overlay.Opacity".Translate() + ": " + settings.OverlayOpacity.ToString("P0"));
            settings.OverlayOpacity = listing.Slider(settings.OverlayOpacity, 0f, 1.0f);

            Text.Font = GameFont.Tiny;
            listing.Label("RimTalk.Overlay.FontSize".Translate() + ": " + settings.OverlayFontSize.ToString("F0"));
            float newFontSize = listing.Slider(Mathf.Round(settings.OverlayFontSize), 10f, 24f);
            if (Mathf.Round(newFontSize) != Mathf.Round(settings.OverlayFontSize))
            {
                _isCacheDirty = true;
                settings.OverlayFontSize = newFontSize;
            }

            listing.Gap(10);

            Rect buttonRowRect = listing.GetRect(28f);
            const float buttonGap = 4f;
            float buttonWidth = (buttonRowRect.width - buttonGap) / 2f;

            var bubbleSettingsBtnRect = new Rect(buttonRowRect.x, buttonRowRect.y, buttonWidth, buttonRowRect.height);
            var settingsButtonRect = new Rect(bubbleSettingsBtnRect.xMax + buttonGap, buttonRowRect.y, buttonWidth, buttonRowRect.height);

            if (DrawTinyButtonText(bubbleSettingsBtnRect, "RimTalk.BubbleSettings.Button".Translate()))
            {
                Find.WindowStack.Add(new Dialog_BubbleSettings());
                _showSettingsDropdown = false;
            }
            TooltipHandler.TipRegion(bubbleSettingsBtnRect, "RimTalk.BubbleSettings.OpenTooltip".Translate());

            if (DrawTinyButtonText(settingsButtonRect, "RimTalk.DebugWindow.ModSettings".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
                _showSettingsDropdown = false;
            }

            listing.Gap(6);

            Rect bottomRowRect = listing.GetRect(28f);
            var debugRect = new Rect(bottomRowRect.x, bottomRowRect.y, buttonWidth, bottomRowRect.height);
            var turnOffRect = new Rect(debugRect.xMax + buttonGap, bottomRowRect.y, buttonWidth, bottomRowRect.height);

            if (DrawTinyButtonText(debugRect, "RimTalk.Overlay.Debug".Translate()))
            {
                if (!Find.WindowStack.IsOpen<DebugWindow>())
                {
                    Find.WindowStack.Add(new DebugWindow());
                }
                _showSettingsDropdown = false;
            }

            if (DrawTinyButtonText(turnOffRect, "RimTalk.Overlay.TurnOff".Translate()))
            {
                settings.OverlayEnabled = false;
                settings.Write();
                _showSettingsDropdown = false;
            }
            TooltipHandler.TipRegion(turnOffRect, "RimTalk.Overlay.TurnOffTooltip".Translate());

            listing.End();
        }
        finally
        {
            Text.Font = originalFont;
        }
    }


    private static void DrawParticipants(Rect rowRect, CachedMessageLine message)
    {
        float totalPawnLabelWidth = message.LeftBracketWidth + message.SpeakerWidth + message.RightBracketWidth;
        var speakerRect = new Rect(rowRect.x, rowRect.y, totalPawnLabelWidth, rowRect.height);
        UIUtil.DrawClickablePawnName(speakerRect, message.SpeakerLabel, message.PawnInstance, includeBrackets: true);
    }

    private void DrawMessageLog(Rect inRect)
    {
        if (_isCacheDirty)
        {
            UpdateAndRecalculateCache();
        }

        var contentRect = inRect.ContractedBy(5f);
        if (_cachedMessagesForLog == null || _cachedMessagesForLog.Count == 0) return;

        var settings = Settings.Get();
        var originalFont = Text.Font;
        var originalAnchor = Text.Anchor;
        var gameFont = GameFont.Small;
        var originalFontSize = Text.fontStyles[(int)gameFont].fontSize;

        try
        {
            Text.Font = gameFont;
            Text.fontStyles[(int)gameFont].fontSize = (int)settings.OverlayFontSize;
            Text.Anchor = TextAnchor.UpperLeft;

            float currentY = contentRect.yMax;

            for (int i = 0; i < _cachedMessagesForLog.Count; i++)
            {
                var message = _cachedMessagesForLog[i];

                float remainingHeight = currentY - contentRect.y;
                if (i > 0 && message.LineHeight > remainingHeight) break;

                float rowHeight = i == 0
                    ? Mathf.Min(message.LineHeight, remainingHeight)
                    : message.LineHeight;
                currentY -= rowHeight;

                // Left accent bar for conversation groups
                if (settings.OverlayShowGroupColors)
                {
                    Color groupColor = UIUtil.GetConversationColor(message.ConversationId);
                    if (groupColor != Color.clear)
                        Widgets.DrawBoxSolid(new Rect(inRect.x, currentY, 2.5f, rowHeight), groupColor);
                }

                var rowRect = new Rect(contentRect.x, currentY, contentRect.width, rowHeight);
                float dialogueWidth = Mathf.Max(1f, rowRect.width - message.NameWidth - TextPadding);
                var dialogueRect = new Rect(rowRect.x + message.NameWidth + TextPadding, rowRect.y,
                    dialogueWidth, rowRect.height);

                // Only the text that user enters gets highlighted
                if (message.IsUserEntered && message.TalkType == TalkType.Announcement)
                {
                    Widgets.DrawBoxSolid(rowRect, AnnounceBgColor);
                    GUI.color = AnnounceNameColor;
                    DrawParticipants(rowRect, message);
                    GUI.color = AnnounceTextColor;
                    Widgets.Label(dialogueRect, message.Dialogue);
                    GUI.color = Color.white;
                }
                else if (message.IsUserEntered && message.TalkType == TalkType.User)
                {
                    GUI.color = UserNameColor;
                    DrawParticipants(rowRect, message);
                    GUI.color = UserTextColor;
                    Widgets.Label(dialogueRect, message.Dialogue);
                    GUI.color = Color.white;
                }
                else
                {
                    DrawParticipants(rowRect, message);
                    Widgets.Label(dialogueRect, message.Dialogue);
                }
            }
        }
        finally
        {
            Text.fontStyles[(int)gameFont].fontSize = originalFontSize;
            Text.Font = originalFont;
            Text.Anchor = originalAnchor;
        }
    }
}

// ... [OverlayPatch remains unchanged] ...
[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
public static class OverlayPatch
{
    private static bool _skip;
    private static void DrawOverlay(bool isPrefixExecution)
    {
        _skip = !_skip;
        if (_skip) return;
        if (Current.ProgramState != ProgramState.Playing) return;

        var settings = Settings.Get();
        if (settings.OverlayDrawAboveUI == isPrefixExecution) return;
        var mapComp = Find.CurrentMap?.GetComponent<Overlay>();
        mapComp?.MapComponentOnGUI();
    }

    [HarmonyPrefix]
    public static void Prefix()
    {
        DrawOverlay(true);
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        if (Overlay.SuppressForScreenshot)
        {
            return;
        }

        DrawOverlay(false);
    }
}