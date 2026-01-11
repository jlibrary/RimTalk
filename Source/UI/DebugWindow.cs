using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Cache = RimTalk.Data.Cache;
using State = RimTalk.Data.ApiLog.State;

namespace RimTalk.UI;

public class DebugWindow : Window
{
    private enum DebugViewMode
    {
        MainTable,
        GroupedByPawn,
        ActiveRequests
    }

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

    // Active Request Column Widths
    private const float ARTimeWidth = 65f;
    private const float ARInitiatorWidth = 80f;
    private const float ARRecipientWidth = 80f;
    private const float ARTypeWidth = 60f;
    private const float ARElapsedWidth = 60f;
    private const float ARStatusWidth = 60f;

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
    private Vector2 _activeRequestsScrollPosition;
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
    private List<TalkRequest> _cachedActiveViewList = [];
    private readonly Dictionary<string, List<ApiLog>> _talkLogsByPawn = new();

    // Controls
    private int _maxRows;
    private string _pawnFilter;
    private string _textSearch;
    private State _stateFilter;
    private RequestStatus? _activeRequestStatusFilter;
    private ApiLog _selectedLog;

    // Temporary Editable State
    private Guid _selectedRequestIdForTemp = Guid.Empty;
    private string _tempResponse;
    private string _tempPromptSegmentsText;
    private List<PromptMessageSegment> _tempPromptSegments = [];
    private readonly HashSet<int> _expandedPromptSegmentIndices = new();

    private DebugViewMode _viewMode;
    private string _sortColumn;
    private bool _sortAscending;
    private readonly List<string> _expandedPawns;

    // Focus Control Names
    private const string ControlNamePawnFilter = "PawnFilterField";
    private const string ControlNameTextSearch = "TextSearchField";
    private const string ControlNameDetailResponse = "DetailResponseField";
    private const string ControlNameDetailMessages = "DetailMessagesField";

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
        closeOnAccept = false;
        closeOnCancel = true;
        preventCameraMotion = false;

        var settings = Settings.Get();
        _viewMode = 0;
        _sortColumn = settings.DebugSortColumn;
        _sortAscending = settings.DebugSortAscending;
        _expandedPawns = [];

