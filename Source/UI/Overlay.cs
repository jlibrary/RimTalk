using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class Overlay : MapComponent
{
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

    private const float OptionsBarHeight = 30f;
    private const float ResizeHandleSize = 24f;
    private const float DropdownWidth = 200f;
    private const float DropdownHeight = 255f;
    private const int MaxMessagesInLog = 10;
    private const float TextPadding = 5f;
    private const float MaxNameColumnFraction = 0.45f;
    private const float MinimumDialogueWidth = 120f;
    private const float LineVerticalPadding = 2f;
    private const string LeftBracket = "[";
    private const string Direction = " -> ";
    private const string RightBracket = "]";

    private static bool _externalDialogueFormatterResolved;
    private static MethodInfo _externalDialogueFormatter;

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

    private static string BuildFinalRichText(string text)
    {
        text ??= string.Empty;

        if (!_externalDialogueFormatterResolved)
        {
            _externalDialogueFormatterResolved = true;
            var formatterType = AccessTools.TypeByName("RimTalkDynamicColors.DynamicColorMod");
            var formatter = formatterType == null
                ? null
                : AccessTools.Method(formatterType, "ColorizeString", [typeof(string)]);

            if (formatter is { IsStatic: true } && formatter.ReturnType == typeof(string))
            {
                _externalDialogueFormatter = formatter;
            }
        }

        if (_externalDialogueFormatter == null) return text;

        try
        {
            return _externalDialogueFormatter.Invoke(null, [text]) as string ?? text;
        }
        catch
        {
            // A compatibility formatter must never prevent the overlay from rendering.
            _externalDialogueFormatter = null;
            return text;
        }
    }

    private static void SplitParticipantNames(string combinedName, string explicitTargetName,
        out string speakerName, out string targetName)
    {
        string candidate = TrimOuterBrackets(combinedName);
        targetName = TrimOuterBrackets(explicitTargetName);

        int separatorIndex = candidate.IndexOf("->", StringComparison.Ordinal);
        int separatorLength = 2;
        if (separatorIndex < 0)
        {
            separatorIndex = candidate.IndexOf('→');
            separatorLength = 1;
        }

        if (separatorIndex > 0 && separatorIndex + separatorLength < candidate.Length)
        {
            speakerName = candidate[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(targetName))
            {
                targetName = candidate[(separatorIndex + separatorLength)..].Trim();
            }
        }
        else
        {
            speakerName = candidate;
        }

        if (string.IsNullOrWhiteSpace(speakerName)) speakerName = "Unknown";
        if (string.IsNullOrWhiteSpace(targetName)) targetName = null;
    }

    private static string TrimOuterBrackets(string value)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length >= 2 && value[0] == '[' && value[^1] == ']'
            ? value[1..^1].Trim()
            : value;
    }

    private static Pawn FindPawn(string pawnName)
    {
        if (string.IsNullOrWhiteSpace(pawnName)) return null;

        return Cache.GetByName(pawnName)?.Pawn ??
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
        string fullDialogue = BuildFinalRichText(rawDialogue);
        if (CalcRichTextHeight(fullDialogue, width) <= maxHeight) return fullDialogue;

        const string ellipsis = "…";
        string bestDialogue = BuildFinalRichText(ellipsis);
        int low = 0;
        int high = rawDialogue.Length;

        while (low <= high)
        {
            int middle = (low + high) / 2;
            int safeLength = GetSafeSubstringLength(rawDialogue, middle);
            string candidate = rawDialogue[..safeLength].TrimEnd() + ellipsis;
            string formattedCandidate = BuildFinalRichText(candidate);

            if (CalcRichTextHeight(formattedCandidate, width) <= maxHeight)
            {
                bestDialogue = formattedCandidate;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return bestDialogue;
    }

    private static void CalculateParticipantLayout(string speakerName, string targetName, float maxNameWidth,
        out string speakerLabel, out string targetLabel, out float leftBracketWidth, out float speakerWidth,
        out float directionWidth, out float targetWidth, out float rightBracketWidth, out float nameWidth)
    {
        leftBracketWidth = Text.CalcSize(LeftBracket).x;
        directionWidth = targetName == null ? 0f : Text.CalcSize(Direction).x;
        rightBracketWidth = Text.CalcSize(RightBracket).x;

        float availableNameWidth = Mathf.Max(1f,
            maxNameWidth - leftBracketWidth - directionWidth - rightBracketWidth);
        float naturalSpeakerWidth = Text.CalcSize(speakerName).x;
        float naturalTargetWidth = targetName == null ? 0f : Text.CalcSize(targetName).x;

        float speakerLimit = availableNameWidth;
        float targetLimit = 0f;
        if (targetName != null)
        {
            float halfWidth = availableNameWidth * 0.5f;
            if (naturalSpeakerWidth <= halfWidth)
            {
                speakerLimit = naturalSpeakerWidth;
                targetLimit = availableNameWidth - speakerLimit;
            }
            else if (naturalTargetWidth <= halfWidth)
            {
                targetLimit = naturalTargetWidth;
                speakerLimit = availableNameWidth - targetLimit;
            }
            else
            {
                speakerLimit = halfWidth;
                targetLimit = halfWidth;
            }
        }

        speakerLabel = ClampSingleLineWithEllipsis(speakerName, speakerLimit);
        targetLabel = targetName == null ? null : ClampSingleLineWithEllipsis(targetName, targetLimit);
        speakerWidth = Text.CalcSize(speakerLabel).x;
        targetWidth = targetLabel == null ? 0f : Text.CalcSize(targetLabel).x;
        nameWidth = leftBracketWidth + speakerWidth + directionWidth + targetWidth + rightBracketWidth;
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
            Text.Anchor = TextAnchor.UpperLeft;

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
                SplitParticipantNames(message.Name, message.TargetName, out string speakerName, out string targetName);

                CalculateParticipantLayout(speakerName, targetName, maxNameWidth,
                    out string speakerLabel, out string targetLabel,
                    out float leftBracketWidth, out float speakerWidth, out float directionWidth,
                    out float targetWidth, out float rightBracketWidth, out float nameWidth);

                newCache.Add(new CachedMessageLine
                {
                    PawnName = speakerName,
                    PawnInstance = FindPawn(speakerName),
                    SpeakerName = speakerName,
                    SpeakerLabel = speakerLabel,
                    TargetName = targetName,
                    TargetLabel = targetLabel,
                    TargetPawnInstance = FindPawn(targetName),
                    RawDialogue = message.Response ?? string.Empty,
                    LeftBracketWidth = leftBracketWidth,
                    SpeakerWidth = speakerWidth,
                    DirectionWidth = directionWidth,
                    TargetWidth = targetWidth,
                    RightBracketWidth = rightBracketWidth,
                    NameWidth = nameWidth,
                    TalkType = message.TalkRequest?.TalkType ?? TalkType.Other,
                    IsUserEntered = message.Channel == Channel.User,
                });
            }

            if (newCache.Count > 0)
            {
                // Use one bounded name column for every row. This keeps the dialogue column
                // aligned and prevents long participant names from covering dialogue text.
                float nameColumnWidth = newCache.Max(line => line.NameWidth);
                float dialogueWidth = Mathf.Max(1f, contentWidth - nameColumnWidth - TextPadding);
                float maxLatestDialogueHeight = Mathf.Max(1f, contentHeight - LineVerticalPadding);

                for (int i = 0; i < newCache.Count; i++)
                {
                    var line = newCache[i];
                    line.NameWidth = nameColumnWidth;

                    // The newest message gets the whole bubble first. Only truncate that
                    // message when it cannot fit even with every older row omitted.
                    line.Dialogue = i == 0
                        ? FitDialogueToHeight(line.RawDialogue, dialogueWidth, maxLatestDialogueHeight)
                        : BuildFinalRichText(line.RawDialogue);

                    float dialogueHeight = CalcRichTextHeight(line.Dialogue, dialogueWidth);
                    float nameHeight = Text.CalcSize(LeftBracket + line.SpeakerLabel +
                        (line.TargetLabel == null ? string.Empty : Direction + line.TargetLabel) + RightBracket).y;
                    line.LineHeight = Mathf.Max(dialogueHeight, nameHeight) + LineVerticalPadding;

                    if (i == 0)
                    {
                        line.LineHeight = Mathf.Min(line.LineHeight, contentHeight);
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

    private void DrawSettingsCheckbox(Listing_Standard listing, string label, bool initialValue, Action<bool> onValueChanged)
    {
        bool currentValue = initialValue;
        listing.CheckboxLabeled(label, ref currentValue);
        if (currentValue != initialValue)
        {
            onValueChanged(currentValue);
        }
    }

    private void DrawSettingsDropdown()
    {
        var settings = Settings.Get();

        Widgets.DrawBoxSolid(_settingsDropdownRect, new Color(0.15f, 0.15f, 0.15f, 0.95f));

        var listing = new Listing_Standard();
        listing.Begin(_settingsDropdownRect.ContractedBy(10f));

        DrawSettingsCheckbox(listing, "RimTalk.DebugWindow.EnableRimTalk".Translate(), settings.IsEnabled, value =>
        {
            settings.IsEnabled = value;
            settings.Write();
        });
        
        listing.Gap(6);
        
        bool overlayDrawAboveUI = settings.OverlayDrawAboveUI;
        listing.CheckboxLabeled("RimTalk.Overlay.DrawAboveUI".Translate(), ref overlayDrawAboveUI);
        if (overlayDrawAboveUI != settings.OverlayDrawAboveUI)
        {
            settings.OverlayDrawAboveUI = overlayDrawAboveUI;
            settings.Write();
        }

        listing.Gap(6);

        listing.Label("RimTalk.Overlay.Opacity".Translate() + ": " + settings.OverlayOpacity.ToString("P0"));
        settings.OverlayOpacity = listing.Slider(settings.OverlayOpacity, 0f, 1.0f);

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

        var debugRect = new Rect(buttonRowRect.x, buttonRowRect.y, buttonWidth, buttonRowRect.height);
        var settingsButtonRect = new Rect(debugRect.xMax + buttonGap, buttonRowRect.y, buttonWidth, buttonRowRect.height);

        if (Widgets.ButtonText(debugRect, "RimTalk.Overlay.Debug".Translate()))
        {
            if (!Find.WindowStack.IsOpen<DebugWindow>())
            {
                Find.WindowStack.Add(new DebugWindow());
            }
            _showSettingsDropdown = false;
        }

        if (Widgets.ButtonText(settingsButtonRect, "RimTalk.DebugWindow.ModSettings".Translate()))
        {
            Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
            _showSettingsDropdown = false;
        }

        listing.Gap(6);

        Rect turnOffRect = listing.GetRect(28f);
        if (Widgets.ButtonText(turnOffRect, "RimTalk.Overlay.TurnOff".Translate()))
        {
            settings.OverlayEnabled = false;
            settings.Write();
            _showSettingsDropdown = false;
        }
        TooltipHandler.TipRegion(turnOffRect, "RimTalk.Overlay.TurnOffTooltip".Translate());

        listing.End();
    }

    private static void DrawCachedLabel(Rect rect, string text)
    {
        // Keep Widgets.Label outside DrawMessageLog so compatibility transpilers cannot
        // transform the already measured rich text a second time at the call site.
        Widgets.Label(rect, text);
    }

    private static void DrawParticipants(Rect rowRect, CachedMessageLine message)
    {
        float currentX = rowRect.x;

        DrawCachedLabel(new Rect(currentX, rowRect.y, message.LeftBracketWidth, rowRect.height), LeftBracket);
        currentX += message.LeftBracketWidth;

        var speakerRect = new Rect(currentX, rowRect.y, message.SpeakerWidth, rowRect.height);
        UIUtil.DrawClickablePawnName(speakerRect, message.SpeakerLabel, message.PawnInstance, false);
        currentX += message.SpeakerWidth;

        if (message.TargetName != null)
        {
            DrawCachedLabel(new Rect(currentX, rowRect.y, message.DirectionWidth, rowRect.height), Direction);
            currentX += message.DirectionWidth;

            var targetRect = new Rect(currentX, rowRect.y, message.TargetWidth, rowRect.height);
            UIUtil.DrawClickablePawnName(targetRect, message.TargetLabel, message.TargetPawnInstance, false);
            currentX += message.TargetWidth;
        }

        DrawCachedLabel(new Rect(currentX, rowRect.y, message.RightBracketWidth, rowRect.height), RightBracket);
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
                    DrawCachedLabel(dialogueRect, message.Dialogue);
                    GUI.color = Color.white;
                }
                else if (message.IsUserEntered && message.TalkType == TalkType.User)
                {
                    GUI.color = UserNameColor;
                    DrawParticipants(rowRect, message);
                    GUI.color = UserTextColor;
                    DrawCachedLabel(dialogueRect, message.Dialogue);
                    GUI.color = Color.white;
                }
                else
                {
                    DrawParticipants(rowRect, message);
                    DrawCachedLabel(dialogueRect, message.Dialogue);
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
        DrawOverlay(false);
    }
}
