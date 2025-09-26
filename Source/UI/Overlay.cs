using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class Overlay : MapComponent
{
    // UI State
    private bool _isDragging;
    private bool _isResizing;
    private Vector2 _dragStartOffset;
    private bool _showSettingsDropdown;
    private Rect _gearIconScreenRect;
    private Rect _settingsDropdownRect;
    private Vector2 _tableScrollPosition;

    // Settings & Cache
    private bool _groupingEnabled;
    private bool _debugModeEnabled;
    private string _sortColumn;
    private bool _sortAscending;
    private readonly List<string> _expandedPawns;

    // Data Cache
    private string _aiStatus;
    private long _totalCalls;
    private long _totalTokens;
    private double _avgCallsPerMin;
    private double _avgTokensPerMin;
    private double _avgTokensPerCall;
    private List<PawnState> _pawnStates;
    private List<ApiLog> _requests;
    private Dictionary<string, List<ApiLog>> _talkLogsByPawn = new();

    // Constants: General
    private const float ColumnPadding = 10f;
    private const float OptionsBarHeight = 30f;
    private const float ResizeHandleSize = 24f;
    private const float DropdownWidth = 200f;
    private const float DropdownHeight = 270f;

    // Constants: Debug View
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
        _sortColumn = settings.DebugSortColumn;
        _sortAscending = settings.DebugSortAscending;
        _expandedPawns = settings.DebugExpandedPawns ?? new List<string>();
    }

    public override void MapComponentOnGUI()
    {
        if (Current.ProgramState != ProgramState.Playing) return;

        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        if (!settings.OverlayEnabled) return;

        // Use the correct Rect based on whether debug mode is enabled
        ref Rect currentOverlayRect =
            ref (_debugModeEnabled ? ref settings.OverlayRectDebug : ref settings.OverlayRectNonDebug);

        if (currentOverlayRect.width <= 0 || currentOverlayRect.height <= 0)
        {
            currentOverlayRect = _debugModeEnabled
                ? new Rect(20, 20, 600, 450)
                : new Rect(20, 20, 400, 250);
        }

        ClampRectToScreen(ref currentOverlayRect);

        var windowRect = currentOverlayRect;
        var dragHandleRect = new Rect(windowRect.x, windowRect.y, windowRect.width, OptionsBarHeight);

        float iconSize = OptionsBarHeight - 4f;
        _gearIconScreenRect = new Rect(windowRect.xMax - iconSize - 5f, windowRect.y + 2f, iconSize, iconSize);
        _settingsDropdownRect = new Rect(_gearIconScreenRect.x - DropdownWidth + _gearIconScreenRect.width,
            _gearIconScreenRect.yMax, DropdownWidth, DropdownHeight);

        HandleInput(ref currentOverlayRect, dragHandleRect);

        GUI.BeginGroup(windowRect);
        var inRect = new Rect(Vector2.zero, windowRect.size);

        Widgets.DrawBoxSolid(inRect, new Color(0.1f, 0.1f, 0.1f, settings.OverlayOpacity));

        UpdateData();

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


        var resizeHandleRect = new Rect(inRect.width - ResizeHandleSize, inRect.height - ResizeHandleSize,
            ResizeHandleSize, ResizeHandleSize);
        GUI.DrawTexture(resizeHandleRect, TexUI.WinExpandWidget);
        TooltipHandler.TipRegion(resizeHandleRect, "Drag to resize");

        GUI.EndGroup();

        if (_showSettingsDropdown)
        {
            DrawSettingsDropdown();
        }
    }

    private void HandleInput(ref Rect windowRect, Rect dragHandleRect)
    {
        Event currentEvent = Event.current;
        var resizeHandleRect = new Rect(windowRect.xMax - ResizeHandleSize, windowRect.yMax - ResizeHandleSize,
            ResizeHandleSize, ResizeHandleSize);

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

            if (resizeHandleRect.Contains(currentEvent.mousePosition))
            {
                _isResizing = true;
                currentEvent.Use();
            }
            else if (dragHandleRect.Contains(currentEvent.mousePosition) &&
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
                windowRect.width = Mathf.Max(350, currentEvent.mousePosition.x - windowRect.x);
                windowRect.height = Mathf.Max(50, currentEvent.mousePosition.y - windowRect.y);
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

    private void UpdateData()
    {
        var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        _aiStatus = !settings.IsEnabled
            ? "RimTalk.DebugWindow.StatusDisabled".Translate()
            : (AIService.IsBusy()
                ? "RimTalk.DebugWindow.StatusProcessing".Translate()
                : "RimTalk.DebugWindow.StatusIdle".Translate());

        _totalCalls = Stats.TotalCalls;
        _totalTokens = Stats.TotalTokens;
        _avgCallsPerMin = Stats.AvgCallsPerMinute;
        _avgTokensPerMin = Stats.AvgTokensPerMinute;
        _avgTokensPerCall = Stats.AvgTokensPerCall;
        _pawnStates = Cache.GetAll().ToList();
        _requests = ApiHistory.GetAll().ToList();

        _talkLogsByPawn = _requests.Where(r => r.Name != null)
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.ToList());
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
        if (_groupingEnabled)
        {
            var latestMessages = _talkLogsByPawn
                .Select(kvp => kvp.Value.LastOrDefault(log => log.Response != null))
                .Where(log => log != null)
                .OrderByDescending(log => log.Timestamp);
            DrawMessageLogInternal(inRect, latestMessages);
        }
        else
        {
            var completedMessages = _requests.Where(r => r.Response != null).Reverse();
            DrawMessageLogInternal(inRect, completedMessages);
        }
    }

    private void DrawMessageLogInternal(Rect inRect, IEnumerable<ApiLog> messagesToDraw)
    {
        var contentRect = inRect.ContractedBy(5f);

        if (!messagesToDraw.Any())
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
                    
                float nameWidth = Text.CalcSize(formattedName + " ").x;
                var nameRect = new Rect(rowRect.x, rowRect.y, nameWidth, rowRect.height);
                var dialogueRect = new Rect(nameRect.xMax, rowRect.y, rowRect.width - nameWidth,
                    rowRect.height);

                // Use the new centralized method to draw the pawn's name
                DrawClickablePawnName(nameRect, pawnName);

                Widgets.Label(dialogueRect, dialogue);
            }
        }
        finally
        {
            Text.fontStyles[(int)gameFont].fontSize = originalFontSize;
            Text.Font = originalFont;
            Text.Anchor = originalAnchor;
        }
    }
        
    /// <summary>
    /// Draws a pawn's name with appropriate coloring and makes it a clickable button to jump to their location.
    /// This is the centralized method for drawing pawn names consistently.
    /// </summary>
    /// <param name="rect">The Rect to draw the name in.</param>
    /// <param name="pawnName">The name of the pawn to draw.</param>
    /// <param name="pawnInstance">An optional, pre-fetched instance of the pawn to avoid lookups.</param>
    private void DrawClickablePawnName(Rect rect, string pawnName, Pawn pawnInstance = null)
    {
        // 1. Find the pawn if not already provided.
        var pawn = pawnInstance;
        if (pawn == null)
        {
            pawn = Cache.GetByName(pawnName)?.Pawn;
            if (pawn == null)
            {
                pawn = Find.WorldPawns.AllPawnsAliveOrDead.FirstOrDefault(p => p.Name.ToStringShort == pawnName);
            }
        }

        // 2. Handle drawing based on whether the pawn was found.
        if (pawn != null)
        {
            var originalColor = GUI.color; // Store original color.
            Widgets.DrawHighlightIfMouseover(rect);

            // 3. Apply color based on pawn status (dead or alive).
            GUI.color = pawn.Dead ? Color.gray : PawnNameColorUtility.PawnNameColorOf(pawn);

            Widgets.Label(rect, pawnName);

            // 4. Create an invisible button for camera jumping.
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

            GUI.color = originalColor; // 5. IMPORTANT: Reset to original color.
        }
        else
        {
            // Fallback for pawns that can't be found.
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
        var aiStatus = _aiStatus.Translate();
        if (aiStatus == "RimTalk.DebugWindow.StatusProcessing".Translate()) statusColor = Color.yellow;
        else if (aiStatus == "RimTalk.DebugWindow.StatusIdle".Translate()) statusColor = Color.green;
        else statusColor = Color.grey;


        var statusRowRect = new Rect(contentRect.x, currentY, contentRect.width, rowHeight);
        var statusLabelRect = statusRowRect.LeftPartPixels(labelWidth);
        var statusValueRect = new Rect(statusLabelRect.xMax, currentY, 100f, rowHeight);

        GUI.color = Color.gray;
        Widgets.Label(statusLabelRect, "RimTalk.DebugWindow.AIStatus".Translate());
        GUI.color = statusColor;
        Widgets.Label(statusValueRect, _aiStatus);

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

        DrawStatRow("RimTalk.DebugWindow.TotalCalls".Translate(), _totalCalls.ToString("N0"));
        DrawStatRow("RimTalk.DebugWindow.TotalTokens".Translate(), _totalTokens.ToString("N0"));
        DrawStatRow("RimTalk.DebugWindow.AvgCallsPerMin".Translate(), _avgCallsPerMin.ToString("F2"));
        DrawStatRow("RimTalk.DebugWindow.AvgTokensPerMin".Translate(), _avgTokensPerMin.ToString("F2"));
        DrawStatRow("RimTalk.DebugWindow.AvgTokensPerCall".Translate(), _avgTokensPerCall.ToString("F2"));

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
        if (_pawnStates == null || !_pawnStates.Any())
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

            var sortedPawns = GetSortedPawnStates().ToList();
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                var pawnState = sortedPawns[i];
                string pawnKey = pawnState.Pawn.LabelShort;
                bool isExpanded = _expandedPawns.Contains(pawnKey);

                var rowRect = new Rect(0, currentY, viewRect.width, dynamicRowHeight);
                if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

                float currentX = 0;

                Widgets.Label(new Rect(rowRect.x + 5, rowRect.y + (dynamicRowHeight - 15) / 2, 15, 15),
                    isExpanded ? "-" : "+");
                currentX += GroupedExpandIconWidth;

                var pawnNameRect = new Rect(currentX, rowRect.y, GroupedPawnNameWidth, dynamicRowHeight);
                // Use the new centralized method to draw the pawn's name
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

                    _talkLogsByPawn.TryGetValue(pawnKey, out var pawnRequests);
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

                if (isExpanded && _talkLogsByPawn.TryGetValue(pawnKey, out var requests) && requests.Any())
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
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedPawnNameWidth, rect.height), "Pawn");
        currentX += GroupedPawnNameWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, responseColumnWidth, rect.height), "Response");
        currentX += responseColumnWidth + ColumnPadding;

        if (_debugModeEnabled)
        {
            DrawSortableHeader(new Rect(currentX, rect.y, GroupedStatusWidth, rect.height), "Status");
            currentX += GroupedStatusWidth + ColumnPadding;
            DrawSortableHeader(new Rect(currentX, rect.y, GroupedLastTalkWidth, rect.height), "LastTalk");
            currentX += GroupedLastTalkWidth + ColumnPadding;
            DrawSortableHeader(new Rect(currentX, rect.y, GroupedRequestsWidth, rect.height), "Requests");
            currentX += GroupedRequestsWidth + ColumnPadding;
            DrawSortableHeader(new Rect(currentX, rect.y, GroupedChattinessWidth, rect.height), "Chattiness");
        }
    }

    private void DrawUngroupedRequestTable(Rect rect)
    {
        if (_requests == null || !_requests.Any())
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

            var reversedRequests = ApiHistory.GetAll().ToList();
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
                var pawn = _pawnStates.FirstOrDefault(p => p.Pawn.LabelShort == pawnName)?.Pawn;

                // Use the new centralized method to draw the pawn's name
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

    private void DrawSortableHeader(Rect rect, string column)
    {
        string translatedColumn = ("RimTalk.DebugWindow.Header" + column).Translate();
        string arrow = (_sortColumn == column) ? (_sortAscending ? " ▲" : " ▼") : "";
        if (Widgets.ButtonInvisible(rect))
        {
            if (_sortColumn == column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = column;
                _sortAscending = true;
            }

            var settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
            settings.DebugSortColumn = _sortColumn;
            settings.DebugSortAscending = _sortAscending;
            settings.Write();
        }

        Widgets.Label(rect, translatedColumn + arrow);
    }

    private IEnumerable<PawnState> GetSortedPawnStates()
    {
        switch (_sortColumn)
        {
            case "Pawn":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.Pawn.LabelShort)
                    : _pawnStates.OrderByDescending(p => p.Pawn.LabelShort);
            case "Requests":
                return _sortAscending
                    ? _pawnStates.OrderBy(p =>
                        _talkLogsByPawn.TryGetValue(p.Pawn.LabelShort, out var logs) ? logs.Count : 0)
                    : _pawnStates.OrderByDescending(p =>
                        _talkLogsByPawn.TryGetValue(p.Pawn.LabelShort, out var logs) ? logs.Count : 0);
            case "Response":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => GetLastResponseForPawn(p.Pawn.LabelShort))
                    : _pawnStates.OrderByDescending(p => GetLastResponseForPawn(p.Pawn.LabelShort));
            case "Status":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.CanDisplayTalk())
                    : _pawnStates.OrderByDescending(p => p.CanDisplayTalk());
            case "LastTalk":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.LastTalkTick)
                    : _pawnStates.OrderByDescending(p => p.LastTalkTick);
            case "Chattiness":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.TalkInitiationWeight)
                    : _pawnStates.OrderByDescending(p => p.TalkInitiationWeight);
            default:
                return _pawnStates;
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
        return _requests == null ? 0f : rowHeight + (_requests.Count * rowHeight) + 50f;
    }

    private float CalculateGroupedTableHeight(float rowHeight)
    {
        if (_pawnStates == null) return 0f;
        float height = rowHeight + (_pawnStates.Count * rowHeight);
        foreach (var pawnState in _pawnStates)
        {
            var pawnKey = pawnState.Pawn.LabelShort;
            if (_expandedPawns.Contains(pawnKey) && _talkLogsByPawn.TryGetValue(pawnKey, out var requests))
            {
                height += rowHeight;
                height += requests.Count * rowHeight;
            }
        }

        return height + 50f;
    }

    private string GetLastResponseForPawn(string pawnKey)
    {
        if (_talkLogsByPawn.TryGetValue(pawnKey, out var logs) && logs.Any())
        {
            return logs.LastOrDefault(l => l.Response != null)?.Response ?? _generating;
        }

        return "";
    }
}