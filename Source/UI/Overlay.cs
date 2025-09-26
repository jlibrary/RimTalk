using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class Overlay : MapComponent
{
    private bool _isDragging;
    private bool _isResizing;
    private Vector2 _dragStartOffset;
    private bool _showSettingsDropdown;
    private Vector2 _tableScrollPosition;

    private Rect _gearIconScreenRect;
    private Rect _settingsDropdownRect;
    private Rect _dragHandleRect;
    private Rect _localResizeHandleRect;
    private Rect _screenResizeHandleRect;

    private bool _groupingEnabled;
    private bool _debugModeEnabled;
    private readonly List<string> _expandedPawns;

    private string _cachedAiStatus;
    private long _cachedTotalCalls;
    private long _cachedTotalTokens;
    private double _cachedAvgCallsPerMin;
    private double _cachedAvgTokensPerMin;
    private double _cachedAvgTokensPerCall;
    private List<PawnState> _cachedPawnStates;
    private List<ApiLog> _cachedRequests;
    private Dictionary<string, List<ApiLog>> _cachedTalkLogsByPawn = new();
    private IEnumerable<ApiLog> _cachedMessagesForLog;

    private int _ticksSinceLastUpdate;
    private const int UpdateIntervalTicks = 30;

    private const float ColumnPadding = 10f;
    private const float OptionsBarHeight = 30f;
    private const float ResizeHandleSize = 24f;
    private const float DropdownWidth = 200f;
    private const float DropdownHeight = 270f;
    private const int MaxMessagesInLog = 10;

    private readonly string _generating = "RimTalk.DebugWindow.Generating".Translate();
    private const float TimestampColumnWidth = 80f;
    private const float PawnColumnWidth = 80f;
    private const float TimeColumnWidth = 80f;
    private const float TokensColumnWidth = 80f;
    private const float CopyAreaWidth = 10f;
    private const float GroupedPawnNameWidth = 80f;
    private const float GroupedRequestsWidth = 80f;
    private const float GroupedLastTalkWidth = 80f;
    private const float GroupedChattinessWidth = 80f;
    private const float GroupedExpandIconWidth = 25f;
    private const float GroupedStatusWidth = 80f;
    private const float DebugBottomSectionHeight = 150f;
    private const float DebugGraphStatsSpacing = 10f;

    public Overlay(Map map) : base(map)
    {
        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        _groupingEnabled = settings.DebugGroupingEnabled;
        _debugModeEnabled = settings.DebugModeEnabled;
        _expandedPawns = settings.DebugExpandedPawns ?? new List<string>();

        UpdateCachedData();
    }

    public override void MapComponentTick()
    {
        base.MapComponentTick();
        _ticksSinceLastUpdate++;
        if (_ticksSinceLastUpdate >= UpdateIntervalTicks)
        {
            UpdateCachedData();
            _ticksSinceLastUpdate = 0;
        }
    }

    private void UpdateCachedData()
    {
        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        _cachedAiStatus = !settings.IsEnabled
            ? "RimTalk.DebugWindow.StatusDisabled".Translate()
            : (AIService.IsBusy()
                ? "RimTalk.DebugWindow.StatusProcessing".Translate()
                : "RimTalk.DebugWindow.StatusIdle".Translate());

        _cachedTotalCalls = Stats.TotalCalls;
        _cachedTotalTokens = Stats.TotalTokens;
        _cachedAvgCallsPerMin = Stats.AvgCallsPerMinute;
        _cachedAvgTokensPerMin = Stats.AvgTokensPerMinute;
        _cachedAvgTokensPerCall = Stats.AvgTokensPerCall;
        _cachedPawnStates = Cache.GetAll().ToList();
        _cachedRequests = ApiHistory.GetAll().ToList();

        _cachedTalkLogsByPawn = _cachedRequests.Where(r => r.Name != null)
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (_groupingEnabled)
        {
            _cachedMessagesForLog = _cachedTalkLogsByPawn
                .Select(kvp => kvp.Value.LastOrDefault(log => log.IsSpoken))
                .Where(log => log != null)
                .OrderByDescending(log => log.Timestamp)
                .Take(MaxMessagesInLog)
                .ToList();
        }
        else
        {
            _cachedMessagesForLog = _cachedRequests
                .Where(r => r.IsSpoken)
                .Reverse()
                .Take(MaxMessagesInLog)
                .ToList();
        }
    }

    public override void MapComponentOnGUI()
    {
        if (Current.ProgramState != ProgramState.Playing) return;

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        if (!settings.OverlayEnabled) return;

        ref Rect currentOverlayRect =
            ref (_debugModeEnabled ? ref settings.OverlayRectDebug : ref settings.OverlayRectNonDebug);

        if (currentOverlayRect.width <= 0 || currentOverlayRect.height <= 0)
        {
            currentOverlayRect = _debugModeEnabled
                ? new Rect(20, 20, 600, 450)
                : new Rect(20, 20, 400, 250);
        }

        ClampRectToScreen(ref currentOverlayRect);

        float iconSize = OptionsBarHeight - 4f;
        _dragHandleRect.Set(currentOverlayRect.x, currentOverlayRect.y, currentOverlayRect.width, OptionsBarHeight);
        _gearIconScreenRect.Set(currentOverlayRect.xMax - iconSize - 5f, currentOverlayRect.y + 2f, iconSize, iconSize);
        _settingsDropdownRect.Set(_gearIconScreenRect.x - DropdownWidth + _gearIconScreenRect.width,
            _gearIconScreenRect.yMax, DropdownWidth, DropdownHeight);
        _screenResizeHandleRect.Set(currentOverlayRect.xMax - ResizeHandleSize,
            currentOverlayRect.yMax - ResizeHandleSize,
            ResizeHandleSize, ResizeHandleSize);

        HandleInput(ref currentOverlayRect);

        GUI.BeginGroup(currentOverlayRect);
        var inRect = new Rect(Vector2.zero, currentOverlayRect.size);

        Widgets.DrawBoxSolid(inRect, new Color(0.1f, 0.1f, 0.1f, settings.OverlayOpacity));

        var contentRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);

        if (_debugModeEnabled)
        {
            DrawDebugContent(contentRect);
        }
        else
        {
            DrawMessageLog(contentRect);
        }

        var optionsRect = new Rect(inRect.x, inRect.y, inRect.width, OptionsBarHeight);
        DrawOptionsBar(optionsRect);

        _localResizeHandleRect.Set(inRect.width - ResizeHandleSize, inRect.height - ResizeHandleSize,
            ResizeHandleSize, ResizeHandleSize);
        GUI.DrawTexture(_localResizeHandleRect, TexUI.WinExpandWidget);
        TooltipHandler.TipRegion(_localResizeHandleRect, "Drag to resize");

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
        var localIconRect = new Rect(rect.width - iconSize, 0f, iconSize, iconSize);

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

        TooltipHandler.TipRegion(localIconRect, "RimTalk.DebugWindow.Option".Translate());
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

        DrawSettingsCheckbox(listing, "RimTalk.DebugWindow.GroupByPawn".Translate(), _groupingEnabled, value =>
        {
            _groupingEnabled = value;
            settings.DebugGroupingEnabled = value;
            settings.Write();
            UpdateCachedData();
        });

        DrawSettingsCheckbox(listing, "RimTalk.DebugWindow.DebugMode".Translate(), _debugModeEnabled, value =>
        {
            _debugModeEnabled = value;
            settings.DebugModeEnabled = value;
            settings.Write();
            _showSettingsDropdown = false;
        });

        listing.Gap(6);

        listing.Label("RimTalk.DebugWindow.OverlayOpacity".Translate() + ": " +
                      settings.OverlayOpacity.ToString("P0"));
        settings.OverlayOpacity = listing.Slider(settings.OverlayOpacity, 0f, 1.0f);

        listing.Label("RimTalk.DebugWindow.OverlayFontSize".Translate() + ": " +
                      settings.OverlayFontSize.ToString("F0"));
        settings.OverlayFontSize = listing.Slider(settings.OverlayFontSize, 10f, 24f);

        listing.Gap(12);

        if (listing.ButtonText("RimTalk.DebugWindow.ModSettings".Translate()))
        {
            Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
            _showSettingsDropdown = false;
        }

        if (listing.ButtonText("RimTalk.DebugWindow.CloseOverlay".Translate()))
        {
            settings.OverlayEnabled = false;
            settings.Write();
            _showSettingsDropdown = false;
        }

        listing.End();
    }

    private void DrawDebugContent(Rect inRect)
    {
        var tableRect = new Rect(inRect.x, inRect.y, inRect.width,
            inRect.height - DebugBottomSectionHeight - DebugGraphStatsSpacing);
        var bottomRect = new Rect(inRect.x, tableRect.yMax + DebugGraphStatsSpacing, inRect.width,
            DebugBottomSectionHeight);
        var graphRect = new Rect(bottomRect.x, bottomRect.y,
            bottomRect.width * 0.55f - (DebugGraphStatsSpacing / 2), bottomRect.height);
        var statsRect = new Rect(graphRect.xMax + DebugGraphStatsSpacing, bottomRect.y,
            bottomRect.width * 0.45f - (DebugGraphStatsSpacing / 2), bottomRect.height);

        if (_groupingEnabled)
            DrawGroupedPawnTable(tableRect);
        else
            DrawUngroupedRequestTable(tableRect);

        DrawGraph(graphRect);
        DrawStatsSection(statsRect);
    }

    private void DrawMessageLog(Rect inRect)
    {
        DrawMessageLogInternal(inRect, _cachedMessagesForLog);
    }

    private void DrawMessageLogInternal(Rect inRect, IEnumerable<ApiLog> messagesToDraw)
    {
        var contentRect = inRect.ContractedBy(5f);

        if (messagesToDraw == null || !messagesToDraw.Any())
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

            foreach (var message in messagesToDraw)
            {
                string pawnName = message.Name ?? "Unknown";
                string dialogue = message.Response ?? "";
                string formattedName = $"[{pawnName}]";
                string fullMessage = $"{formattedName} {dialogue}";

                float rowCalculatedHeight = Text.CalcHeight(fullMessage, contentRect.width);
                currentY -= rowCalculatedHeight;

                if (currentY < contentRect.y) break;

                var rowRect = new Rect(contentRect.x, currentY, contentRect.width, rowCalculatedHeight);

                float nameWidth = Text.CalcSize(formattedName).x;
                var nameRect = new Rect(rowRect.x, rowRect.y, nameWidth, rowRect.height);
                var dialogueRect = new Rect(nameRect.xMax, rowRect.y, rowRect.width - nameWidth,
                    rowRect.height);

                DrawClickablePawnName(nameRect, pawnName);

                Widgets.Label(dialogueRect, "  " + dialogue);
            }
        }
        finally
        {
            Text.fontStyles[(int)gameFont].fontSize = originalFontSize;
            Text.Font = originalFont;
            Text.Anchor = originalAnchor;
        }
    }

    private void DrawClickablePawnName(Rect rect, string pawnName, Pawn pawnInstance = null)
    {
        var pawn = pawnInstance;
        if (pawn == null)
        {
            pawn = Cache.GetByName(pawnName)?.Pawn;
            if (pawn == null)
            {
                pawn = Find.WorldPawns?.AllPawnsAliveOrDead.FirstOrDefault(p => p?.Name?.ToStringShort == pawnName);
            }
        }

        if (pawn != null)
        {
            var originalColor = GUI.color;
            Widgets.DrawHighlightIfMouseover(rect);

            GUI.color = pawn.Dead ? Color.gray : PawnNameColorUtility.PawnNameColorOf(pawn);

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
            Widgets.Label(rect, pawnName);
        }
    }

    private void DrawStatsSection(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

        Text.Font = GameFont.Small;
        GUI.BeginGroup(rect);

        const float rowHeight = 22f;
        const float labelWidth = 120f;
        float currentY = 10f;

        var contentRect = rect.AtZero().ContractedBy(10f);

        Color statusColor;
        var aiStatus = _cachedAiStatus.Translate();
        if (aiStatus == "RimTalk.DebugWindow.StatusProcessing".Translate()) statusColor = Color.yellow;
        else if (aiStatus == "RimTalk.DebugWindow.StatusIdle".Translate()) statusColor = Color.green;
        else statusColor = Color.grey;


        var statusRowRect = new Rect(contentRect.x, currentY, contentRect.width, rowHeight);
        var statusLabelRect = statusRowRect.LeftPartPixels(labelWidth);
        var statusValueRect = new Rect(statusLabelRect.xMax, currentY, 100f, rowHeight);

        GUI.color = Color.gray;
        Widgets.Label(statusLabelRect, "RimTalk.DebugWindow.AIStatus".Translate());
        GUI.color = statusColor;
        Widgets.Label(statusValueRect, _cachedAiStatus);

        GUI.color = Color.white;
        currentY += rowHeight;

        void DrawStatRow(string label, string value)
        {
            var rowRect = new Rect(contentRect.x, currentY, contentRect.width, rowHeight);
            var labelRect = rowRect.LeftPartPixels(labelWidth);
            var valueRect = rowRect.RightPartPixels(rowRect.width - labelWidth);

            GUI.color = Color.gray;
            Widgets.Label(labelRect, label);
            GUI.color = Color.white;
            Widgets.Label(valueRect, value);

            currentY += rowHeight;
        }

        DrawStatRow("RimTalk.DebugWindow.TotalCalls".Translate(), _cachedTotalCalls.ToString("N0"));
        DrawStatRow("RimTalk.DebugWindow.TotalTokens".Translate(), _cachedTotalTokens.ToString("N0"));
        DrawStatRow("RimTalk.DebugWindow.AvgCallsPerMin".Translate(), _cachedAvgCallsPerMin.ToString("F2"));
        DrawStatRow("RimTalk.DebugWindow.AvgTokensPerMin".Translate(), _cachedAvgTokensPerMin.ToString("F2"));
        DrawStatRow("RimTalk.DebugWindow.AvgTokensPerCall".Translate(), _cachedAvgTokensPerCall.ToString("F2"));

        GUI.EndGroup();
        GUI.color = Color.white;
    }

    private void DrawGraph(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.15f, 0.4f));

        var series = new[]
        {
            (data: Stats.TokensPerSecondHistory, color: new Color(1f, 1f, 1f, 0.4f),
                label: "RimTalk.DebugWindow.TokensPerSecond".Translate()),
        };

        if (!series.Any(s => s.data != null && s.data.Any())) return;

        long maxVal = Math.Max(1, series.Where(s => s.data != null && s.data.Any()).SelectMany(s => s.data).Max());

        Text.Font = GameFont.Tiny;
        GUI.color = Color.grey;
        Widgets.Label(new Rect(rect.x + 5, rect.y, 40, 20), maxVal.ToString());
        Widgets.Label(new Rect(rect.x + 5, rect.y + rect.height - 15, 60, 20),
            "RimTalk.DebugWindow.SixtySecondsAgo".Translate());
        Widgets.Label(new Rect(rect.xMax - 35, rect.y + rect.height - 15, 40, 20),
            "RimTalk.DebugWindow.Now".Translate());
        GUI.color = Color.white;

        Rect graphArea = rect.ContractedBy(2f);

        foreach (var (data, color, _) in series)
        {
            if (data == null || data.Count < 2) continue;

            const float verticalPadding = 15f;
            float graphHeight = graphArea.height - (2 * verticalPadding);
            if (graphHeight <= 0) continue;

            var points = new List<Vector2>();
            for (int i = 0; i < data.Count; i++)
            {
                float x = graphArea.x + (float)i / (data.Count - 1) * graphArea.width;
                float y = (graphArea.y + graphArea.height - verticalPadding) -
                          ((float)data[i] / maxVal * graphHeight);
                points.Add(new Vector2(x, y));

                if (data[i] > 0 && i > 0 && i % 6 == 0)
                {
                    GUI.color = color;
                    Widgets.Label(new Rect(x - 10, y - 15, 40, 20), data[i].ToString());
                    GUI.color = Color.white;
                }
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                Widgets.DrawLine(points[i], points[i + 1], color, 2f);
            }
        }

        var legendRect = new Rect(rect.xMax - 100, rect.y + 10, 90, 30);
        var legendListing = new Listing_Standard();
        Widgets.DrawBoxSolid(legendRect, new Color(0, 0, 0, 0.4f));
        legendListing.Begin(legendRect.ContractedBy(5));
        foreach (var (data, color, label) in series)
        {
            var labelRect = legendListing.GetRect(18);
            Widgets.DrawBoxSolid(new Rect(labelRect.x, labelRect.y + 4, 10, 10), color);
            Widgets.Label(new Rect(labelRect.x + 15, labelRect.y, 70, 20), label);
        }

        legendListing.End();
    }

    private void DrawGroupedPawnTable(Rect rect)
    {
        if (_cachedPawnStates == null || !_cachedPawnStates.Any())
            return;

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        var originalSmallSize = Text.fontStyles[(int)GameFont.Small].fontSize;
        var originalTinySize = Text.fontStyles[(int)GameFont.Tiny].fontSize;

        try
        {
            float dynamicRowHeight = settings.OverlayFontSize + 8f;
            float dynamicHeaderHeight = dynamicRowHeight;

            Text.fontStyles[(int)GameFont.Small].fontSize = (int)settings.OverlayFontSize;
            Text.fontStyles[(int)GameFont.Tiny].fontSize = Mathf.Max(6, (int)settings.OverlayFontSize - 4);

            float totalHeight = CalculateGroupedTableHeight(dynamicRowHeight);
            var viewRect = new Rect(0, 0, rect.width - 16f, totalHeight);

            Widgets.BeginScrollView(rect, ref _tableScrollPosition, viewRect);

            float responseColumnWidth = CalculateGroupedResponseColumnWidth(viewRect.width);

            DrawGroupedHeader(new Rect(0, 0, viewRect.width, dynamicHeaderHeight), responseColumnWidth);
            float currentY = dynamicHeaderHeight;

            var pawnsToDisplay = _cachedPawnStates.ToList();
            for (int i = 0; i < pawnsToDisplay.Count; i++)
            {
                var pawnState = pawnsToDisplay[i];
                string pawnKey = pawnState.Pawn.LabelShort;
                bool isExpanded = _expandedPawns.Contains(pawnKey);

                var rowRect = new Rect(0, currentY, viewRect.width, dynamicRowHeight);
                if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

                float currentX = 0;

                Widgets.Label(new Rect(rowRect.x + 5, rowRect.y + (dynamicRowHeight - 15) / 2, 15, 15),
                    isExpanded ? "-" : "+");
                currentX += GroupedExpandIconWidth;

                var pawnNameRect = new Rect(currentX, rowRect.y, GroupedPawnNameWidth, dynamicRowHeight);

                DrawClickablePawnName(pawnNameRect, pawnKey, pawnState.Pawn);

                currentX += GroupedPawnNameWidth + ColumnPadding;

                string lastResponse = GetLastResponseForPawn(pawnKey);
                Widgets.Label(new Rect(currentX, rowRect.y, responseColumnWidth, dynamicRowHeight), lastResponse);
                currentX += responseColumnWidth + ColumnPadding;

                if (_debugModeEnabled)
                {
                    bool canTalk = pawnState.CanDisplayTalk();
                    string statusText = canTalk
                        ? "RimTalk.DebugWindow.StatusReady".Translate()
                        : "RimTalk.DebugWindow.StatusBusy".Translate();
                    GUI.color = canTalk ? Color.green : Color.yellow;
                    Widgets.Label(new Rect(currentX, rowRect.y, GroupedStatusWidth, dynamicRowHeight), statusText);
                    GUI.color = Color.white;
                    currentX += GroupedStatusWidth + ColumnPadding;

                    Widgets.Label(new Rect(currentX, rowRect.y, GroupedLastTalkWidth, dynamicRowHeight),
                        pawnState.LastTalkTick.ToString());
                    currentX += GroupedLastTalkWidth + ColumnPadding;

                    _cachedTalkLogsByPawn.TryGetValue(pawnKey, out var pawnRequests);
                    var requestsWithTokens = pawnRequests?.Where(r => r.TokenCount != 0).ToList();
                    Widgets.Label(new Rect(currentX, rowRect.y, GroupedRequestsWidth, dynamicRowHeight),
                        (requestsWithTokens?.Count ?? 0).ToString());
                    currentX += GroupedRequestsWidth + ColumnPadding;

                    Widgets.Label(new Rect(currentX, rowRect.y, GroupedChattinessWidth, dynamicRowHeight),
                        pawnState.TalkInitiationWeight.ToString("F2"));
                }

                if (Widgets.ButtonInvisible(rowRect))
                {
                    if (isExpanded) _expandedPawns.Remove(pawnKey);
                    else _expandedPawns.Add(pawnKey);
                }

                currentY += dynamicRowHeight;

                if (isExpanded && _cachedTalkLogsByPawn.TryGetValue(pawnKey, out var requests) && requests.Any())
                {
                    const float indentWidth = 20f;
                    float innerWidth = viewRect.width - indentWidth;
                    float innerResponseWidth = CalculateResponseColumnWidth(innerWidth, false);
                    DrawRequestTableHeader(new Rect(indentWidth, currentY, innerWidth, dynamicHeaderHeight),
                        innerResponseWidth, false);
                    currentY += dynamicHeaderHeight;

                    var reversedRequests = requests.ToList();
                    reversedRequests.Reverse();
                    DrawRequestRows(reversedRequests, ref currentY, innerWidth, indentWidth, innerResponseWidth, false,
                        dynamicRowHeight);
                }
            }

            Widgets.EndScrollView();
        }
        finally
        {
            Text.fontStyles[(int)GameFont.Small].fontSize = originalSmallSize;
            Text.fontStyles[(int)GameFont.Tiny].fontSize = originalTinySize;
        }
    }

    private void DrawGroupedHeader(Rect rect, float responseColumnWidth)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.3f, 0.3f, 0.3f, 0.4f));
        Text.Font = GameFont.Tiny;
        GUI.color = Color.white;

        float currentX = GroupedExpandIconWidth;
        Widgets.Label(new Rect(currentX, rect.y, GroupedPawnNameWidth, rect.height),
            "RimTalk.DebugWindow.HeaderPawn".Translate());
        currentX += GroupedPawnNameWidth + ColumnPadding;
        Widgets.Label(new Rect(currentX, rect.y, responseColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderResponse".Translate());
        currentX += responseColumnWidth + ColumnPadding;

        if (_debugModeEnabled)
        {
            Widgets.Label(new Rect(currentX, rect.y, GroupedStatusWidth, rect.height),
                "RimTalk.DebugWindow.HeaderStatus".Translate());
            currentX += GroupedStatusWidth + ColumnPadding;
            Widgets.Label(new Rect(currentX, rect.y, GroupedLastTalkWidth, rect.height),
                "RimTalk.DebugWindow.HeaderLastTalk".Translate());
            currentX += GroupedLastTalkWidth + ColumnPadding;
            Widgets.Label(new Rect(currentX, rect.y, GroupedRequestsWidth, rect.height),
                "RimTalk.DebugWindow.HeaderRequests".Translate());
            currentX += GroupedRequestsWidth + ColumnPadding;
            Widgets.Label(new Rect(currentX, rect.y, GroupedChattinessWidth, rect.height),
                "RimTalk.DebugWindow.HeaderChattiness".Translate());
        }
    }

    private void DrawUngroupedRequestTable(Rect rect)
    {
        if (_cachedRequests == null || !_cachedRequests.Any())
            return;

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        var originalSmallSize = Text.fontStyles[(int)GameFont.Small].fontSize;
        var originalTinySize = Text.fontStyles[(int)GameFont.Tiny].fontSize;

        try
        {
            float dynamicRowHeight = settings.OverlayFontSize + 8f;
            float dynamicHeaderHeight = dynamicRowHeight;

            Text.fontStyles[(int)GameFont.Small].fontSize = (int)settings.OverlayFontSize;
            Text.fontStyles[(int)GameFont.Tiny].fontSize = Mathf.Max(6, (int)settings.OverlayFontSize - 4);

            float totalHeight = CalculateUngroupedTableHeight(dynamicRowHeight);
            var viewRect = new Rect(0, 0, rect.width - 16f, totalHeight);

            Widgets.BeginScrollView(rect, ref _tableScrollPosition, viewRect);

            float responseWidth = CalculateResponseColumnWidth(viewRect.width, true);
            DrawRequestTableHeader(new Rect(0, 0, viewRect.width, dynamicHeaderHeight), responseWidth, true);
            float currentY = dynamicHeaderHeight;

            var reversedRequests = _cachedRequests.ToList();
            reversedRequests.Reverse();
            DrawRequestRows(reversedRequests, ref currentY, viewRect.width, 0, responseWidth, true,
                dynamicRowHeight);

            Widgets.EndScrollView();
        }
        finally
        {
            Text.fontStyles[(int)GameFont.Small].fontSize = originalSmallSize;
            Text.fontStyles[(int)GameFont.Tiny].fontSize = originalTinySize;
        }
    }

    private void DrawRequestTableHeader(Rect rect, float responseColumnWidth, bool showPawnColumn)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.25f, 0.4f));
        Text.Font = GameFont.Tiny;
        float currentX = rect.x + 5f;

        Widgets.Label(new Rect(currentX, rect.y, TimestampColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTimestamp".Translate());
        currentX += TimestampColumnWidth + ColumnPadding;
        if (showPawnColumn)
        {
            Widgets.Label(new Rect(currentX, rect.y, PawnColumnWidth, rect.height),
                "RimTalk.DebugWindow.HeaderPawn".Translate());
            currentX += PawnColumnWidth + ColumnPadding;
        }

        Widgets.Label(new Rect(currentX, rect.y, responseColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderResponse".Translate());
        currentX += responseColumnWidth + ColumnPadding;

        if (_debugModeEnabled)
        {
            Widgets.Label(new Rect(currentX, rect.y, TimeColumnWidth, rect.height),
                "RimTalk.DebugWindow.HeaderTimeMs".Translate());
            currentX += TimeColumnWidth + ColumnPadding;
            Widgets.Label(new Rect(currentX, rect.y, TokensColumnWidth, rect.height),
                "RimTalk.DebugWindow.HeaderTokens".Translate());
        }
    }

    private void DrawRequestRows(List<ApiLog> requests, ref float currentY, float totalWidth, float xOffset,
        float responseColumnWidth, bool showPawnColumn, float rowHeight)
    {
        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var rowRect = new Rect(xOffset, currentY, totalWidth, rowHeight);
            if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

            string resp = request.Response ?? _generating;

            bool isMultiTurnChild = (request.TokenCount == 0 || request.ElapsedMs == 0) && request.Response != null;

            int maxChars = (int)(responseColumnWidth / 7);
            if (resp.Length > maxChars) resp = resp.Substring(0, Math.Max(10, maxChars - 3)) + "...";

            float currentX = xOffset + 5f;
            Widgets.Label(new Rect(currentX, rowRect.y, TimestampColumnWidth, rowHeight),
                request.Timestamp.ToString("HH:mm:ss"));
            currentX += TimestampColumnWidth + ColumnPadding;

            if (showPawnColumn)
            {
                string pawnName = request.Name ?? "-";
                var pawnNameRect = new Rect(currentX, rowRect.y, PawnColumnWidth, rowHeight);
                var pawn = _cachedPawnStates.FirstOrDefault(p => p.Pawn.LabelShort == pawnName)?.Pawn;

                DrawClickablePawnName(pawnNameRect, pawnName, pawn);

                currentX += PawnColumnWidth + ColumnPadding;
            }

            var responseRect = new Rect(currentX, rowRect.y, responseColumnWidth, rowHeight);
            Widgets.Label(responseRect, resp);
            currentX += responseColumnWidth + ColumnPadding;

            if (_debugModeEnabled)
            {
                string elapsedMsText = (request.Response == null)
                    ? ""
                    : (isMultiTurnChild ? "-" : request.ElapsedMs.ToString());
                Widgets.Label(new Rect(currentX, rowRect.y, TimeColumnWidth, rowHeight), elapsedMsText);
                currentX += TimeColumnWidth + ColumnPadding;

                string tokenCountText = (request.Response == null)
                    ? ""
                    : (isMultiTurnChild ? "-" : request.TokenCount.ToString());
                Widgets.Label(new Rect(currentX, rowRect.y, TokensColumnWidth, rowHeight), tokenCountText);

                string tooltip =
                    "RimTalk.DebugWindow.TooltipPromptResponse".Translate(request.Prompt,
                        (request.Response ?? _generating));
                TooltipHandler.TipRegion(responseRect, tooltip);
            }
            else
            {
                var copyRect = new Rect(rowRect.xMax - CopyAreaWidth, rowRect.y, CopyAreaWidth, rowRect.height);
                if (Widgets.ButtonInvisible(copyRect))
                {
                    GUIUtility.systemCopyBuffer = request.RequestPayload ?? "";
                }
            }

            currentY += rowHeight;
        }
    }

    private float CalculateResponseColumnWidth(float totalWidth, bool includePawnColumn)
    {
        float fixedWidth = TimestampColumnWidth;
        int columnGaps = 2;

        if (includePawnColumn)
        {
            fixedWidth += PawnColumnWidth;
            columnGaps++;
        }

        if (_debugModeEnabled)
        {
            fixedWidth += TimeColumnWidth + TokensColumnWidth;
            columnGaps += 2;
        }

        float availableWidth = totalWidth - fixedWidth - (ColumnPadding * columnGaps) - CopyAreaWidth;
        return Math.Max(150f, availableWidth);
    }

    private float CalculateGroupedResponseColumnWidth(float totalWidth)
    {
        float fixedWidth = GroupedExpandIconWidth + GroupedPawnNameWidth;
        int columnGaps = 2;

        if (_debugModeEnabled)
        {
            fixedWidth += GroupedRequestsWidth + GroupedLastTalkWidth + GroupedChattinessWidth + GroupedStatusWidth;
            columnGaps += 4;
        }

        float availableWidth = totalWidth - fixedWidth - (ColumnPadding * columnGaps);
        return Math.Max(150f, availableWidth);
    }

    private float CalculateUngroupedTableHeight(float rowHeight)
    {
        return _cachedRequests == null ? 0f : rowHeight + (_cachedRequests.Count * rowHeight) + 50f;
    }

    private float CalculateGroupedTableHeight(float rowHeight)
    {
        if (_cachedPawnStates == null) return 0f;
        float height = rowHeight + (_cachedPawnStates.Count * rowHeight);
        foreach (var pawnState in _cachedPawnStates)
        {
            var pawnKey = pawnState.Pawn.LabelShort;
            if (_expandedPawns.Contains(pawnKey) && _cachedTalkLogsByPawn.TryGetValue(pawnKey, out var requests))
            {
                height += rowHeight;
                height += requests.Count * rowHeight;
            }
        }

        return height + 50f;
    }

    private string GetLastResponseForPawn(string pawnKey)
    {
        if (_cachedTalkLogsByPawn.TryGetValue(pawnKey, out var logs) && logs.Any())
        {
            return logs.LastOrDefault(l => l.Response != null)?.Response ?? _generating;
        }

        return "";
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