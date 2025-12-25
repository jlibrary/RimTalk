using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimTalk.Data;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class DebugWindow : Window
{
    // Layout Constants
    private const float RowHeight = 22f;
    private const float FilterBarHeight = 30f;
    private const float HeaderHeight = 22f;
    private const float ColumnPadding = 10f;

    // Column Widths
    private const float TimestampColumnWidth = 65f;
    private const float PawnColumnWidth = 80f;
    private const float TimeColumnWidth = 55f;
    private const float TokensColumnWidth = 50f;
    private const float StateColumnWidth = 65f;
    private const float InteractionTypeColumnWidth = 50f;

    // Grouping Column Widths
    private const float GroupedPawnNameWidth = 80f;
    private const float GroupedRequestsWidth = 60f;
    private const float GroupedLastTalkWidth = 60f;
    private const float GroupedChattinessWidth = 65f;
    private const float GroupedExpandIconWidth = 25f;
    private const float GroupedStatusWidth = 60f;

    private readonly string _generating = "RimTalk.DebugWindow.Generating".Translate();

    // State Variables
    private Vector2 _tableScrollPosition;
    private Vector2 _detailsScrollPosition;
    private bool _stickToBottom = true;

    private string _aiStatus;

    // Stats
    private long _totalCalls;
    private long _totalTokens;
    private double _avgCallsPerMin;
    private double _avgTokensPerMin;
    private double _avgTokensPerCall;

    private List<PawnState> _pawnStates;
    private List<ApiLog> _requests;
    private readonly Dictionary<string, List<ApiLog>> _talkLogsByPawn = new();

    // Controls
    private int _maxRows;
    private string _pawnFilter;
    private string _textSearch;
    private int _stateFilter; // 0=All, 1=Pending, 2=Ignored, 3=Spoken
    private ApiLog _selectedRequest;

    private bool _groupingEnabled;
    private string _sortColumn;
    private bool _sortAscending;
    private readonly List<string> _expandedPawns;

    // Focus Control Names
    private const string ControlNamePawnFilter = "PawnFilterField";
    private const string ControlNameTextSearch = "TextSearchField";

    // Styles
    private GUIStyle _contextStyle;
    private GUIStyle _monoTinyStyle;

    public DebugWindow()
    {
        doCloseX = true;
        draggable = true;
        resizeable = true;
        absorbInputAroundWindow = false;
        closeOnClickedOutside = false;
        preventCameraMotion = false;

        var settings = Settings.Get();
        _groupingEnabled = settings.DebugGroupingEnabled;
        _sortColumn = settings.DebugSortColumn;
        _sortAscending = settings.DebugSortAscending;
        _expandedPawns = [];

        _maxRows = 500;
        _pawnFilter = string.Empty;
        _textSearch = string.Empty;
        _stateFilter = 0;
    }

    public override Vector2 InitialSize => new(1100f, 600f);

    public override void PreClose()
    {
        base.PreClose();
        var settings = Settings.Get();
        settings.DebugGroupingEnabled = _groupingEnabled;
        settings.DebugSortColumn = _sortColumn;
        settings.DebugSortAscending = _sortAscending;
        settings.Write();
    }

    private void InitializeContextStyle()
    {
        if (_contextStyle == null)
        {
            _contextStyle = new GUIStyle(Text.fontStyles[(int)GameFont.Tiny])
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };
        }

        if (_monoTinyStyle == null)
        {
            _monoTinyStyle = new GUIStyle(Text.fontStyles[(int)GameFont.Tiny])
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
        }
    }

    public override void DoWindowContents(Rect inRect)
    {
        HandleGlobalClicks(inRect);
        UpdateData();

        const float bottomSectionHeight = 150f;
        const float spacing = 10f;

        float contentHeight = inRect.height - bottomSectionHeight - spacing;

        // LEFT PANE (Table + Filters) vs RIGHT PANE (Details)
        float leftWidth = inRect.width * 0.60f - (spacing / 2);
        float rightWidth = inRect.width * 0.40f - (spacing / 2);

        var leftPaneRect = new Rect(inRect.x, inRect.y, leftWidth, contentHeight);
        var detailsRect = new Rect(leftPaneRect.xMax + spacing, inRect.y, rightWidth, contentHeight);

        DrawLeftPane(leftPaneRect);
        DrawDetailsPanel(detailsRect);

        // Bottom Section
        var bottomRect = new Rect(inRect.x, leftPaneRect.yMax + spacing, inRect.width, bottomSectionHeight);
        float graphWidth = bottomRect.width * 0.50f;
        float statsWidth = bottomRect.width * 0.30f;
        float actionsWidth = bottomRect.width * 0.20f - (spacing * 2);

        var graphRect = new Rect(bottomRect.x, bottomRect.y, graphWidth, bottomRect.height);
        var statsRect = new Rect(graphRect.xMax + spacing, bottomRect.y, statsWidth, bottomRect.height);
        var actionsRect = new Rect(statsRect.xMax + spacing, bottomRect.y, actionsWidth, bottomRect.height);

        DrawGraph(graphRect);
        DrawStatsSection(statsRect);
        DrawBottomActions(actionsRect);
    }

    private void UpdateData()
    {
        var settings = Settings.Get();
        if (!settings.IsEnabled)
            _aiStatus = "RimTalk.DebugWindow.StatusDisabled".Translate();
        else
            _aiStatus = AIService.IsBusy()
                ? "RimTalk.DebugWindow.StatusProcessing".Translate()
                : "RimTalk.DebugWindow.StatusIdle".Translate();

        _totalCalls = Stats.TotalCalls;
        _totalTokens = Stats.TotalTokens;
        _avgCallsPerMin = Stats.AvgCallsPerMinute;
        _avgTokensPerMin = Stats.AvgTokensPerMinute;
        _avgTokensPerCall = Stats.AvgTokensPerCall;
        _pawnStates = Cache.GetAll().ToList();

        var allHistory = ApiHistory.GetAll();
        var filtered = ApplyFilters(allHistory);

        var apiLogs = filtered as ApiLog[] ?? filtered.ToArray();
        int count = apiLogs.Count();
        _requests = count > _maxRows ? apiLogs.Skip(count - _maxRows).ToList() : apiLogs.ToList();

        _talkLogsByPawn.Clear();
        foreach (var request in _requests.Where(r => r.Name != null))
        {
            if (!_talkLogsByPawn.ContainsKey(request.Name))
                _talkLogsByPawn[request.Name] = [];
            _talkLogsByPawn[request.Name].Add(request);
        }

        // If selected request is no longer in the filtered list, deselect
        if (_selectedRequest != null && _requests.All(r => r.Id != _selectedRequest.Id))
            _selectedRequest = null;
    }

    private void DrawLeftPane(Rect rect)
    {
        // Filter Bar (Integrated at top)
        var filterRect = new Rect(rect.x, rect.y, rect.width, FilterBarHeight);
        DrawInternalFilterBar(filterRect);

        // Table Area
        var tableRect = new Rect(rect.x, rect.y + FilterBarHeight, rect.width, rect.height - FilterBarHeight);

        if (_groupingEnabled)
            DrawGroupedPawnTable(tableRect);
        else
            DrawConsoleTable(tableRect);
    }

    private void DrawInternalFilterBar(Rect rect)
    {
        float y = rect.y + 3f;
        float height = 24f;
        float gap = 5f;
        float startX = rect.x + 5f;

        // Defined widths for fixed elements
        float iconWidth = 24f;
        float statusWidth = 100f;
        float limitWidth = 90f;

        // Calculate how much horizontal space is used by fixed elements + gaps
        // (Margin) + Icon + (Gap) + (Gap) + (Gap) + Status + (Gap) + Limit
        float totalFixedSpace = 5f + iconWidth + gap + gap + gap + statusWidth + gap + limitWidth;

        // Calculate remaining flexible space
        float flexSpace = rect.width - totalFixedSpace;
        if (flexSpace < 50f) flexSpace = 50f; // Prevent collapse

        // Allocate space: 35% for Pawn Name, 65% for Text Search
        float pawnFilterWidth = flexSpace * 0.35f;
        float textSearchWidth = flexSpace * 0.65f;

        float currentX = startX;

        // 1. Grouping Checkbox
        WidgetRow row = new WidgetRow(currentX, y, UIDirection.RightThenUp, 9999f, 0f);
        row.ToggleableIcon(
            ref _groupingEnabled,
            TexButton.ToggleTweak,
            "RimTalk.DebugWindow.GroupByPawn".Translate(),
            SoundDefOf.Mouseover_ButtonToggle
        );
        currentX += iconWidth + gap;

        // 2. Pawn Filter
        _pawnFilter = DrawSearchField(new Rect(currentX, y, pawnFilterWidth, height), _pawnFilter,
            "RimTalk.DebugWindow.FilterPawn".Translate(), ControlNamePawnFilter);
        currentX += pawnFilterWidth + gap;

        // 3. Text Search
        _textSearch = DrawSearchField(new Rect(currentX, y, textSearchWidth, height), _textSearch,
            "RimTalk.DebugWindow.Search".Translate(), ControlNameTextSearch);
        currentX += textSearchWidth + gap;

        // 4. Status Dropdown
        var stateBtnRect = new Rect(currentX, y, statusWidth, height);
        if (Widgets.ButtonText(stateBtnRect, GetStateLabel(_stateFilter)))
        {
            var options = new List<FloatMenuOption>
            {
                new(GetStateLabel(0), () => _stateFilter = 0),
                new(GetStateLabel(1), () => _stateFilter = 1),
                new(GetStateLabel(2), () => _stateFilter = 2),
                new(GetStateLabel(3), () => _stateFilter = 3)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
        currentX += statusWidth + gap;

        // 5. Limit Dropdown
        var limitBtnRect = new Rect(currentX, y, limitWidth, height);
        string lastPrefix = "RimTalk.DebugWindow.Last".Translate();
        
        if (Widgets.ButtonText(limitBtnRect, $"{lastPrefix} {_maxRows}"))
        {
            var options = new List<FloatMenuOption>
            {
                new($"{lastPrefix} 200", () => _maxRows = 200),
                new($"{lastPrefix} 500", () => _maxRows = 500),
                new($"{lastPrefix} 1000", () => _maxRows = 1000),
                new($"{lastPrefix} 2000", () => _maxRows = 2000)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    private void DrawConsoleTable(Rect rect)
    {
        // Column Headers
        float responseWidth = CalculateResponseColumnWidth(rect.width, true);
        DrawRequestTableHeader(new Rect(rect.x, rect.y, rect.width, HeaderHeight), responseWidth, true);

        // Scroll View
        var scrollRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
        float viewWidth = scrollRect.width - 16f;
        float viewHeight = _requests.Count * RowHeight;
        var viewRect = new Rect(0, 0, viewWidth, viewHeight);

        // Calculate max scroll
        float maxScroll = Mathf.Max(0f, viewHeight - scrollRect.height);

        if (_stickToBottom)
            _tableScrollPosition.y = maxScroll;

        Widgets.BeginScrollView(scrollRect, ref _tableScrollPosition, viewRect);

        if (_stickToBottom && _tableScrollPosition.y < maxScroll - 1f)
            _stickToBottom = false;

        // Define button size constant
        const float btnSize = 30f;

        // Calculate Overlay Blocking Rect (Inner Content Space)
        Rect? overlayContentRect = null;
        if (!_stickToBottom)
        {
            float overlayWinX = scrollRect.xMax - btnSize - 20f;
            float overlayWinY = scrollRect.yMax - btnSize - 5f;

            float overlayContentX = overlayWinX - scrollRect.x + _tableScrollPosition.x;
            float overlayContentY = overlayWinY - scrollRect.y + _tableScrollPosition.y;

            overlayContentRect = new Rect(overlayContentX, overlayContentY, btnSize, btnSize);
        }

        // Virtualization
        float visibleTop = _tableScrollPosition.y;
        float visibleBottom = _tableScrollPosition.y + scrollRect.height;
        int firstIndex = Mathf.Clamp((int)(visibleTop / RowHeight), 0, _requests.Count);
        int lastIndex = Mathf.Clamp((int)(visibleBottom / RowHeight) + 1, 0, _requests.Count);

        for (int i = firstIndex; i < lastIndex; i++)
        {
            float rowY = i * RowHeight;
            bool inputBlocked = overlayContentRect.HasValue && Mouse.IsOver(overlayContentRect.Value);
            DrawRequestRow(_requests[i], i, rowY, viewWidth, 0f, responseWidth, true, inputBlocked);
        }

        Widgets.EndScrollView();

        // Overlay Button (Scroll to Bottom)
        if (!_stickToBottom)
        {
            var overlayRect = new Rect(scrollRect.xMax - btnSize - 20f, scrollRect.yMax - btnSize - 5f, btnSize,
                btnSize);

            bool isMouseOver = Mouse.IsOver(overlayRect);

            Color bgColor = isMouseOver
                ? new Color(0.3f, 0.3f, 0.3f, 1f)
                : new Color(0, 0, 0, 0.6f);

            Widgets.DrawBoxSolid(overlayRect, bgColor);

            if (Widgets.ButtonInvisible(overlayRect))
            {
                _stickToBottom = true;
                _tableScrollPosition.y = maxScroll;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;

            var labelRect = overlayRect;
            labelRect.xMin += 5f;

            Widgets.Label(labelRect, "▼");

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;

            TooltipHandler.TipRegion(overlayRect, "RimTalk.DebugWindow.AutoScroll".Translate());
        }
    }

    private void DrawRequestRow(ApiLog request, int rowIndex, float rowY, float totalWidth, float xOffset,
        float responseColumnWidth, bool showPawnColumn, bool inputBlocked = false)
    {
        var rowRect = new Rect(xOffset, rowY, totalWidth, RowHeight);
        if (rowIndex % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

        bool isSelected = _selectedRequest != null && _selectedRequest.Id == request.Id;
        if (isSelected) Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.25f, 0.35f, 0.45f));

        string resp = request.Response ?? _generating;
        int maxChars = Mathf.FloorToInt(responseColumnWidth / 7f);
        if (maxChars < 1) maxChars = 1;
        if (resp.Length > maxChars)
        {
            int targetLen = maxChars - 3;
            targetLen = Mathf.Clamp(targetLen, 1, resp.Length);
            resp = resp.Substring(0, targetLen) + "...";
        }

        float currentX = xOffset + 5f;
        Widgets.Label(new Rect(currentX, rowRect.y, TimestampColumnWidth, RowHeight),
            request.Timestamp.ToString("HH:mm:ss"));
        currentX += TimestampColumnWidth + ColumnPadding;

        if (showPawnColumn)
        {
            string pawnName = request.Name ?? "-";
            var pawnNameRect = new Rect(currentX, rowRect.y, PawnColumnWidth, RowHeight);
            var pawn = _pawnStates.FirstOrDefault(p => p.Pawn.LabelShort == pawnName)?.Pawn;
            UIUtil.DrawClickablePawnName(pawnNameRect, pawnName, pawn);
            currentX += PawnColumnWidth + ColumnPadding;
        }

        Widgets.Label(new Rect(currentX, rowRect.y, responseColumnWidth, RowHeight), resp);
        currentX += responseColumnWidth + ColumnPadding;

        string interactionType = request.InteractionType ?? "-";
        Widgets.Label(new Rect(currentX, rowRect.y, InteractionTypeColumnWidth, RowHeight), interactionType);
        currentX += InteractionTypeColumnWidth + ColumnPadding;

        string elapsedMsText = request.Response == null
            ? ""
            : (request.ElapsedMs == 0 ? "-" : request.ElapsedMs.ToString());
        Widgets.Label(new Rect(currentX, rowRect.y, TimeColumnWidth, RowHeight), elapsedMsText);
        currentX += TimeColumnWidth + ColumnPadding;

        string tokenCountText = request.Response == null
            ? ""
            : (request.TokenCount == 0 ? (request.IsFirstDialogue ? "?" : "-") : request.TokenCount.ToString());
        Widgets.Label(new Rect(currentX, rowRect.y, TokensColumnWidth, RowHeight), tokenCountText);
        currentX += TokensColumnWidth + ColumnPadding;

        string statusText;
        Color statusColor;
        if (request.Response == null || request.SpokenTick == 0)
        {
            statusText = "RimTalk.DebugWindow.StatePending".Translate();
            statusColor = Color.yellow;
        }
        else if (request.SpokenTick == -1)
        {
            statusText = "RimTalk.DebugWindow.StateIgnored".Translate();
            statusColor = Color.red;
        }
        else
        {
            statusText = "RimTalk.DebugWindow.StateSpoken".Translate();
            statusColor = Color.green;
        }

        GUI.color = statusColor;
        Widgets.Label(new Rect(currentX, rowRect.y, StateColumnWidth, RowHeight), statusText);
        GUI.color = Color.white;

        // Only show tooltip if input isn't blocked by the overlay
        if (!inputBlocked)
        {
            TooltipHandler.TipRegion(rowRect, "RimTalk.DebugWindow.TooltipSelectForDetails".Translate());
        }

        // Only process click if input isn't blocked by the overlay
        if (!inputBlocked && Widgets.ButtonInvisible(rowRect))
        {
            _selectedRequest = request;
            // Interrupt auto-scroll on selection
            _stickToBottom = false;
        }
    }

    private void DrawDetailsPanel(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f, 0.8f));
        InitializeContextStyle();

        var inner = rect.ContractedBy(8f);
        GUI.BeginGroup(inner);

        float y = 0f;
        Text.Font = GameFont.Small;
        Widgets.Label(new Rect(0f, y, inner.width, 24f), "RimTalk.DebugWindow.Details".Translate());
        y += 26f;

        if (_selectedRequest == null)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inner.width, 50f), "RimTalk.DebugWindow.SelectRowHint".Translate());
            GUI.color = Color.white;
            GUI.EndGroup();
            return;
        }

        var header = new StringBuilder();
        header.Append(_selectedRequest.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        header.Append("  |  ");
        header.Append((_selectedRequest.Name ?? "-").Trim());
        if (_selectedRequest.InteractionType != null)
        {
            header.Append("  |  ");
            header.Append(_selectedRequest.InteractionType);
        }

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(0f, y, inner.width, 18f), header.ToString());
        GUI.color = Color.white;
        y += 22f;

        float buttonsRowH = 24f;
        float btnW = 88f;
        float btnX = 0f;
        if (Widgets.ButtonText(new Rect(btnX, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyAll".Translate()))
        {
            GUIUtility.systemCopyBuffer = BuildCopyAll(_selectedRequest);
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        btnX += btnW + 6f;
        if (Widgets.ButtonText(new Rect(btnX, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyPrompt".Translate()))
        {
            GUIUtility.systemCopyBuffer = _selectedRequest.Prompt ?? string.Empty;
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        btnX += btnW + 6f;
        if (Widgets.ButtonText(new Rect(btnX, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyResponse".Translate()))
        {
            GUIUtility.systemCopyBuffer = _selectedRequest.Response ?? string.Empty;
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        btnX += btnW + 6f;
        if (Widgets.ButtonText(new Rect(btnX, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyContexts".Translate()))
        {
            GUIUtility.systemCopyBuffer = BuildCopyContexts(_selectedRequest);
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        y += buttonsRowH + 8f;

        // Scrollable selectable areas
        var scrollOuter = new Rect(0f, y, inner.width, inner.height - y);
        float viewH = 0f;
        float blockSpacing = 10f;

        string prompt = _selectedRequest.Prompt ?? string.Empty;
        string response = _selectedRequest.Response ?? string.Empty;
        string contexts = BuildCopyContexts(_selectedRequest);

        float promptH = Mathf.Max(70f,
            _monoTinyStyle.CalcHeight(new GUIContent(prompt), scrollOuter.width - 16f) + 10f);
        float respH = Mathf.Max(90f,
            _monoTinyStyle.CalcHeight(new GUIContent(response), scrollOuter.width - 16f) + 10f);
        float ctxH = Mathf.Max(70f, _monoTinyStyle.CalcHeight(new GUIContent(contexts), scrollOuter.width - 16f) + 10f);

        viewH = 18f + promptH + blockSpacing + 18f + respH + blockSpacing + 18f + ctxH + 10f;
        var view = new Rect(0f, 0f, scrollOuter.width - 16f, viewH);

        Widgets.BeginScrollView(scrollOuter, ref _detailsScrollPosition, view);
        float yy = 0f;

        DrawSelectableBlock(ref yy, view.width, "RimTalk.DebugWindow.Response".Translate(), response, respH);
        yy += blockSpacing;
        DrawSelectableBlock(ref yy, view.width, "RimTalk.DebugWindow.Prompt".Translate(), prompt, promptH);
        yy += blockSpacing;
        DrawSelectableBlock(ref yy, view.width, "RimTalk.DebugWindow.Contexts".Translate(), contexts, ctxH);

        Widgets.EndScrollView();

        GUI.EndGroup();
    }

    private void DrawSelectableBlock(ref float y, float width, string title, string content, float contentHeight)
    {
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(0f, y, width, 18f), title);
        GUI.color = Color.white;
        y += 18f;

        var box = new Rect(0f, y, width, contentHeight);
        Widgets.DrawBoxSolid(box, new Color(0.05f, 0.05f, 0.05f, 0.55f));

        var textRect = box.ContractedBy(4f);
        GUI.SetNextControlName("RimTalk_DebugDetails_TextArea");
        GUI.TextArea(textRect, content, _monoTinyStyle);

        y += contentHeight;
    }

    private void DrawGroupedPawnTable(Rect rect)
    {
        if (_pawnStates == null || !_pawnStates.Any())
            return;

        float viewWidth = rect.width - 16f;
        float totalHeight = CalculateGroupedTableHeight(viewWidth);
        var viewRect = new Rect(0, 0, viewWidth, totalHeight);

        Widgets.BeginScrollView(rect, ref _tableScrollPosition, viewRect);

        float responseColumnWidth = CalculateGroupedResponseColumnWidth(viewRect.width);

        DrawGroupedHeader(new Rect(0, 0, viewRect.width, HeaderHeight), responseColumnWidth);
        float currentY = HeaderHeight;

        var sortedPawns = GetSortedPawnStates().ToList();
        for (int i = 0; i < sortedPawns.Count; i++)
        {
            var pawnState = sortedPawns[i];
            string pawnKey = pawnState.Pawn.LabelShort;
            bool isExpanded = _expandedPawns.Contains(pawnKey);

            var rowRect = new Rect(0, currentY, viewRect.width, RowHeight);
            if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

            float currentX = 0;
            Widgets.Label(new Rect(rowRect.x + 5, rowRect.y + 3, 15, 15), isExpanded ? "-" : "+");
            currentX += GroupedExpandIconWidth;

            var pawnNameRect = new Rect(currentX, rowRect.y, GroupedPawnNameWidth, RowHeight);
            UIUtil.DrawClickablePawnName(pawnNameRect, pawnKey, pawnState.Pawn);
            currentX += GroupedPawnNameWidth + ColumnPadding;

            string lastResponse = GetLastResponseForPawn(pawnKey);
            Widgets.Label(new Rect(currentX, rowRect.y, responseColumnWidth, RowHeight), lastResponse);
            currentX += responseColumnWidth + ColumnPadding;

            bool canTalk = pawnState.CanGenerateTalk();
            string statusText = canTalk
                ? "RimTalk.DebugWindow.StatusReady".Translate()
                : "RimTalk.DebugWindow.StatusBusy".Translate();
            GUI.color = canTalk ? Color.green : Color.yellow;
            Widgets.Label(new Rect(currentX, rowRect.y, GroupedStatusWidth, RowHeight), statusText);
            GUI.color = Color.white;
            currentX += GroupedStatusWidth + ColumnPadding;

            Widgets.Label(new Rect(currentX, rowRect.y, GroupedLastTalkWidth, RowHeight),
                pawnState.LastTalkTick.ToString());
            currentX += GroupedLastTalkWidth + ColumnPadding;

            _talkLogsByPawn.TryGetValue(pawnKey, out var pawnRequests);
            var requestsWithTokens = pawnRequests?.Where(r => r.TokenCount != 0).ToList();
            Widgets.Label(new Rect(currentX, rowRect.y, GroupedRequestsWidth, RowHeight),
                (requestsWithTokens?.Count ?? 0).ToString());
            currentX += GroupedRequestsWidth + ColumnPadding;

            Widgets.Label(new Rect(currentX, rowRect.y, GroupedChattinessWidth, RowHeight),
                pawnState.TalkInitiationWeight.ToString("F2"));

            if (Widgets.ButtonInvisible(rowRect))
            {
                if (isExpanded) _expandedPawns.Remove(pawnKey);
                else _expandedPawns.Add(pawnKey);
            }

            currentY += RowHeight;

            if (isExpanded && _talkLogsByPawn.TryGetValue(pawnKey, out var requests) && requests.Any())
            {
                const float indentWidth = 20f;
                float innerWidth = viewRect.width - indentWidth;
                float innerResponseWidth = CalculateResponseColumnWidth(innerWidth, false);
                DrawRequestTableHeader(new Rect(indentWidth, currentY, innerWidth, HeaderHeight), innerResponseWidth,
                    false);
                currentY += HeaderHeight;

                foreach (var r in requests)
                {
                    DrawRequestRow(r, 0, currentY, innerWidth, indentWidth, innerResponseWidth, false);
                    currentY += RowHeight;
                }
            }
        }

        Widgets.EndScrollView();
    }

    private void DrawGroupedHeader(Rect rect, float responseColumnWidth)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.3f, 0.3f, 0.3f, 0.8f));
        Text.Font = GameFont.Tiny;
        GUI.color = Color.white;

        float currentX = GroupedExpandIconWidth;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedPawnNameWidth, rect.height), "Pawn");
        currentX += GroupedPawnNameWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, responseColumnWidth, rect.height), "Response");
        currentX += responseColumnWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedStatusWidth, rect.height), "Status");
        currentX += GroupedStatusWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedLastTalkWidth, rect.height), "Last Talk");
        currentX += GroupedLastTalkWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedRequestsWidth, rect.height), "Requests");
        currentX += GroupedRequestsWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedChattinessWidth, rect.height), "Chattiness");
    }

    private void DrawRequestTableHeader(Rect rect, float responseColumnWidth, bool showPawnColumn)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.25f, 0.9f));
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
        Widgets.Label(new Rect(currentX, rect.y, InteractionTypeColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderInteractionType".Translate());
        currentX += InteractionTypeColumnWidth + ColumnPadding;
        Widgets.Label(new Rect(currentX, rect.y, TimeColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTimeMs".Translate());
        currentX += TimeColumnWidth + ColumnPadding;
        Widgets.Label(new Rect(currentX, rect.y, TokensColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTokens".Translate());
        currentX += TokensColumnWidth + ColumnPadding;
        Widgets.Label(new Rect(currentX, rect.y, StateColumnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderState".Translate());
    }

    private void DrawSortableHeader(Rect rect, string column)
    {
        string translatedColumn = column.Translate();
        string arrow = (_sortColumn == column) ? (_sortAscending ? " ▲" : " ▼") : "";
        if (Widgets.ButtonInvisible(rect))
        {
            if (_sortColumn == column) _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = column;
                _sortAscending = true;
            }

            var settings = Settings.Get();
            settings.DebugSortColumn = _sortColumn;
            settings.DebugSortAscending = _sortAscending;
        }

        Widgets.Label(rect, translatedColumn + arrow);
    }

    private void DrawGraph(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.15f, 0.8f));

        var series = new[]
        {
            (data: Stats.TokensPerSecondHistory, color: new Color(1f, 1f, 1f, 0.7f),
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
                float y = (graphArea.y + graphArea.height - verticalPadding) - ((float)data[i] / maxVal * graphHeight);
                points.Add(new Vector2(x, y));

                if (data[i] > 0 && i > 0 && i % 6 == 0)
                {
                    GUI.color = color;
                    Widgets.Label(new Rect(x - 10, y - 15, 40, 20), data[i].ToString());
                    GUI.color = Color.white;
                }
            }

            for (int i = 0; i < points.Count - 1; i++) Widgets.DrawLine(points[i], points[i + 1], color, 2f);
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

        GUI.color = Color.gray;
        Widgets.Label(new Rect(contentRect.x, currentY, labelWidth, rowHeight),
            "RimTalk.DebugWindow.AIStatus".Translate());
        GUI.color = statusColor;
        Widgets.Label(new Rect(contentRect.x + labelWidth, currentY, 150f, rowHeight), _aiStatus);
        GUI.color = Color.white;
        currentY += rowHeight;

        void DrawStatRow(string label, string value)
        {
            GUI.color = Color.gray;
            Widgets.Label(new Rect(contentRect.x, currentY, labelWidth, rowHeight), label);
            GUI.color = Color.white;
            Widgets.Label(new Rect(contentRect.x + labelWidth, currentY, 150f, rowHeight), value);
            currentY += rowHeight;
        }

        DrawStatRow("RimTalk.DebugWindow.TotalCalls".Translate(), _totalCalls.ToString("N0"));
        DrawStatRow("RimTalk.DebugWindow.TotalTokens".Translate(), _totalTokens.ToString("N0"));
        DrawStatRow("RimTalk.DebugWindow.AvgCallsPerMin".Translate(), _avgCallsPerMin.ToString("F2"));
        DrawStatRow("RimTalk.DebugWindow.AvgTokensPerMin".Translate(), _avgTokensPerMin.ToString("F2"));
        DrawStatRow("RimTalk.DebugWindow.AvgTokensPerCall".Translate(), _avgTokensPerCall.ToString("F2"));

        GUI.EndGroup();
    }

    private void DrawBottomActions(Rect rect)
    {
        var listing = new Listing_Standard();
        listing.Begin(rect);

        listing.Gap(6f);

        var settings = Settings.Get();
        bool modEnabled = settings.IsEnabled;
        listing.CheckboxLabeled("RimTalk.DebugWindow.EnableRimTalk".Translate(), ref modEnabled);
        settings.IsEnabled = modEnabled;

        listing.Gap(12f);

        if (listing.ButtonText("RimTalk.DebugWindow.ModSettings".Translate()))
            Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
        listing.Gap(6f);

        if (listing.ButtonText("RimTalk.DebugWindow.Export".Translate()))
            UIUtil.ExportLogs(_requests);
        listing.Gap(6f);

        var prevColor = GUI.color;
        GUI.color = new Color(1f, 0.4f, 0.4f);
        if (listing.ButtonText("RimTalk.DebugWindow.Reset".Translate()))
            Reset();
        GUI.color = prevColor;
        listing.End();
    }

    // Helpers

    private IEnumerable<ApiLog> ApplyFilters(IEnumerable<ApiLog> source)
    {
        IEnumerable<ApiLog> q = source;

        if (!string.IsNullOrWhiteSpace(_pawnFilter))
        {
            var needle = _pawnFilter.Trim();
            q = q.Where(r => (r.Name ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(_textSearch))
        {
            var needle = _textSearch.Trim();
            q = q.Where(r =>
                (r.Prompt ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (r.Response ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                ((r.InteractionType ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        switch (_stateFilter)
        {
            case 1: q = q.Where(r => r.Response == null || r.SpokenTick == 0); break; 
            case 2: q = q.Where(r => r.SpokenTick == -1); break;
            case 3: q = q.Where(r => r.Response != null && r.SpokenTick > 0); break;
        }

        return q;
    }

    private void HandleGlobalClicks(Rect inRect)
    {
        if (Event.current.type == EventType.MouseDown && inRect.Contains(Event.current.mousePosition))
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (focused == ControlNamePawnFilter || focused == ControlNameTextSearch)
            {
                GUI.FocusControl(null);
            }
        }
    }

    private string DrawSearchField(Rect rect, string text, string placeholder, string controlName)
    {
        GUI.SetNextControlName(controlName);
        string result = Widgets.TextField(rect, text);

        if (string.IsNullOrEmpty(result))
        {
            var prevColor = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            Text.Anchor = TextAnchor.MiddleLeft;
            var labelRect = new Rect(rect.x + 5f, rect.y, rect.width - 5f, rect.height);
            Widgets.Label(labelRect, placeholder);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prevColor;
        }

        return result;
    }

    private string GetStateLabel(int state)
    {
        return state switch
        {
            1 => "RimTalk.DebugWindow.StatePending".Translate(),
            2 => "RimTalk.DebugWindow.StateIgnored".Translate(),
            3 => "RimTalk.DebugWindow.StateSpoken".Translate(),
            _ => "RimTalk.DebugWindow.StateAll".Translate()
        };
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
                        _talkLogsByPawn.ContainsKey(p.Pawn.LabelShort) ? _talkLogsByPawn[p.Pawn.LabelShort].Count : 0)
                    : _pawnStates.OrderByDescending(p =>
                        _talkLogsByPawn.ContainsKey(p.Pawn.LabelShort) ? _talkLogsByPawn[p.Pawn.LabelShort].Count : 0);
            case "Response":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => GetLastResponseForPawn(p.Pawn.LabelShort))
                    : _pawnStates.OrderByDescending(p => GetLastResponseForPawn(p.Pawn.LabelShort));
            case "Status":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.CanDisplayTalk())
                    : _pawnStates.OrderByDescending(p => p.CanDisplayTalk());
            case "Last Talk":
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
        float fixedWidth = TimestampColumnWidth + TimeColumnWidth + TokensColumnWidth + StateColumnWidth +
                           InteractionTypeColumnWidth;
        int columnGaps = 6;
        if (includePawnColumn)
        {
            fixedWidth += PawnColumnWidth;
            columnGaps++;
        }

        float availableWidth = totalWidth - fixedWidth - (ColumnPadding * columnGaps);
        return Mathf.Max(40f, availableWidth);
    }

    private float CalculateGroupedResponseColumnWidth(float totalWidth)
    {
        float fixedWidth = GroupedExpandIconWidth + GroupedPawnNameWidth + GroupedRequestsWidth + GroupedLastTalkWidth +
                           GroupedChattinessWidth + GroupedStatusWidth;
        int columnGaps = 6;
        float availableWidth = totalWidth - fixedWidth - (ColumnPadding * columnGaps);
        return Math.Max(150f, availableWidth);
    }

    private float CalculateGroupedTableHeight(float viewWidth)
    {
        float height = HeaderHeight + (_pawnStates.Count * RowHeight);
        foreach (var pawnState in _pawnStates)
        {
            var pawnKey = pawnState.Pawn.LabelShort;
            if (_expandedPawns.Contains(pawnKey) && _talkLogsByPawn.TryGetValue(pawnKey, out var requests))
            {
                height += HeaderHeight;
                height += requests.Count * RowHeight;
            }
        }

        return height + 50f;
    }

    private string GetLastResponseForPawn(string pawnKey)
    {
        if (_talkLogsByPawn.TryGetValue(pawnKey, out var logs) && logs.Any())
        {
            return logs.Last().Response ?? _generating;
        }

        return "";
    }

    private string BuildCopyContexts(ApiLog r)
    {
        if (r.Contexts == null || r.Contexts.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < r.Contexts.Count; i++)
        {
            sb.AppendLine($"--- Context {i + 1} ---");
            sb.AppendLine(r.Contexts[i]);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildCopyAll(ApiLog r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Timestamp: {r.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Pawn: {r.Name ?? "-"}");
        sb.AppendLine($"InteractionType: {r.InteractionType ?? "-"}");
        sb.AppendLine($"ElapsedMs: {r.ElapsedMs}");
        sb.AppendLine($"TokenCount: {r.TokenCount}");
        sb.AppendLine($"SpokenTick: {r.SpokenTick}");
        sb.AppendLine();
        sb.AppendLine("=== Prompt ===");
        sb.AppendLine(r.Prompt ?? string.Empty);
        sb.AppendLine();
        sb.AppendLine("=== Response ===");
        sb.AppendLine(r.Response ?? string.Empty);
        var ctx = BuildCopyContexts(r);
        if (!string.IsNullOrWhiteSpace(ctx))
        {
            sb.AppendLine();
            sb.AppendLine("=== Contexts ===");
            sb.AppendLine(ctx);
        }

        return sb.ToString().TrimEnd();
    }

    private void Reset()
    {
        TalkHistory.Clear();
        Stats.Reset();
        ApiHistory.Clear();
        UpdateData();
        Messages.Message("Conversation history cleared.", MessageTypeDefOf.TaskCompletion, false);
    }
}