        _maxRows = 500;
        _pawnFilter = string.Empty;
        _textSearch = string.Empty;
        _stateFilter = State.None;
        _activeRequestStatusFilter = null;
    }

    public override Vector2 InitialSize => new(1100f, 600f);

    public override void PreClose()
    {
        base.PreClose();
        var settings = Settings.Get();
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
        if (_selectedLog != null && _requests.All(r => r.Id != _selectedLog.Id))
        {
            _selectedLog = null;
            _selectedRequestIdForTemp = Guid.Empty;
        }

        var tempActiveList = new List<TalkRequest>();
        tempActiveList.AddRange(TalkRequestPool.GetAllActive());
        tempActiveList.AddRange(TalkRequestPool.GetHistory());
        if (_pawnStates != null)
            tempActiveList.AddRange(_pawnStates.SelectMany(state => state.TalkRequests));

        IEnumerable<TalkRequest> q = tempActiveList;

        if (!string.IsNullOrWhiteSpace(_pawnFilter))
        {
            var needle = _pawnFilter.Trim();
            q = q.Where(r =>
                (r.Initiator?.LabelShort ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (r.Recipient?.LabelShort ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        if (!string.IsNullOrWhiteSpace(_textSearch))
        {
            var needle = _textSearch.Trim();
            q = q.Where(r => (r.Prompt ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (_activeRequestStatusFilter.HasValue)
        {
            q = q.Where(r => r.Status == _activeRequestStatusFilter.Value);
        }

        _cachedActiveViewList = q.ToList();
        _cachedActiveViewList.Sort((a, b) => a.CreatedTime.CompareTo(b.CreatedTime));
    }

    private void DrawLeftPane(Rect rect)
    {
        // Filter Bar (Integrated at top)
        var filterRect = new Rect(rect.x, rect.y, rect.width, FilterBarHeight);
        DrawInternalFilterBar(filterRect);

        // Table Area
        var tableRect = new Rect(rect.x, rect.y + FilterBarHeight, rect.width, rect.height - FilterBarHeight);

        switch (_viewMode)
        {
            case DebugViewMode.ActiveRequests:
                DrawActiveRequestsTable(tableRect);
                break;
            case DebugViewMode.GroupedByPawn:
                DrawGroupedPawnTable(tableRect);
                break;
            case DebugViewMode.MainTable:
            default:
                DrawConsoleTable(tableRect);
                break;
        }
    }

    private void DrawInternalFilterBar(Rect rect)
    {
        float y = rect.y + 3f;
        float height = 24f;
        float gap = 5f;
        float startX = rect.x;
        float viewDropdownWidth = 90f;
        float statusWidth = 100f;
        float limitWidth = 90f;
        float totalFixedSpace = viewDropdownWidth + statusWidth + limitWidth + (4 * gap);
        float flexSpace = rect.width - totalFixedSpace;
        if (flexSpace < 50f) flexSpace = 50f;
        float pawnFilterWidth = flexSpace * 0.35f;
        float textSearchWidth = flexSpace * 0.65f;

        float currentX = startX;

        // 1. View Mode Dropdown
        var viewBtnRect = new Rect(currentX, y, viewDropdownWidth, height);
        string viewLabel = _viewMode switch
        {
            DebugViewMode.MainTable => "RimTalk.DebugWindow.ViewByTime".Translate(),
            DebugViewMode.GroupedByPawn => "RimTalk.DebugWindow.ViewByPawn".Translate(),
            DebugViewMode.ActiveRequests => "RimTalk.DebugWindow.ViewTalkRequests".Translate(),
            _ => _viewMode.ToString()
        };

        if (Widgets.ButtonText(viewBtnRect, viewLabel))
        {
            var options = new List<FloatMenuOption>
            {
                new("RimTalk.DebugWindow.ViewByTime".Translate(), () => _viewMode = DebugViewMode.MainTable),
                new("RimTalk.DebugWindow.ViewByPawn".Translate(), () => _viewMode = DebugViewMode.GroupedByPawn),
                new("RimTalk.DebugWindow.ViewTalkRequests".Translate(), () => _viewMode = DebugViewMode.ActiveRequests)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        currentX += viewDropdownWidth + gap;

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
        if (_viewMode == DebugViewMode.ActiveRequests)
        {
            string label = _activeRequestStatusFilter.HasValue
                ? $"RimTalk.DebugWindow.State{_activeRequestStatusFilter.Value}".Translate()
                : "RimTalk.DebugWindow.StateAll".Translate();

            if (Widgets.ButtonText(stateBtnRect, label))
            {
                var options = new List<FloatMenuOption>
                {
                    new("RimTalk.DebugWindow.StateAll".Translate(), () => _activeRequestStatusFilter = null)
                };
                options.AddRange(Enum.GetValues(typeof(RequestStatus))
                    .Cast<RequestStatus>()
                    .Select(s => new FloatMenuOption($"RimTalk.DebugWindow.State{s}".Translate(),
                        () => _activeRequestStatusFilter = s)));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        else
        {
            if (Widgets.ButtonText(stateBtnRect, _stateFilter.GetLabel()))
            {
                var options = Enum.GetValues(typeof(State))
                    .Cast<State>()
                    .Select(filter => new FloatMenuOption(filter.GetLabel(), () => _stateFilter = filter))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
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

    private void DrawActiveRequestsTable(Rect rect)
    {
        // Header
        float fixedWidth = ARTimeWidth + ARInitiatorWidth + ARRecipientWidth + ARTypeWidth + ARElapsedWidth +
                           ARStatusWidth + (ColumnPadding * 6);
        float promptWidth = Mathf.Max(50f, rect.width - fixedWidth - 16f);

        DrawActiveRequestHeader(new Rect(rect.x, rect.y, rect.width, HeaderHeight), promptWidth);

        var scrollRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
        float viewWidth = scrollRect.width - 16f;
        float viewHeight = _cachedActiveViewList.Count * RowHeight;
        var viewRect = new Rect(0, 0, viewWidth, viewHeight);

        // Auto-scroll logic
        float maxScroll = Mathf.Max(0f, viewHeight - scrollRect.height);
        if (_stickToBottom)
            _activeRequestsScrollPosition.y = maxScroll;

        Widgets.BeginScrollView(scrollRect, ref _activeRequestsScrollPosition, viewRect);

        // Unstick if user manually scrolls up
        if (_stickToBottom && _activeRequestsScrollPosition.y < maxScroll - 1f)
            _stickToBottom = false;

        // Iterate the cached list
        for (int i = 0; i < _cachedActiveViewList.Count; i++)
        {
            DrawActiveRequestRow(_cachedActiveViewList[i], i, i * RowHeight, viewWidth, promptWidth);
        }

        Widgets.EndScrollView();

        DrawStickToBottomOverlay(scrollRect, maxScroll, ref _activeRequestsScrollPosition);
    }

    private void DrawActiveRequestHeader(Rect rect, float promptWidth)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.25f, 0.9f));
        Text.Font = GameFont.Tiny;
        float currentX = rect.x + 5f;

        Widgets.Label(new Rect(currentX, rect.y, ARTimeWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTimestamp".Translate());
        currentX += ARTimeWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, ARInitiatorWidth, rect.height),
            "RimTalk.DebugWindow.HeaderInitiator".Translate());
        currentX += ARInitiatorWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, ARRecipientWidth, rect.height),
            "RimTalk.DebugWindow.HeaderRecipient".Translate());
        currentX += ARRecipientWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, promptWidth, rect.height),
            "RimTalk.DebugWindow.HeaderPrompt".Translate());
        currentX += promptWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, ARTypeWidth, rect.height),
            "RimTalk.DebugWindow.HeaderType".Translate());
        currentX += ARTypeWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, ARElapsedWidth, rect.height),
            "RimTalk.DebugWindow.HeaderElapsed".Translate());
        currentX += ARElapsedWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, ARStatusWidth, rect.height),
            "RimTalk.DebugWindow.HeaderStatus".Translate());
    }

    private void DrawActiveRequestRow(TalkRequest req, int index, float rowY, float width, float promptWidth)
    {
        var rowRect = new Rect(0, rowY, width, RowHeight);

        if (index % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

        float currentX = 5f;
        int currentTick = GenTicks.TicksGame;

        // 1. Time (HH:mm:ss to match main table)
        Widgets.Label(new Rect(currentX, rowY, ARTimeWidth, RowHeight), req.CreatedTime.ToString("HH:mm:ss"));
        currentX += ARTimeWidth + ColumnPadding;

        // 2. Initiator
        var initRect = new Rect(currentX, rowY, ARInitiatorWidth, RowHeight);
        string initName = req.Initiator?.LabelShort ?? "-";
        UIUtil.DrawClickablePawnName(initRect, initName, req.Initiator);
        currentX += ARInitiatorWidth + ColumnPadding;

        // 3. Recipient
        var recRect = new Rect(currentX, rowY, ARRecipientWidth, RowHeight);
        if (req.Recipient != null && req.Recipient != req.Initiator)
            UIUtil.DrawClickablePawnName(recRect, req.Recipient.LabelShort, req.Recipient);
        else
            Widgets.Label(recRect, "-");
        currentX += ARRecipientWidth + ColumnPadding;

        // 4. Prompt (Truncated)
        string prompt = req.Prompt ?? "";
        Widgets.Label(new Rect(currentX, rowY, promptWidth, RowHeight), prompt);
        currentX += promptWidth + ColumnPadding;

        // 5. Type
        Widgets.Label(new Rect(currentX, rowY, ARTypeWidth, RowHeight), req.TalkType.ToString());
        currentX += ARTypeWidth + ColumnPadding;

        // 6. Elapsed
        int ticksElapsed = Math.Max(0,
            (req.Status == RequestStatus.Pending || req.FinishedTick == -1 ? GenTicks.TicksGame : req.FinishedTick) -
            req.CreatedTick);
        string elapsedStr = $"{ticksElapsed / 60}s";
        Widgets.Label(new Rect(currentX, rowY, ARElapsedWidth, RowHeight), elapsedStr);
        currentX += ARElapsedWidth + ColumnPadding;

        // 7. Status
        Color c = GUI.color;
        if (req.Status == RequestStatus.Expired) GUI.color = Color.gray;
        else if (req.Status == RequestStatus.Processed) GUI.color = Color.green;
        else GUI.color = Color.yellow;

        string translationKey = $"RimTalk.DebugWindow.State{req.Status}";
        Widgets.Label(new Rect(currentX, rowY, ARStatusWidth, RowHeight), translationKey.Translate());
        GUI.color = c;
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

        // Determine blocked area for input
        const float btnSize = 30f;
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

        // Use Helper Method
        DrawStickToBottomOverlay(scrollRect, maxScroll, ref _tableScrollPosition);
    }

    private void DrawRequestRow(ApiLog request, int rowIndex, float rowY, float totalWidth, float xOffset,
        float responseColumnWidth, bool showPawnColumn, bool inputBlocked = false)
    {
        var rowRect = new Rect(xOffset, rowY, totalWidth, RowHeight);
        if (rowIndex % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

        bool isSelected = _selectedLog != null && _selectedLog.Id == request.Id;
        if (isSelected) Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.25f, 0.35f, 0.45f));

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

        string resp = request.Response ?? _generating;
        Widgets.Label(new Rect(currentX, rowRect.y, responseColumnWidth, RowHeight), resp);
        currentX += responseColumnWidth + ColumnPadding;

        string interactionType = request.InteractionType ?? "-";
        Widgets.Label(new Rect(currentX, rowRect.y, InteractionTypeColumnWidth, RowHeight), interactionType);
        currentX += InteractionTypeColumnWidth + ColumnPadding;

        string elapsedMsText = request.Response == null
            ? "" : request.ElapsedMs == 0 ? "-" : request.ElapsedMs.ToString();
        Widgets.Label(new Rect(currentX, rowRect.y, TimeColumnWidth, RowHeight), elapsedMsText);
        currentX += TimeColumnWidth + ColumnPadding;

        int count = request.Payload?.TokenCount ?? 0;
        string tokenCountText = count != 0
            ? count.ToString()
            : request.IsFirstDialogue ? "-" : "";
        Widgets.Label(new Rect(currentX, rowRect.y, TokensColumnWidth, RowHeight), tokenCountText);
        currentX += TokensColumnWidth + ColumnPadding;

        State stateFilter = request.GetState();
        GUI.color = stateFilter.GetColor();
        Widgets.Label(new Rect(currentX, rowRect.y, StateColumnWidth, RowHeight), stateFilter.GetLabel());
        GUI.color = Color.white;

        if (!inputBlocked)
        {
            TooltipHandler.TipRegion(rowRect, "RimTalk.DebugWindow.TooltipSelectForDetails".Translate());
        }

        if (!inputBlocked && Widgets.ButtonInvisible(rowRect))
        {
            _selectedLog = request;
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

        if (_selectedLog == null)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inner.width, 50f), "RimTalk.DebugWindow.SelectRowHint".Translate());
            GUI.color = Color.white;
            GUI.EndGroup();
            return;
        }

        // Initialize temp strings if a new row is selected
        if (_selectedLog.Id != _selectedRequestIdForTemp)
        {
            _selectedRequestIdForTemp = _selectedLog.Id;
            _tempResponse = _selectedLog.Response ?? string.Empty;
            _tempPromptSegments = ResolvePromptSegments(_selectedLog);
            _tempPromptSegmentsText = FormatPromptSegments(_tempPromptSegments);
            _expandedPromptSegmentIndices.Clear();
        }
        else if (string.IsNullOrEmpty(_tempResponse) && !string.IsNullOrEmpty(_selectedLog.Response))
        {
            _tempResponse = _selectedLog.Response;
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

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(0f, y, inner.width, 18f), header.ToString());
        GUI.color = Color.white;
        y += 22f;

        float buttonsRowH = 24f;
        float btnW = 88f;
        float btnX = 0f;

        // Copy All Button
        if (Widgets.ButtonText(new Rect(btnX, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyAll".Translate()))
        {
            GUIUtility.systemCopyBuffer = _selectedLog.ToString();
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        // API Log Button
        if (_selectedLog.GetState() != State.None)
        {
            btnX += btnW + 6f;
            Rect reportRect = new Rect(btnX, y, btnW, buttonsRowH);
            if (Widgets.ButtonText(reportRect, "RimTalk.DebugWindow.ApiLog".Translate()))
            {
                GUIUtility.systemCopyBuffer = _selectedLog.Payload?.ToString();
                Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
        }

        // Resend Button
        GUI.enabled = _selectedLog.Channel != Channel.User;
        btnX += btnW + 6f;
        var prevResendColor = GUI.color;
        GUI.color = new Color(0.6f, 0.9f, 0.6f);
        Rect resendRect = new Rect(btnX, y, btnW, buttonsRowH);
        if (Widgets.ButtonText(resendRect, "RimTalk.DebugWindow.Resend".Translate()))
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
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

        // Calculate heights dynamically based on current content
        float viewWidth = scrollOuter.width - 16f;
        float textAreaWidth = viewWidth - 8f;
        float respH = Mathf.Max(40f,
            _monoTinyStyle.CalcHeight(new GUIContent(_tempResponse), textAreaWidth) + 10f);
        float msgH = CalculatePromptSegmentsHeight(_tempPromptSegments, viewWidth);

        var viewH = headerH + respH + blockSpacing +
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

        // Prompt Messages Block (segmented, collapsible)
        DrawPromptMessagesBlock(ref yy, view.width, "RimTalk.DebugWindow.PromptMessages".Translate(),
            _tempPromptSegments, _tempPromptSegmentsText);

        Widgets.EndScrollView();

        GUI.EndGroup();
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

        const float messageHeaderHeight = 22f;
        const float messageSpacing = 6f;

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            string preview = GetPromptMessagePreview(segment.Content);
            string roleLabel = GetRoleLabel(segment.Role);
            string entryName = string.IsNullOrWhiteSpace(segment.EntryName) ? "Entry" : segment.EntryName;
            string state = _expandedPromptSegmentIndices.Contains(i) ? "[-]" : "[+]";
            string label = $"{state} {i + 1}. {entryName} ({roleLabel}): {preview}";

            var headerRect = new Rect(0f, y, width, messageHeaderHeight);
            Widgets.DrawBoxSolid(headerRect, new Color(0.12f, 0.12f, 0.12f, 0.6f));
            Widgets.Label(new Rect(headerRect.x + 6f, headerRect.y + 2f, headerRect.width - 12f, headerRect.height),
                label);

            if (Widgets.ButtonInvisible(headerRect))
            {
                if (_expandedPromptSegmentIndices.Contains(i))
                    _expandedPromptSegmentIndices.Remove(i);
                else
                    _expandedPromptSegmentIndices.Add(i);
            }

            y += messageHeaderHeight;

            if (_expandedPromptSegmentIndices.Contains(i))
            {
                string safeContent = segment.Content ?? "";
                float bodyHeight = Mathf.Max(40f,
                    _monoTinyStyle.CalcHeight(new GUIContent(safeContent), width - 8f) + 10f);
                var bodyRect = new Rect(0f, y, width, bodyHeight);
                Widgets.DrawBoxSolid(bodyRect, new Color(0.05f, 0.05f, 0.05f, 0.55f));

                var textRect = bodyRect.ContractedBy(4f);
                GUI.TextArea(textRect, safeContent, _monoTinyStyle);
                y += bodyHeight;
            }

            y += messageSpacing;
        }
    }

    private void DrawGroupedPawnTable(Rect rect)
    {
        if (_pawnStates == null || !_pawnStates.Any())
            return;

        float viewWidth = rect.width - 16f;
        float totalHeight = CalculateGroupedTableHeight(viewWidth);
        var viewRect = new Rect(0, 0, viewWidth, totalHeight);

        // Auto-scroll logic (Using main _tableScrollPosition)
        float maxScroll = Mathf.Max(0f, totalHeight - rect.height);
        if (_stickToBottom)
            _tableScrollPosition.y = maxScroll;

        Widgets.BeginScrollView(rect, ref _tableScrollPosition, viewRect);

        // Unstick if user manually scrolls up
        if (_stickToBottom && _tableScrollPosition.y < maxScroll - 1f)
            _stickToBottom = false;

        float responseColumnWidth = CalculateGroupedResponseColumnWidth(viewRect.width);

        DrawGroupedHeader(new Rect(0, 0, viewRect.width, HeaderHeight), responseColumnWidth);
        float currentY = HeaderHeight;

        var sortedPawns = GetSortedPawnStates().ToList();

        // Filters
        if (!string.IsNullOrWhiteSpace(_pawnFilter))
        {
            var needle = _pawnFilter.Trim();
            sortedPawns = sortedPawns.Where(p =>
                (p.Pawn.LabelShort ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        if (!string.IsNullOrWhiteSpace(_textSearch))
        {
            var needle = _textSearch.Trim();
            sortedPawns = sortedPawns.Where(p =>
                    GetLastResponseForPawn(p.Pawn.LabelShort).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

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
            var initiatingRequests = pawnRequests?.Where(r => r.IsFirstDialogue).ToList();
            Widgets.Label(new Rect(currentX, rowRect.y, GroupedRequestsWidth, RowHeight),
                (initiatingRequests?.Count ?? 0).ToString());
            currentX += GroupedRequestsWidth + ColumnPadding;

            Widgets.Label(new Rect(currentX, rowRect.y, GroupedChattinessWidth, RowHeight),
                pawnState.TalkInitiationWeight.ToString("F2"));

            if (Widgets.ButtonInvisible(rowRect))
            {
                if (isExpanded) _expandedPawns.Remove(pawnKey);
                else _expandedPawns.Add(pawnKey);
            }

            currentY += RowHeight;

            // Expanded Inner List Logic
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

        DrawStickToBottomOverlay(rect, maxScroll, ref _tableScrollPosition);
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
            "RimTalk.DebugWindow.HeaderType".Translate());
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

    private void DrawStickToBottomOverlay(Rect scrollRect, float maxScroll, ref Vector2 scrollPosition)
    {
        if (_stickToBottom) return;

        const float btnSize = 30f;
        // Position the button inside the scroll rect, bottom-right
        var overlayRect = new Rect(scrollRect.xMax - btnSize - 20f, scrollRect.yMax - btnSize - 5f, btnSize, btnSize);

        bool isMouseOver = Mouse.IsOver(overlayRect);
        Color bgColor = isMouseOver
            ? new Color(0.3f, 0.3f, 0.3f, 1f)
            : new Color(0, 0, 0, 0.6f);

        Widgets.DrawBoxSolid(overlayRect, bgColor);

        if (Widgets.ButtonInvisible(overlayRect))
        {
            _stickToBottom = true;
            scrollPosition.y = maxScroll;
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

    private void DrawSortableHeader(Rect rect, string column)
    {
        string translatedColumn = column.Translate();
        string arrow = _sortColumn == column ? (_sortAscending ? " ▲" : " ▼") : "";
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
        GUI.color = Color.gray;
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
        else statusColor = Color.gray;

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
        if (listing.ButtonText("RimTalk.DebugWindow.ResetLogs".Translate()))
            Reset();
        GUI.color = prevColor;
        listing.End();
    }

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
                (r.TalkRequest.Prompt ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (r.Response ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (r.InteractionType ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (_stateFilter != State.None)
            q = q.Where(r => r.GetState() == _stateFilter);

        return q;
    }

    private void HandleGlobalClicks(Rect inRect)
    {
        if (Event.current.type == EventType.MouseDown && inRect.Contains(Event.current.mousePosition))
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (focused == ControlNamePawnFilter ||
                focused == ControlNameTextSearch ||
                focused == ControlNameDetailResponse ||
                focused == ControlNameDetailMessages)
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
                        _talkLogsByPawn.TryGetValue(p.Pawn.LabelShort, out var logs) 
                            ? logs.Count(r => r.IsFirstDialogue) : 0)
                    : _pawnStates.OrderByDescending(p =>
                        _talkLogsByPawn.TryGetValue(p.Pawn.LabelShort, out var logs) 
                            ? logs.Count(r => r.IsFirstDialogue) : 0);

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

    private static List<PromptMessageSegment> ResolvePromptSegments(ApiLog log)
    {
        var request = log?.TalkRequest;
        if (request?.PromptMessageSegments != null && request.PromptMessageSegments.Count > 0)
            return request.PromptMessageSegments;

        var segments = new List<PromptMessageSegment>();
        if (request == null) return segments;

        if (request.PromptMessages != null && request.PromptMessages.Count > 0)
        {
            for (int i = 0; i < request.PromptMessages.Count; i++)
            {
                var (role, content) = request.PromptMessages[i];
                segments.Add(new PromptMessageSegment($"message-{i}", $"Message {i + 1}", role, content));
            }
            return segments;
        }

        var instruction = $"{Constant.Instruction}\n{request.Context}";
        segments.Add(new PromptMessageSegment("legacy-instruction", "Legacy Instruction", Role.System, instruction));

        if (request.Initiator != null)
        {
            foreach (var (role, message) in TalkHistory.GetMessageHistory(request.Initiator))
            {
                segments.Add(new PromptMessageSegment("legacy-history", "Legacy Chat History", role, message));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Prompt))
            segments.Add(new PromptMessageSegment("legacy-prompt", "Legacy Prompt", Role.User, request.Prompt));

        return segments;
    }

    private float CalculatePromptSegmentsHeight(List<PromptMessageSegment> segments, float width)
    {
        float height = 18f;
        if (segments == null || segments.Count == 0)
            return height + 20f;

        const float messageHeaderHeight = 22f;
        const float messageSpacing = 6f;

        for (int i = 0; i < segments.Count; i++)
        {
            height += messageHeaderHeight;
            if (_expandedPromptSegmentIndices.Contains(i))
            {
                string content = segments[i].Content ?? "";
                height += Mathf.Max(40f,
                    _monoTinyStyle.CalcHeight(new GUIContent(content), width - 8f) + 10f);
            }
            height += messageSpacing;
        }

        return height;
    }

    private static string GetPromptMessagePreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "(empty)";

        var firstLine = content.Replace("\r", "").Split('\n')[0].Trim();
        if (firstLine.Length > 80)
            firstLine = firstLine.Substring(0, 77) + "...";

        return firstLine;
    }

    private static string GetRoleLabel(Role role)
    {
        return role == Role.AI ? "Assistant" : role.ToString();
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
            return "(No prompt segments available)";

        var sb = new StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var roleLabel = segment.Role == Role.AI ? "assistant" : segment.Role.ToString().ToLowerInvariant();
            var entryName = string.IsNullOrWhiteSpace(segment.EntryName) ? "Entry" : segment.EntryName;
            sb.Append("Entry: ");
            sb.AppendLine(entryName);
            sb.Append("Role: ");
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
        if (AIService.IsBusy())
        {
            Messages.Message("RimTalk.DebugWindow.ResendError".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        TalkRequest debugRequest = _selectedLog.TalkRequest.Clone();
        // Note: Resend uses the original PromptMessages from the TalkRequest
        if (_selectedLog.Channel == Channel.Stream)
            TalkService.GenerateTalkDebug(debugRequest);
        else if (_selectedLog.Channel == Channel.Query)
            Task.Run(() => AIService.Query<PersonalityData>(debugRequest));

        Messages.Message("RimTalk.DebugWindow.ResendSuccess".Translate(), MessageTypeDefOf.TaskCompletion);
    }

    private void Reset()
    {
        TalkHistory.Clear();
        TalkRequestPool.ClearHistory();
        Stats.Reset();
        ApiHistory.Clear();
        UpdateData();
        Messages.Message("Conversation history cleared.", MessageTypeDefOf.TaskCompletion, false);
    }
}
