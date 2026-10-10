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

        // 4. Prompt (Truncated, Single Line)
        string prompt = (req.Prompt ?? "").Replace("\r", "").Replace("\n", " ");
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
}
