using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI
{
    public class DebugWindow : Window
    {
        private const float RowHeight = 22f;
        private const float HeaderHeight = 22f;
        private const float TimestampColumnWidth = 80f;
        private const float PawnColumnWidth = 80f;
        private const float TimeColumnWidth = 80f;
        private const float TokensColumnWidth = 80f;
        private const float CopyAreaWidth = 10f;
        private const float ColumnPadding = 10f;

        private const float GroupedPawnNameWidth = 80f;
        private const float GroupedRequestsWidth = 80f;
        private const float GroupedLastTalkWidth = 80f;
        private const float GroupedChattinessWidth = 80f;
        private const float GroupedExpandIconWidth = 25f;
        private const float GroupedStatusWidth = 80f;

        private readonly string _generating = "RimTalk.DebugWindow.Generating".Translate();


        private Vector2 _tableScrollPosition;

        private string _aiStatus;
        private long _totalCalls;
        private long _totalTokens;
        private double _avgCallsPerMin;
        private double _avgTokensPerMin;
        private double _avgTokensPerCall;
        private List<PawnState> _pawnStates;
        private List<TalkLog> _requests;
        private readonly Dictionary<string, List<TalkLog>> _talkLogsByPawn = new Dictionary<string, List<TalkLog>>();

        private bool _groupingEnabled;
        private bool _debugModeEnabled;
        private string _sortColumn;
        private bool _sortAscending;
        private readonly List<string> _expandedPawns;

        public DebugWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;

            var settings = LoadedModManager.GetMod<Settings>().GetSettings<CurrentWorkDisplayModSettings>();
            _groupingEnabled = settings.DebugGroupingEnabled;
            _debugModeEnabled = settings.DebugModeEnabled;
            _sortColumn = settings.DebugSortColumn;
            _sortAscending = settings.DebugSortAscending;
            _expandedPawns = settings.DebugExpandedPawns ?? new List<string>();
        }

        public override Vector2 InitialSize => new Vector2(1000f, 600f);

        public override void PreClose()
        {
            base.PreClose();
            var settings = LoadedModManager.GetMod<Settings>().GetSettings<CurrentWorkDisplayModSettings>();
            settings.DebugGroupingEnabled = _groupingEnabled;
            settings.DebugModeEnabled = _debugModeEnabled;
            settings.DebugSortColumn = _sortColumn;
            settings.DebugSortAscending = _sortAscending;
            settings.DebugExpandedPawns = _expandedPawns;
            settings.Write();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UpdateData();

            const float bottomSectionHeight = 150f;
            const float optionsBarHeight = 30f;
            const float spacing = 10f;

            var optionsRect = new Rect(inRect.x, inRect.y, inRect.width, optionsBarHeight);

            float tableHeight = inRect.height - optionsBarHeight;
            if (_debugModeEnabled)
            {
                tableHeight -= (bottomSectionHeight + spacing);
            }

            var tableRect = new Rect(inRect.x, optionsRect.yMax, inRect.width, tableHeight);

            DrawOptionsBar(optionsRect);

            if (_groupingEnabled)
                DrawGroupedPawnTable(tableRect);
            else
                DrawUngroupedRequestTable(tableRect);

            if (_debugModeEnabled)
            {
                var bottomRect = new Rect(inRect.x, tableRect.yMax + spacing, inRect.width, bottomSectionHeight);
                var graphRect = new Rect(bottomRect.x, bottomRect.y, bottomRect.width * 0.55f - (spacing / 2),
                    bottomRect.height);
                var statsRect = new Rect(graphRect.xMax + spacing, bottomRect.y,
                    bottomRect.width * 0.45f - (spacing / 2), bottomRect.height);
                DrawGraph(graphRect);
                DrawStatsSection(statsRect);
            }
        }

        private void UpdateData()
        {
            var settings = LoadedModManager.GetMod<Settings>().GetSettings<CurrentWorkDisplayModSettings>();
            if (!settings.IsEnabled)
                _aiStatus = "RimTalk.DebugWindow.StatusDisabled".Translate();
            else
                _aiStatus = AIService.IsBusy() ? "RimTalk.DebugWindow.StatusProcessing".Translate() : "RimTalk.DebugWindow.StatusIdle".Translate();

            _totalCalls = Stats.TotalCalls;
            _totalTokens = Stats.TotalTokens;
            _avgCallsPerMin = Stats.AvgCallsPerMinute;
            _avgTokensPerMin = Stats.AvgTokensPerMinute;
            _avgTokensPerCall = Stats.AvgTokensPerCall;
            _pawnStates = Cache.GetAll().ToList();
            _requests = TalkLogHistory.GetAll().OrderByDescending(r => r.Timestamp).ToList();

            _talkLogsByPawn.Clear();
            foreach (var request in _requests.Where(r => r.Name != null))
            {
                if (!_talkLogsByPawn.ContainsKey(request.Name))
                    _talkLogsByPawn[request.Name] = new List<TalkLog>();
                _talkLogsByPawn[request.Name].Add(request);
            }
        }

        private void DrawOptionsBar(Rect rect)
        {
            var settings = LoadedModManager.GetMod<Settings>().GetSettings<CurrentWorkDisplayModSettings>();

            // --- Left-aligned controls (View Options) ---
            float currentX = rect.x;

            // Grouping checkbox
            bool grouping = _groupingEnabled;
            var groupingRect = new Rect(currentX, rect.y, 130f, 24f);
            Widgets.CheckboxLabeled(groupingRect, "RimTalk.DebugWindow.GroupByPawn".Translate(), ref grouping);
            if (grouping != _groupingEnabled)
            {
                _groupingEnabled = grouping;
                settings.DebugGroupingEnabled = _groupingEnabled;
            }

            currentX += groupingRect.width + ColumnPadding;

            // Debug Mode checkbox
            bool debugMode = _debugModeEnabled;
            var debugModeRect = new Rect(currentX, rect.y, 120f, 24f);
            Widgets.CheckboxLabeled(debugModeRect, "RimTalk.DebugWindow.DebugMode".Translate(), ref debugMode);
            if (debugMode != _debugModeEnabled)
            {
                _debugModeEnabled = debugMode;
                settings.DebugModeEnabled = _debugModeEnabled;
            }

            // --- Right-aligned controls (Global Actions) ---
            float rightEdgeX = rect.xMax - 15;

            // Mod Settings button
            var modSettingsButtonRect = new Rect(rightEdgeX - 120f, rect.y, 120f, 24f);
            if (Widgets.ButtonText(modSettingsButtonRect, "RimTalk.DebugWindow.ModSettings".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
            }

            rightEdgeX -= modSettingsButtonRect.width + ColumnPadding;

            // "Enable AI Talk" checkbox
            bool modEnabled = settings.IsEnabled;
            var enabledCheckboxRect = new Rect(rightEdgeX - 150f, rect.y, 130f, 24f);
            Widgets.CheckboxLabeled(enabledCheckboxRect, "RimTalk.DebugWindow.EnableRimTalk".Translate(), ref modEnabled);
            if (modEnabled != settings.IsEnabled)
            {
                settings.IsEnabled = modEnabled;
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
            switch (_aiStatus)
            {
                case "Processing":
                    statusColor = Color.yellow;
                    break;
                case "Idle":
                    statusColor = Color.green;
                    break;
                default:
                    statusColor = Color.grey;
                    break;
            }

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
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.15f, 0.8f));

            var series = new[]
            {
                (data: Stats.TokensPerSecondHistory, color: new Color(1f, 1f, 1f, 0.7f), label: "RimTalk.DebugWindow.TokensPerSecond".Translate()),
            };

            if (!series.Any(s => s.data != null && s.data.Any())) return;

            long maxVal = Math.Max(1, series.Where(s => s.data != null && s.data.Any()).SelectMany(s => s.data).Max());

            Text.Font = GameFont.Tiny;
            GUI.color = Color.grey;
            Widgets.Label(new Rect(rect.x + 5, rect.y, 40, 20), maxVal.ToString());
            Widgets.Label(new Rect(rect.x + 5, rect.y + rect.height - 15, 60, 20), "RimTalk.DebugWindow.SixtySecondsAgo".Translate());
            Widgets.Label(new Rect(rect.xMax - 35, rect.y + rect.height - 15, 40, 20), "RimTalk.DebugWindow.Now".Translate());
            GUI.color = Color.white;

            // --- SOLUTION: Define an inner drawing area to act as a margin ---
            Rect graphArea = rect.ContractedBy(2f);

            foreach (var (data, color, _) in series)
            {
                if (data == null || data.Count < 2) continue;

                const float verticalPadding = 15f;
                // Use graphArea.height for calculation
                float graphHeight = graphArea.height - (2 * verticalPadding);
                if (graphHeight <= 0) continue;

                var points = new List<Vector2>();
                for (int i = 0; i < data.Count; i++)
                {
                    // --- FIX: Use graphArea for all point calculations ---
                    // Also, replaced hardcoded '59f' with the actual count for robustness
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

        private void DrawGroupedPawnTable(Rect rect)
        {
            if (_pawnStates == null || !_pawnStates.Any())
                return;

            float totalHeight = CalculateGroupedTableHeight();
            var viewRect = new Rect(0, 0, rect.width - 16f, totalHeight);

            Widgets.BeginScrollView(rect, ref _tableScrollPosition, viewRect);

            float responseColumnWidth = CalculateGroupedResponseColumnWidth(viewRect.width);

            DrawGroupedHeader(new Rect(0, 0, viewRect.width, HeaderHeight), responseColumnWidth);
            float currentY = HeaderHeight;

            var sortedPawns = GetSortedPawnStates().ToList();
            for (int i = 0; i < sortedPawns.Count; i++)
            {
                var pawnState = sortedPawns[i];
                string pawnKey = pawnState.Pawn.Name.ToStringShort;
                bool isExpanded = _expandedPawns.Contains(pawnKey);

                var rowRect = new Rect(0, currentY, viewRect.width, RowHeight);
                if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

                float currentX = 0;

                Widgets.Label(new Rect(rowRect.x + 5, rowRect.y + 3, 15, 15), isExpanded ? "-" : "+");
                currentX += GroupedExpandIconWidth;

                var pawnNameRect = new Rect(currentX, rowRect.y, GroupedPawnNameWidth, RowHeight);
                if (Widgets.ButtonText(pawnNameRect, pawnKey, drawBackground: false, doMouseoverSound: true,
                        active: true))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(pawnState.Pawn));
                }

                currentX += GroupedPawnNameWidth + ColumnPadding;

                string lastResponse = GetLastResponseForPawn(pawnKey);
                Widgets.Label(new Rect(currentX, rowRect.y, responseColumnWidth, RowHeight), lastResponse);
                currentX += responseColumnWidth + ColumnPadding;

                if (_debugModeEnabled)
                {
                    bool canTalk = pawnState.CanDisplayTalk();
                    string statusText = canTalk ? "RimTalk.DebugWindow.StatusReady".Translate() : "RimTalk.DebugWindow.StatusBusy".Translate();
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
                }

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
                    DrawRequestTableHeader(new Rect(indentWidth, currentY, innerWidth, HeaderHeight),
                        innerResponseWidth, false);
                    currentY += HeaderHeight;
                    DrawRequestRows(requests, ref currentY, innerWidth, indentWidth, innerResponseWidth, false);
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
            DrawSortableHeader(new Rect(currentX, rect.y, GroupedPawnNameWidth, rect.height), "RimTalk.DebugWindow.HeaderPawn".Translate());
            currentX += GroupedPawnNameWidth + ColumnPadding;
            DrawSortableHeader(new Rect(currentX, rect.y, responseColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderResponse".Translate());
            currentX += responseColumnWidth + ColumnPadding;

            if (_debugModeEnabled)
            {
                DrawSortableHeader(new Rect(currentX, rect.y, GroupedStatusWidth, rect.height), "RimTalk.DebugWindow.HeaderStatus".Translate());
                currentX += GroupedStatusWidth + ColumnPadding;
                DrawSortableHeader(new Rect(currentX, rect.y, GroupedLastTalkWidth, rect.height), "RimTalk.DebugWindow.HeaderLastTalk".Translate());
                currentX += GroupedLastTalkWidth + ColumnPadding;
                DrawSortableHeader(new Rect(currentX, rect.y, GroupedRequestsWidth, rect.height), "RimTalk.DebugWindow.HeaderRequests".Translate());
                currentX += GroupedRequestsWidth + ColumnPadding;
                DrawSortableHeader(new Rect(currentX, rect.y, GroupedChattinessWidth, rect.height), "RimTalk.DebugWindow.HeaderChattiness".Translate());
            }
        }

        private void DrawUngroupedRequestTable(Rect rect)
        {
            if (_requests == null || !_requests.Any())
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(rect, "RimTalk.DebugWindow.NoTalkLogs".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float totalHeight = HeaderHeight + (_requests.Count * RowHeight) + 50f;
            var viewRect = new Rect(0, 0, rect.width - 16f, totalHeight);

            Widgets.BeginScrollView(rect, ref _tableScrollPosition, viewRect);

            float responseWidth = CalculateResponseColumnWidth(viewRect.width, true);
            DrawRequestTableHeader(new Rect(0, 0, viewRect.width, HeaderHeight), responseWidth, true);
            float currentY = HeaderHeight;
            DrawRequestRows(_requests, ref currentY, viewRect.width, 0, responseWidth, true);

            Widgets.EndScrollView();
        }

        private void DrawRequestTableHeader(Rect rect, float responseColumnWidth, bool showPawnColumn)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.25f, 0.9f));
            Text.Font = GameFont.Tiny;
            float currentX = rect.x + 5f;

            Widgets.Label(new Rect(currentX, rect.y, TimestampColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderTimestamp".Translate());
            currentX += TimestampColumnWidth + ColumnPadding;
            if (showPawnColumn)
            {
                Widgets.Label(new Rect(currentX, rect.y, PawnColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderPawn".Translate());
                currentX += PawnColumnWidth + ColumnPadding;
            }

            Widgets.Label(new Rect(currentX, rect.y, responseColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderResponse".Translate());
            currentX += responseColumnWidth + ColumnPadding;

            if (_debugModeEnabled)
            {
                Widgets.Label(new Rect(currentX, rect.y, TimeColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderTimeMs".Translate());
                currentX += TimeColumnWidth + ColumnPadding;
                Widgets.Label(new Rect(currentX, rect.y, TokensColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderTokens".Translate());
            }
        }

        private void DrawRequestRows(List<TalkLog> requests, ref float currentY, float totalWidth, float xOffset,
            float responseColumnWidth, bool showPawnColumn)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                var rowRect = new Rect(xOffset, currentY, totalWidth, RowHeight);
                if (i % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

                string resp = request.Response ?? _generating;
                int maxChars = (int)(responseColumnWidth / 8);
                if (resp.Length > maxChars) resp = resp.Substring(0, Math.Max(10, maxChars - 3)) + "...";

                float currentX = xOffset + 5f;
                Widgets.Label(new Rect(currentX, rowRect.y, TimestampColumnWidth, RowHeight),
                    request.Timestamp.ToString("HH:mm:ss"));
                currentX += TimestampColumnWidth + ColumnPadding;

                if (showPawnColumn)
                {
                    string pawnName = request.Name ?? "-";
                    var pawnNameRect = new Rect(currentX, rowRect.y, PawnColumnWidth, RowHeight);
                    var pawn = _pawnStates.FirstOrDefault(p => p.Pawn.Name.ToStringShort == pawnName)?.Pawn;

                    if (pawn != null)
                    {
                        if (Widgets.ButtonText(pawnNameRect, pawnName, drawBackground: false, doMouseoverSound: true,
                                active: true))
                        {
                            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
                        }
                    }
                    else
                    {
                        Widgets.Label(pawnNameRect, pawnName);
                    }

                    currentX += PawnColumnWidth + ColumnPadding;
                }

                var responseRect = new Rect(currentX, rowRect.y, responseColumnWidth, RowHeight);
                Widgets.Label(responseRect, resp);
                currentX += responseColumnWidth + ColumnPadding;

                if (_debugModeEnabled)
                {
                    Widgets.Label(new Rect(currentX, rowRect.y, TimeColumnWidth, RowHeight),
                        request.ElapsedMs.ToString());
                    currentX += TimeColumnWidth + ColumnPadding;
                    Widgets.Label(new Rect(currentX, rowRect.y, TokensColumnWidth, RowHeight),
                        request.TokenCount.ToString());
                }

                var copyRect = new Rect(rowRect.xMax - CopyAreaWidth, rowRect.y, CopyAreaWidth, rowRect.height);
                if (Widgets.ButtonInvisible(copyRect)) GUIUtility.systemCopyBuffer = request.RequestPayload ?? "";

                if (_debugModeEnabled)
                {
                    string tooltip = "RimTalk.DebugWindow.TooltipPromptResponse".Translate(request.Prompt, (request.Response ?? _generating));
                    TooltipHandler.TipRegion(responseRect, tooltip);
                }

                currentY += RowHeight;
            }
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

                var settings = LoadedModManager.GetMod<Settings>().GetSettings<CurrentWorkDisplayModSettings>();
                settings.DebugSortColumn = _sortColumn;
                settings.DebugSortAscending = _sortAscending;
            }

            Widgets.Label(rect, translatedColumn + arrow);
        }

        private IEnumerable<PawnState> GetSortedPawnStates()
        {
            switch (_sortColumn)
            {
                case "Pawn":
                    return _sortAscending
                        ? _pawnStates.OrderBy(p => p.Pawn.Name.ToStringShort)
                        : _pawnStates.OrderByDescending(p => p.Pawn.Name.ToStringShort);
                case "Requests":
                    return _sortAscending
                        ? _pawnStates.OrderBy(p =>
                            _talkLogsByPawn.ContainsKey(p.Pawn.Name.ToStringShort)
                                ? _talkLogsByPawn[p.Pawn.Name.ToStringShort].Count
                                : 0)
                        : _pawnStates.OrderByDescending(p =>
                            _talkLogsByPawn.ContainsKey(p.Pawn.Name.ToStringShort)
                                ? _talkLogsByPawn[p.Pawn.Name.ToStringShort].Count
                                : 0);
                case "Response":
                    return _sortAscending
                        ? _pawnStates.OrderBy(p => GetLastResponseForPawn(p.Pawn.Name.ToStringShort))
                        : _pawnStates.OrderByDescending(p => GetLastResponseForPawn(p.Pawn.Name.ToStringShort));
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

        private float CalculateGroupedTableHeight()
        {
            float height = HeaderHeight + (_pawnStates.Count * RowHeight);
            foreach (var pawnState in _pawnStates)
            {
                var pawnKey = pawnState.Pawn.Name.ToStringShort;
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
                return logs.First().Response ?? _generating;
            }

            return null;
        }
    }
}