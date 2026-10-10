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

public partial class DebugWindow : Window
{
    private enum DebugViewMode
    {
        MainTable,
        GroupedByPawn,
        ActiveRequests,
        MemoryLog
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

    // Memory Log Column Widths
    private const float MLTimeWidth = 60f;
    private const float MLTickWidth = 60f;
    private const float MLPawnWidth = 75f;
    private const float MLTargetWidth = 75f;
    private const float MLTypeWidth = 85f;
    private const float MLWeightWidth = 55f;

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
    private Vector2 _memoryLogsScrollPosition;
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
    private List<MemoryLogEntry> _cachedMemoryLogs = [];
    private readonly Dictionary<string, List<ApiLog>> _talkLogsByPawn = new();

    // Controls
    private int _maxRows;
    private string _pawnFilter;
    private string _textSearch;
    private State _stateFilter;
    private RequestStatus? _activeRequestStatusFilter;
    private MemoryChangeType? _memoryTypeFilter;
    private ApiLog _selectedLog;
    private MemoryLogEntry _selectedMemoryLog;

    // Temporary Editable State
    private Guid _selectedRequestIdForTemp = Guid.Empty;
    private string _tempResponse;
    private string _tempPromptSegmentsText;
    private List<PromptMessageSegment> _tempPromptSegments = [];
    private readonly HashSet<int> _expandedPromptSegmentIndices = new();
    private Texture2D _cachedImageTexture;
    private string _cachedImageBase64;
    private Guid _cachedImageLogId = Guid.Empty;

    private DebugViewMode _viewMode;
    private string _sortColumn;
    private bool _sortAscending;
    private readonly List<string> _expandedPawns;

    // Focus Control Names
    private const string ControlNamePawnFilter = "PawnFilterField";
    private const string ControlNameTextSearch = "TextSearchField";
    private const string ControlNameDetailResponse = "DetailResponseField";
    private const string ControlNameDetailMessages = "DetailMessagesField";
    private const string ControlNameDetailPromptMessages = "DetailPromptMessagesField";

    // Styles
    private GUIStyle _contextStyle;
    private GUIStyle _monoTinyStyle;

    public DebugWindow()
    {
        doCloseX = true;
        closeOnClickedOutside = false;
        draggable = true;
        resizeable = true;
        closeOnAccept = false;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;

        var settings = Settings.Get();
        _viewMode = 0;
        _sortColumn = settings.DebugSortColumn;
        _sortAscending = settings.DebugSortAscending;
        _expandedPawns = [];

        _maxRows = ApiHistory.MaxHistoryCount;
        _pawnFilter = string.Empty;
        _textSearch = string.Empty;
        _stateFilter = State.None;
        _activeRequestStatusFilter = null;
    }

    public override Vector2 InitialSize => new(1100f, 600f);

