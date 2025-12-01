using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class Overlay : MapComponent
{
    public static event Action OnLogUpdated;

    private static readonly Color[] TextColors =
    [
        new(1f, 0.95f, 0.7f),
        new(0.75f, 1f, 0.75f),
        new(1f, 0.75f, 0.85f),
        new(0.7f, 0.85f, 1f),
        new(1f, 0.85f, 0.7f),
        new(0.85f, 0.75f, 1f)
    ];

    public static void NotifyLogUpdated()
    {
        OnLogUpdated?.Invoke();
    }

    private class CachedMessageLine
    {
        public string PawnName;
        public string Dialogue;
        public float NameWidth;
        public float LineHeight;
        public Pawn PawnInstance;
        public Color TextColor;
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
    private const float DropdownHeight = 190f;
    private const int MaxMessagesInLog = 10;

    public Overlay(Map map) : base(map)
    {
        OnLogUpdated += MarkCacheAsDirty;
    }

    private void MarkCacheAsDirty()
    {
        _isCacheDirty = true;
    }

    public override void MapRemoved()
    {
        base.MapRemoved();
        OnLogUpdated -= MarkCacheAsDirty;
    }

    private void UpdateAndRecalculateCache()
    {
        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
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

            // Use the same contraction as when drawing to ensure calculation and drawing are consistent
            // contentRect = inRect.ContractedBy(5f), so width is inRect.width - 10f
            float contentWidth = settings.OverlayRectNonDebug.width - 10f;

            var newCache = new List<CachedMessageLine>();
            var messages = allRequests
                .Where(r => r.SpokenTick > 0)
                .OrderByDescending(r => r.SpokenTick)
                .Take(MaxMessagesInLog);

            foreach (var message in messages)
            {
                string pawnName = message.Name ?? "Unknown";
                string dialogue = message.Response ?? "";
                string formattedName = $"[{pawnName}]";
                
                // Calculate name width
                float nameWidth = Text.CalcSize(formattedName).x;
                
                // Calculate available width for dialogue (subtract name width)
                // Note: Use contentWidth here instead of subtracting safety margin to ensure calculation and drawing use the same width
                float availableDialogueWidth = contentWidth - nameWidth;
                if (availableDialogueWidth < 0)
                {
                    availableDialogueWidth = contentWidth * 0.5f; // If name is too long, at least give dialogue half the space
                }
                
                // Add safety margin to prevent text from being cut off at boundaries (effective for all languages)
                // Use a slightly smaller width when calculating height, making the calculated height more conservative to ensure text is fully displayed
                // This is particularly important for Chinese characters, wide characters (such as Japanese, Korean), and text with certain fonts
                const float safetyMargin = 3f;
                float dialogueWidthForCalc = Mathf.Max(0f, availableDialogueWidth - safetyMargin);
                
                // Calculate dialogue height separately (including the two leading spaces)
                // This method is more accurate than calculating with the full message, as name and dialogue are drawn separately in practice
                string dialogueWithSpaces = "  " + dialogue;
                float dialogueHeight = Text.CalcHeight(dialogueWithSpaces, dialogueWidthForCalc);
                
                // Calculate name height (usually one line, but long names in some languages may require multiple lines)
                float nameHeight = Text.CalcHeight(formattedName, nameWidth);
                
                // Use the larger of the two heights to ensure text is fully displayed
                float lineHeight = Mathf.Max(dialogueHeight, nameHeight);
                
                // Add extra line height buffer to prevent text from being cut off vertically
                // This benefits all languages, especially when line spacing calculations for certain fonts are not precise enough
                lineHeight += 2f;

                var foundPawn = Cache.GetByName(pawnName)?.Pawn ??
                                Find.CurrentMap?.mapPawns?.AllPawns?.FirstOrDefault(p =>
                                    p?.Name?.ToStringShort == pawnName) ??
                                Find.WorldPawns?.AllPawnsAliveOrDead.FirstOrDefault(p =>
                                    p?.Name?.ToStringShort == pawnName);

                newCache.Add(new CachedMessageLine
                {
                    PawnName = pawnName,
                    Dialogue = dialogue,
                    NameWidth = nameWidth,
                    LineHeight = lineHeight,
                    PawnInstance = foundPawn,
                    TextColor = settings.AllowSimultaneousConversations && message.ConversationId >= 0 ? TextColors[message.ConversationId % TextColors.Length] : Color.white
                });
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

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        if (!settings.OverlayEnabled) return;

        ref Rect currentOverlayRect = ref settings.OverlayRectNonDebug;

        if (currentOverlayRect.width <= 0 || currentOverlayRect.height <= 0)
        {
            currentOverlayRect = new Rect(20, 20, 400, 250);
        }

        ClampRectToScreen(ref currentOverlayRect);

        float iconSize = OptionsBarHeight - 4f;
        _dragHandleRect.Set(currentOverlayRect.x, currentOverlayRect.y, currentOverlayRect.width, OptionsBarHeight);
        _gearIconScreenRect.Set(currentOverlayRect.xMax - iconSize - 5f, currentOverlayRect.y + 2f, iconSize,
            iconSize);
        _settingsDropdownRect.Set(_gearIconScreenRect.x - DropdownWidth + _gearIconScreenRect.width,
            _gearIconScreenRect.yMax, DropdownWidth, DropdownHeight);
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
                LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>().Write();
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

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();

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

    private void DrawSettingsCheckbox(Listing_Standard listing, string label, bool initialValue,
        Action<bool> onValueChanged)
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
        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();

        Widgets.DrawBoxSolid(_settingsDropdownRect, new Color(0.15f, 0.15f, 0.15f, 0.95f));

        var listing = new Listing_Standard();
        listing.Begin(_settingsDropdownRect.ContractedBy(10f));

        DrawSettingsCheckbox(listing, "RimTalk.DebugWindow.EnableRimTalk".Translate(), settings.IsEnabled, value =>
        {
            settings.IsEnabled = value;
            settings.Write();
        });

        listing.Gap(6);

        listing.Label("RimTalk.Overlay.Opacity".Translate() + ": " +
                      settings.OverlayOpacity.ToString("P0"));
        settings.OverlayOpacity = listing.Slider(settings.OverlayOpacity, 0f, 1.0f);

        listing.Label("RimTalk.Overlay.FontSize".Translate() + ": " +
                      settings.OverlayFontSize.ToString("F0"));

        float newFontSize = listing.Slider(Mathf.Round(settings.OverlayFontSize), 10f, 24f);
        if (Mathf.Round(newFontSize) != Mathf.Round(settings.OverlayFontSize))
        {
            _isCacheDirty = true;
            settings.OverlayFontSize = newFontSize;
        }


        listing.Gap(12);

        Rect buttonRowRect = listing.GetRect(30f);
        const float buttonGap = 4f;
        float buttonWidth = (buttonRowRect.width - (buttonGap * 2)) / 2f;

        var debugRect = new Rect(buttonRowRect.x, buttonRowRect.y, buttonWidth, buttonRowRect.height);
        var settingsButtonRect = new Rect(debugRect.xMax + buttonGap, buttonRowRect.y, buttonWidth,
            buttonRowRect.height);

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

        listing.End();
    }


    private void DrawMessageLog(Rect inRect)
    {
        if (_isCacheDirty)
        {
            UpdateAndRecalculateCache();
        }

        var contentRect = inRect.ContractedBy(5f);

        if (_cachedMessagesForLog == null || !_cachedMessagesForLog.Any())
            return;

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
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

            foreach (var message in _cachedMessagesForLog)
            {
                currentY -= message.LineHeight;

                if (currentY < contentRect.y) break;

                var rowRect = new Rect(contentRect.x, currentY, contentRect.width, message.LineHeight);

                var nameRect = new Rect(rowRect.x, rowRect.y, message.NameWidth, rowRect.height);
                // Calculate dialogue area width, ensuring consistency with the logic used when calculating height
                // Use actual available width, but not exceeding the width used in calculation (which already includes safety margin)
                float dialogueWidth = rowRect.width - message.NameWidth;
                // Ensure dialogue area does not exceed boundaries
                dialogueWidth = Mathf.Max(0f, dialogueWidth);
                var dialogueRect = new Rect(nameRect.xMax, rowRect.y, dialogueWidth, rowRect.height);

                UIUtil.DrawClickablePawnName(nameRect, message.PawnName, message.PawnInstance);

                // GUI.color = message.TextColor;
                Widgets.Label(dialogueRect, "  " + message.Dialogue);
                // GUI.color = Color.white;
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

[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
public static class OverlayPatch
{
    private static bool _skip;

    static void Postfix()
    {
        if (Current.ProgramState != ProgramState.Playing) return;

        _skip = !_skip;
        if (_skip) return;

        var mapComp = Find.CurrentMap?.GetComponent<Overlay>();
        mapComp?.MapComponentOnGUI();
    }
}