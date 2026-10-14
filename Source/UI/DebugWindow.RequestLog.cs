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
        bool isSameConversation = !isSelected && _selectedLog != null && _selectedLog.ConversationId >= 0 &&
                                  _selectedLog.ConversationId == request.ConversationId;

        if (isSelected)
            Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.25f, 0.35f, 0.45f));
        else if (isSameConversation)
            Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.22f, 0.28f, 0.35f));

        // Left accent color bar indicating conversation group
        Color groupColor = UIUtil.GetConversationColor(request.ConversationId, fallbackMuted: true);
        Widgets.DrawBoxSolid(new Rect(xOffset, rowY, 3.5f, RowHeight), groupColor);

        float currentX = xOffset + 5f;
        Widgets.Label(new Rect(currentX, rowRect.y, TimestampColumnWidth, RowHeight),
            request.Timestamp.ToString("HH:mm:ss"));
        currentX += TimestampColumnWidth + ColumnPadding;

        if (showPawnColumn)
        {
            string pawnName = request.Name ?? "-";
            var pawnNameRect = new Rect(currentX, rowRect.y, PawnColumnWidth, RowHeight);
            var pawn = request.TalkRequest?.ResolvePawnState(pawnName)?.Pawn ?? Cache.GetByName(pawnName)?.Pawn;
            UIUtil.DrawClickablePawnName(pawnNameRect, pawnName, pawn);
            currentX += PawnColumnWidth + ColumnPadding;
        }

        string resp = (request.Response ?? _generating).Replace("\r", "").Replace("\n", " ");
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
        if (count == 0 && request.Channel == Channel.User && request.ConversationId >= 0)
        {
            var payload = ApiHistory.GetPayload(request);
            count = payload?.TokenCount ?? 0;
        }
        else if (count != 0 && request.ConversationId >= 0)
        {
            if (ApiHistory.GetAll().Any(l => l.ConversationId == request.ConversationId && l.Channel == Channel.User))
                count = 0;
        }

        string tokenCountText = count > 0 ? count.ToString() : (request.IsFirstDialogue ? "-" : "");
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

            string lastResponse = GetLastResponseForPawn(pawnKey).Replace("\r", "").Replace("\n", " ");
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
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedPawnNameWidth, rect.height), "RimTalk.DebugWindow.HeaderPawn", "Pawn");
        currentX += GroupedPawnNameWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, responseColumnWidth, rect.height), "RimTalk.DebugWindow.HeaderResponse", "Response");
        currentX += responseColumnWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedStatusWidth, rect.height), "RimTalk.DebugWindow.HeaderStatus", "Status");
        currentX += GroupedStatusWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedLastTalkWidth, rect.height), "RimTalk.DebugWindow.HeaderLastTalk", "Last Talk");
        currentX += GroupedLastTalkWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedRequestsWidth, rect.height), "RimTalk.DebugWindow.HeaderRequests", "Requests");
        currentX += GroupedRequestsWidth + ColumnPadding;
        DrawSortableHeader(new Rect(currentX, rect.y, GroupedChattinessWidth, rect.height), "RimTalk.DebugWindow.HeaderChattiness", "Chattiness");
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

    private void DrawSortableHeader(Rect rect, string key, string defaultLabel)
    {
        string translatedColumn = key.Translate();
        if (translatedColumn == key) translatedColumn = defaultLabel; // Fallback if key missing
        
        string arrow = _sortColumn == key ? (_sortAscending ? " ▲" : " ▼") : "";
        if (Widgets.ButtonInvisible(rect))
        {
            if (_sortColumn == key) _sortAscending = !_sortAscending;
            else
            {
                _sortColumn = key;
                _sortAscending = true;
            }

            var settings = Settings.Get();
            settings.DebugSortColumn = _sortColumn;
            settings.DebugSortAscending = _sortAscending;
        }

        Widgets.Label(rect, translatedColumn + arrow);
    }

    private IEnumerable<PawnState> GetSortedPawnStates()
    {
        switch (_sortColumn)
        {
            case "RimTalk.DebugWindow.HeaderPawn":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.Pawn.LabelShort)
                    : _pawnStates.OrderByDescending(p => p.Pawn.LabelShort);
        
            case "RimTalk.DebugWindow.HeaderRequests":
                return _sortAscending
                    ? _pawnStates.OrderBy(p =>
                        _talkLogsByPawn.TryGetValue(p.Pawn.LabelShort, out var logs) 
                            ? logs.Count(r => r.IsFirstDialogue) : 0)
                    : _pawnStates.OrderByDescending(p =>
                        _talkLogsByPawn.TryGetValue(p.Pawn.LabelShort, out var logs) 
                            ? logs.Count(r => r.IsFirstDialogue) : 0);

            case "RimTalk.DebugWindow.HeaderResponse":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => GetLastResponseForPawn(p.Pawn.LabelShort))
                    : _pawnStates.OrderByDescending(p => GetLastResponseForPawn(p.Pawn.LabelShort));
        
            case "RimTalk.DebugWindow.HeaderStatus":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.CanDisplayTalk())
                    : _pawnStates.OrderByDescending(p => p.CanDisplayTalk());
        
            case "RimTalk.DebugWindow.HeaderLastTalk":
                return _sortAscending
                    ? _pawnStates.OrderBy(p => p.LastTalkTick)
                    : _pawnStates.OrderByDescending(p => p.LastTalkTick);
        
            case "RimTalk.DebugWindow.HeaderChattiness":
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
}