    public override void PreClose()
    {
        base.PreClose();
        if (_cachedImageTexture != null)
        {
            Object.Destroy(_cachedImageTexture);
            _cachedImageTexture = null;
        }
        _cachedImageLogId = Guid.Empty;

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
        Text.WordWrap = false;
        Text.Font = GameFont.Tiny;
        try
        {
            HandleGlobalClicks(inRect);
            UpdateData();

            const float bottomSectionHeight = 60f;
            const float spacing = 10f;

            float contentHeight = inRect.height - bottomSectionHeight - spacing;

            // LEFT PANE (Table + Filters) vs RIGHT PANE (Details)
            float leftWidth = inRect.width * 0.60f - (spacing / 2);
            float rightWidth = inRect.width * 0.40f - (spacing / 2);

            var leftPaneRect = new Rect(inRect.x, inRect.y, leftWidth, contentHeight);
            var detailsRect = new Rect(leftPaneRect.xMax + spacing, inRect.y, rightWidth, contentHeight);

            DrawLeftPane(leftPaneRect);
            DrawDetailsPanel(detailsRect);

            // Bottom Section (Fixed button and stats panels, flexible graph)
            const float actionsWidth = 234f;
            const float statsWidth = 340f;
            var bottomRect = new Rect(inRect.x, leftPaneRect.yMax + spacing, inRect.width, bottomSectionHeight);
            var actionsRect = new Rect(bottomRect.xMax - actionsWidth, bottomRect.y, actionsWidth, bottomRect.height);
            var statsRect = new Rect(actionsRect.x - spacing - statsWidth, bottomRect.y, statsWidth, bottomRect.height);
            float graphWidth = Mathf.Max(100f, statsRect.x - bottomRect.x - spacing);
            var graphRect = new Rect(bottomRect.x, bottomRect.y, graphWidth, bottomRect.height);

            DrawGraph(graphRect);
            DrawStatsSection(statsRect);
            DrawBottomActions(actionsRect);
        }
        finally
        {
            Text.WordWrap = true;
            Text.Font = GameFont.Small;
        }
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

        IEnumerable<MemoryLogEntry> memQuery = MemoryHistory.GetAll();
        if (!string.IsNullOrWhiteSpace(_pawnFilter))
        {
            var needle = _pawnFilter.Trim();
            memQuery = memQuery.Where(m =>
                (m.SourcePawnName ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (m.TargetPawnName ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        if (!string.IsNullOrWhiteSpace(_textSearch))
        {
            var needle = _textSearch.Trim();
            memQuery = memQuery.Where(m =>
                (m.EventKey ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (m.Note ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (m.Details ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        if (_memoryTypeFilter.HasValue)
        {
            memQuery = memQuery.Where(m => m.ChangeType == _memoryTypeFilter.Value);
        }

        _cachedMemoryLogs = memQuery.ToList();

        if (_selectedMemoryLog != null && _cachedMemoryLogs.All(m => m.Id != _selectedMemoryLog.Id))
        {
            _selectedMemoryLog = null;
        }
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
            case DebugViewMode.MemoryLog:
                DrawMemoryLogTable(tableRect);
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
        float viewDropdownWidth = 100f;
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
            DebugViewMode.MemoryLog => "RimTalk.DebugWindow.ViewMemoryLog".Translate(),
            _ => _viewMode.ToString()
        };

        if (Widgets.ButtonText(viewBtnRect, viewLabel))
        {
            var options = new List<FloatMenuOption>
            {
                new("RimTalk.DebugWindow.ViewByTime".Translate(), () => _viewMode = DebugViewMode.MainTable),
                new("RimTalk.DebugWindow.ViewByPawn".Translate(), () => _viewMode = DebugViewMode.GroupedByPawn),
                new("RimTalk.DebugWindow.ViewTalkRequests".Translate(), () => _viewMode = DebugViewMode.ActiveRequests),
                new("RimTalk.DebugWindow.ViewMemoryLog".Translate(), () => _viewMode = DebugViewMode.MemoryLog)
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
        else if (_viewMode == DebugViewMode.MemoryLog)
        {
            string label = _memoryTypeFilter.HasValue
                ? $"RimTalk.DebugWindow.MemoryType{_memoryTypeFilter.Value}".Translate()
                : "RimTalk.DebugWindow.StateAll".Translate();

            if (Widgets.ButtonText(stateBtnRect, label))
            {
                var options = new List<FloatMenuOption>
                {
                    new("RimTalk.DebugWindow.StateAll".Translate(), () => _memoryTypeFilter = null)
                };
                options.AddRange(Enum.GetValues(typeof(MemoryChangeType))
                    .Cast<MemoryChangeType>()
                    .Select(t => new FloatMenuOption($"RimTalk.DebugWindow.MemoryType{t}".Translate(),
                        () => _memoryTypeFilter = t)));
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
                new($"{lastPrefix} 200", () => SetMaxRows(200)),
                new($"{lastPrefix} 500", () => SetMaxRows(500)),
                new($"{lastPrefix} 1000", () => SetMaxRows(1000)),
                new($"{lastPrefix} 2000", () => SetMaxRows(2000))
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    private void SetMaxRows(int count)
    {
        _maxRows = count;
        ApiHistory.MaxHistoryCount = count;
        ApiHistory.TrimHistory();
        MemoryHistory.MaxCount = count;
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

        Rect graphArea = rect.ContractedBy(2f);

        foreach (var (data, color, _) in series)
        {
            if (data == null || data.Count < 2) continue;
            const float topPadding = 10f;
            const float bottomPadding = 0f;
            float graphHeight = graphArea.height - topPadding - bottomPadding;
            if (graphHeight <= 0) continue;

            var points = new List<Vector2>(data.Count);
            for (int i = 0; i < data.Count; i++)
            {
                float x = graphArea.x + (float)i / (data.Count - 1) * graphArea.width;
                float y = (graphArea.y + graphArea.height - bottomPadding) - ((float)data[i] / maxVal * graphHeight);
                points.Add(new Vector2(x, y));
            }

            for (int i = 0; i < points.Count - 1; i++) Widgets.DrawLine(points[i], points[i + 1], color, 1f);

            // Show token count at the end of each spike (where line drops to 0)
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < data.Count; i++)
            {
                if (i < Stats.TokenLabels.Count && Stats.TokenLabels[i] > 0)
                {
                    bool isEnd = (i == data.Count - 1) || data[i + 1] <= 0;
                    if (isEnd && i < points.Count)
                    {
                        float labelY = Mathf.Max(rect.y, points[i].y - 13f);
                        Widgets.Label(new Rect(points[i].x - 20, labelY, 60, 14f), Stats.TokenLabels[i].ToString());
                    }
                }
            }
            GUI.color = Color.white;
        }

        var legendRect = new Rect(rect.xMax - 110, rect.y + 3, 105, 18);
        Widgets.DrawBoxSolid(legendRect, new Color(0, 0, 0, 0.4f));
        foreach (var (data, color, label) in series)
        {
            Widgets.DrawBoxSolid(new Rect(legendRect.x + 6, legendRect.y + 4, 10, 10), color);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(legendRect.x + 20, legendRect.y, 80, 16), label);
        }
    }

    private void DrawStatsSection(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.4f));
        Text.Font = GameFont.Tiny;
        GUI.BeginGroup(rect);

        const float rowHeight = 18f;
        float startY = Mathf.Max(2f, (rect.height - (rowHeight * 3)) / 2f);
        var contentRect = rect.AtZero().ContractedBy(6f);
        const float colGap = 10f;
        float colWidth = Mathf.Floor((contentRect.width - colGap) / 2f);
        float col1X = contentRect.x;
        float col2X = col1X + colWidth + colGap;

        Color statusColor;
        var aiStatus = _aiStatus.Translate();
        if (aiStatus == "RimTalk.DebugWindow.StatusProcessing".Translate()) statusColor = Color.yellow;
        else if (aiStatus == "RimTalk.DebugWindow.StatusIdle".Translate()) statusColor = Color.green;
        else statusColor = Color.gray;

        void DrawStat(float x, float y, float width, float valWidth, string label, string value, Color? valColor = null)
        {
            float labelWidth = width - valWidth;
            var labelRect = new Rect(x, y, labelWidth, rowHeight);
            var valRect = new Rect(x + labelWidth, y, valWidth, rowHeight);

            GUI.color = Color.gray;
            Widgets.Label(labelRect, label);
            GUI.color = valColor ?? Color.white;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(valRect, value);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // Column 1 (Status, Total calls, Total tokens)
        DrawStat(col1X, startY, colWidth, 55f, "RimTalk.DebugWindow.AIStatus".Translate(), _aiStatus, statusColor);
        DrawStat(col1X, startY + rowHeight, colWidth, 55f, "RimTalk.DebugWindow.TotalCalls".Translate(), _totalCalls.ToString("N0"));
        DrawStat(col1X, startY + rowHeight * 2, colWidth, 55f, "RimTalk.DebugWindow.TotalTokens".Translate(), _totalTokens.ToString("N0"));

        // Column 2 (Avg calls/min, Avg tokens/min, Avg tokens/call)
        DrawStat(col2X, startY, colWidth, 55f, "RimTalk.DebugWindow.AvgCallsPerMin".Translate(), _avgCallsPerMin.ToString("F2"));
        DrawStat(col2X, startY + rowHeight, colWidth, 55f, "RimTalk.DebugWindow.AvgTokensPerMin".Translate(), _avgTokensPerMin.ToString("F2"));
        DrawStat(col2X, startY + rowHeight * 2, colWidth, 55f, "RimTalk.DebugWindow.AvgTokensPerCall".Translate(), _avgTokensPerCall.ToString("F2"));

        GUI.EndGroup();
    }

    private void DrawBottomActions(Rect rect)
    {
        Text.Font = GameFont.Tiny;
        const float gap = 4f;
        float colWidth = (rect.width - gap) / 2f;
        const float btnHeight = 24f;
        float y1 = rect.y + Mathf.Max(0f, (rect.height - (btnHeight * 2 + gap)) / 2f);
        float y2 = y1 + btnHeight + gap;

        var col1X = rect.x;
        var col2X = rect.x + colWidth + gap;

        // Row 1: Mod Settings (Button) & Toggle RimTalk (Button)
        var modSettingsRect = new Rect(col1X, y1, colWidth, btnHeight);
        if (UIUtil.ButtonText(modSettingsRect, "RimTalk.DebugWindow.ModSettings".Translate()))
            Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));

        var settings = Settings.Get();
        bool modEnabled = settings.IsEnabled;
        string toggleLabel = $"RimTalk: {(modEnabled ? "ON" : "OFF")}";
        var toggleRect = new Rect(col2X, y1, colWidth, btnHeight);
        var prevColor = GUI.color;
        GUI.color = modEnabled ? new Color(0.6f, 1f, 0.6f) : new Color(0.9f, 0.5f, 0.5f);
        if (UIUtil.ButtonText(toggleRect, toggleLabel))
        {
            settings.IsEnabled = !modEnabled;
        }
        GUI.color = prevColor;
        TooltipHandler.TipRegion(toggleRect, "RimTalk.DebugWindow.EnableRimTalk".Translate());

        // Row 2: Export (Button) & Reset Logs (Button)
        var exportRect = new Rect(col1X, y2, colWidth, btnHeight);
        if (UIUtil.ButtonText(exportRect, "RimTalk.DebugWindow.Export".Translate()))
            UIUtil.ExportLogs(_requests);
        TooltipHandler.TipRegion(exportRect, "RimTalk.DebugWindow.ExportTooltip".Translate());

        var resetRect = new Rect(col2X, y2, colWidth, btnHeight);
        GUI.color = new Color(1f, 0.4f, 0.4f);
        if (UIUtil.ButtonText(resetRect, "RimTalk.DebugWindow.ResetLogs".Translate()))
            Reset();
        GUI.color = prevColor;
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

    private void Reset()
    {
        TalkHistory.Clear();
        TalkRequestPool.ClearHistory();
        Stats.Reset();
        ApiHistory.Clear();
        MemoryHistory.Clear();
        UpdateData();
        Messages.Message("RimTalk.DebugWindow.HistoryCleared".Translate(), MessageTypeDefOf.TaskCompletion, false);
    }
}
