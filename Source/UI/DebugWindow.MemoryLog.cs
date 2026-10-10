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
    private void DrawMemoryLogTable(Rect rect)
    {
        float fixedWidth = MLTimeWidth + MLTickWidth + MLPawnWidth + MLTargetWidth + MLTypeWidth + MLWeightWidth + (ColumnPadding * 6);
        float noteWidth = Mathf.Max(50f, rect.width - fixedWidth - 16f);

        DrawMemoryLogHeader(new Rect(rect.x, rect.y, rect.width, HeaderHeight), noteWidth);

        var scrollRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
        float viewWidth = scrollRect.width - 16f;
        float viewHeight = _cachedMemoryLogs.Count * RowHeight;
        var viewRect = new Rect(0, 0, viewWidth, viewHeight);

        float maxScroll = Mathf.Max(0f, viewHeight - scrollRect.height);
        if (_stickToBottom)
            _memoryLogsScrollPosition.y = maxScroll;

        Widgets.BeginScrollView(scrollRect, ref _memoryLogsScrollPosition, viewRect);

        if (_stickToBottom && _memoryLogsScrollPosition.y < maxScroll - 1f)
            _stickToBottom = false;

        float visibleTop = _memoryLogsScrollPosition.y;
        float visibleBottom = _memoryLogsScrollPosition.y + scrollRect.height;
        int firstIndex = Mathf.Clamp((int)(visibleTop / RowHeight), 0, _cachedMemoryLogs.Count);
        int lastIndex = Mathf.Clamp((int)(visibleBottom / RowHeight) + 1, 0, _cachedMemoryLogs.Count);

        for (int i = firstIndex; i < lastIndex; i++)
        {
            DrawMemoryLogRow(_cachedMemoryLogs[i], i, i * RowHeight, viewWidth, noteWidth);
        }

        Widgets.EndScrollView();

        DrawStickToBottomOverlay(scrollRect, maxScroll, ref _memoryLogsScrollPosition);
    }

    private void DrawMemoryLogHeader(Rect rect, float noteWidth)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.25f, 0.9f));
        Text.Font = GameFont.Tiny;
        float currentX = rect.x + 5f;

        Widgets.Label(new Rect(currentX, rect.y, MLTimeWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTimestamp".Translate());
        currentX += MLTimeWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, MLTickWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTick".Translate());
        currentX += MLTickWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, MLPawnWidth, rect.height),
            "RimTalk.DebugWindow.HeaderPawn".Translate());
        currentX += MLPawnWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, MLTargetWidth, rect.height),
            "RimTalk.DebugWindow.HeaderTarget".Translate());
        currentX += MLTargetWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, MLTypeWidth, rect.height),
            "RimTalk.DebugWindow.HeaderChangeType".Translate());
        currentX += MLTypeWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, MLWeightWidth, rect.height),
            "RimTalk.DebugWindow.HeaderWeight".Translate());
        currentX += MLWeightWidth + ColumnPadding;

        Widgets.Label(new Rect(currentX, rect.y, noteWidth, rect.height),
            "RimTalk.DebugWindow.HeaderEventNote".Translate());
    }

    private void DrawMemoryLogRow(MemoryLogEntry entry, int index, float rowY, float width, float noteWidth)
    {
        var rowRect = new Rect(0, rowY, width, RowHeight);
        if (index % 2 == 0) Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.4f));

        bool isSelected = _selectedMemoryLog != null && _selectedMemoryLog.Id == entry.Id;
        if (isSelected)
            Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.25f, 0.35f, 0.45f));

        float currentX = 5f;

        // 1. Time
        Widgets.Label(new Rect(currentX, rowY, MLTimeWidth, RowHeight), entry.Timestamp.ToString("HH:mm:ss"));
        currentX += MLTimeWidth + ColumnPadding;

        // 2. Tick
        Widgets.Label(new Rect(currentX, rowY, MLTickWidth, RowHeight), entry.Tick.ToString());
        currentX += MLTickWidth + ColumnPadding;

        // 3. Source Pawn
        var srcRect = new Rect(currentX, rowY, MLPawnWidth, RowHeight);
        string srcName = string.IsNullOrEmpty(entry.SourcePawnName) ? "-" : entry.SourcePawnName;
        var srcPawn = ResolvePawn(srcName, entry.SourcePawnId);
        UIUtil.DrawClickablePawnName(srcRect, srcName, srcPawn);
        currentX += MLPawnWidth + ColumnPadding;

        // 4. Target Pawn
        var tgtRect = new Rect(currentX, rowY, MLTargetWidth, RowHeight);
        string tgtName = string.IsNullOrEmpty(entry.TargetPawnName) ? "-" : entry.TargetPawnName;
        var tgtPawn = ResolvePawn(tgtName, entry.TargetPawnId);
        UIUtil.DrawClickablePawnName(tgtRect, tgtName, tgtPawn);
        currentX += MLTargetWidth + ColumnPadding;

        // 5. Change Type
        Color typeColor = entry.ChangeType switch
        {
            MemoryChangeType.Added => new Color(0.4f, 0.9f, 0.4f),
            MemoryChangeType.Updated => new Color(0.4f, 0.8f, 1f),
            MemoryChangeType.DirectiveSet => Color.cyan,
            MemoryChangeType.DirectiveRemoved => new Color(1f, 0.7f, 0.3f),
            MemoryChangeType.CoreTraumaAdded => new Color(1f, 0.4f, 0.4f),
            MemoryChangeType.PurgedDecay => Color.gray,
            MemoryChangeType.Evicted => new Color(0.8f, 0.5f, 0.3f),
            MemoryChangeType.Consolidated => new Color(0.7f, 0.7f, 1f),
            MemoryChangeType.Cleared => new Color(1f, 0.3f, 0.3f),
            MemoryChangeType.MilestoneRecorded => new Color(1f, 0.85f, 0.2f),
            _ => Color.white
        };
        var prevColor = GUI.color;
        GUI.color = typeColor;
        Widgets.Label(new Rect(currentX, rowY, MLTypeWidth, RowHeight), $"RimTalk.DebugWindow.MemoryType{entry.ChangeType}".Translate());
        GUI.color = prevColor;
        currentX += MLTypeWidth + ColumnPadding;

        // 6. Weight
        string weightStr = entry.ChangeType == MemoryChangeType.Updated
            ? $"{entry.OldWeight:0}->{entry.NewWeight:0}"
            : $"{entry.NewWeight:0}";
        Widgets.Label(new Rect(currentX, rowY, MLWeightWidth, RowHeight), weightStr);
        currentX += MLWeightWidth + ColumnPadding;

        // 7. Event / Note
        string cleanNote = PawnMemoryTracker.StripBoilerplate(entry.Note);
        string text = !string.IsNullOrEmpty(cleanNote)
            ? $"[{entry.EventKey}] {cleanNote}"
            : (string.IsNullOrEmpty(entry.EventKey) ? entry.Details : entry.EventKey);
        Widgets.Label(new Rect(currentX, rowY, noteWidth, RowHeight), text);

        if (Widgets.ButtonInvisible(rowRect))
        {
            _selectedMemoryLog = entry;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }

    private static Pawn ResolvePawn(string name, int id)
    {
        if (string.IsNullOrEmpty(name) && id < 0) return null;
        var p = Cache.GetByName(name)?.Pawn;
        if (p != null) return p;
        if (id >= 0 && Current.ProgramState == ProgramState.Playing)
        {
            var maps = Find.Maps;
            if (maps != null)
            {
                for (int m = 0; m < maps.Count; m++)
                {
                    var pawns = maps[m].mapPawns?.AllPawns;
                    if (pawns != null)
                    {
                        for (int i = 0; i < pawns.Count; i++)
                        {
                            if (pawns[i].thingIDNumber == id)
                                return pawns[i];
                        }
                    }
                }
            }
        }
        return null;
    }

    private void DrawMemoryLogDetails(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f, 0.8f));
        InitializeContextStyle();

        var inner = rect.ContractedBy(8f);
        GUI.BeginGroup(inner);

        if (_selectedMemoryLog == null)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, 0f, inner.width, 50f), "RimTalk.DebugWindow.SelectRowHint".Translate());
            GUI.color = Color.white;
            GUI.EndGroup();
            return;
        }

        float y = 0f;
        var header = new StringBuilder();
        header.Append(_selectedMemoryLog.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        header.Append($"  |  Tick: {_selectedMemoryLog.Tick}");
        header.Append($"  |  {_selectedMemoryLog.SourcePawnName}");
        if (!string.IsNullOrEmpty(_selectedMemoryLog.TargetPawnName))
        {
            header.Append($" -> {_selectedMemoryLog.TargetPawnName}");
        }

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(0f, y, inner.width, 24f), header.ToString());
        GUI.color = Color.white;
        y += 26f;

        // Copy button
        float buttonsRowH = 24f;
        float btnW = 88f;
        if (Widgets.ButtonText(new Rect(0f, y, btnW, buttonsRowH), "RimTalk.DebugWindow.CopyAll".Translate()))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Time: {_selectedMemoryLog.Timestamp:yyyy-MM-dd HH:mm:ss} (Tick: {_selectedMemoryLog.Tick})");
            sb.AppendLine($"Pawn: {_selectedMemoryLog.SourcePawnName} (ID: {_selectedMemoryLog.SourcePawnId})");
            sb.AppendLine($"Target: {_selectedMemoryLog.TargetPawnName} (ID: {_selectedMemoryLog.TargetPawnId})");
            sb.AppendLine($"Change: {_selectedMemoryLog.ChangeType}");
            sb.AppendLine($"EventKey: {_selectedMemoryLog.EventKey}");
            sb.AppendLine($"Weight: {_selectedMemoryLog.OldWeight:F1} -> {_selectedMemoryLog.NewWeight:F1}");
            sb.AppendLine($"Directive: {_selectedMemoryLog.IsDirective}, CoreTrauma: {_selectedMemoryLog.IsCoreTrauma}");
            sb.AppendLine($"Note: {PawnMemoryTracker.StripBoilerplate(_selectedMemoryLog.Note)}");
            sb.AppendLine($"Details: {_selectedMemoryLog.Details}");
            GUIUtility.systemCopyBuffer = sb.ToString();
            Messages.Message("RimTalk.DebugWindow.Copied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }
        y += buttonsRowH + 8f;

        // Meta Box
        float metaBoxH = 68f;
        var metaRect = new Rect(0f, y, inner.width, metaBoxH);
        Widgets.DrawMenuSection(metaRect);
        var metaInner = metaRect.ContractedBy(6f);

        // Change Type & Weight
        float curMetaY = metaInner.y;
        Widgets.Label(new Rect(metaInner.x, curMetaY, 80f, 18f), "RimTalk.DebugWindow.HeaderChangeType".Translate() + ":");
        Color typeColor = _selectedMemoryLog.ChangeType switch
        {
            MemoryChangeType.Added => new Color(0.4f, 0.9f, 0.4f),
            MemoryChangeType.Updated => new Color(0.4f, 0.8f, 1f),
            MemoryChangeType.DirectiveSet => Color.cyan,
            MemoryChangeType.DirectiveRemoved => new Color(1f, 0.7f, 0.3f),
            MemoryChangeType.CoreTraumaAdded => new Color(1f, 0.4f, 0.4f),
            MemoryChangeType.PurgedDecay => Color.gray,
            MemoryChangeType.Evicted => new Color(0.8f, 0.5f, 0.3f),
            MemoryChangeType.Consolidated => new Color(0.7f, 0.7f, 1f),
            MemoryChangeType.Cleared => new Color(1f, 0.3f, 0.3f),
            MemoryChangeType.MilestoneRecorded => new Color(1f, 0.85f, 0.2f),
            _ => Color.white
        };
        GUI.color = typeColor;
        Widgets.Label(new Rect(metaInner.x + 80f, curMetaY, 130f, 18f), $"RimTalk.DebugWindow.MemoryType{_selectedMemoryLog.ChangeType}".Translate());
        GUI.color = Color.white;

        Widgets.Label(new Rect(metaInner.x + 220f, curMetaY, 60f, 18f), "RimTalk.DebugWindow.HeaderWeight".Translate() + ":");
        string weightStr = _selectedMemoryLog.ChangeType == MemoryChangeType.Updated
            ? $"{_selectedMemoryLog.OldWeight:F1} -> {_selectedMemoryLog.NewWeight:F1}"
            : $"{_selectedMemoryLog.NewWeight:F1}";
        Widgets.Label(new Rect(metaInner.x + 280f, curMetaY, 120f, 18f), weightStr);

        curMetaY += 20f;
        // Event Key
        Widgets.Label(new Rect(metaInner.x, curMetaY, 80f, 18f), "RimTalk.DebugWindow.HeaderEventKey".Translate() + ":");
        Widgets.Label(new Rect(metaInner.x + 80f, curMetaY, metaInner.width - 80f, 18f), string.IsNullOrEmpty(_selectedMemoryLog.EventKey) ? "-" : _selectedMemoryLog.EventKey);

        curMetaY += 20f;
        // Flags
        string flags = $"Directive: {(_selectedMemoryLog.IsDirective ? "Yes" : "No")} | Trauma: {(_selectedMemoryLog.IsCoreTrauma ? "Yes" : "No")}";
        Widgets.Label(new Rect(metaInner.x, curMetaY, metaInner.width, 18f), flags.Colorize(new Color(0.7f, 0.7f, 0.7f)));

        y += metaBoxH + 10f;

        // Note Section
        Widgets.Label(new Rect(0f, y, inner.width, 18f), "RimTalk.DebugWindow.HeaderNoteContent".Translate() + ":");
        y += 20f;

        float noteBoxH = 120f;
        var noteRect = new Rect(0f, y, inner.width, noteBoxH);
        Widgets.DrawBoxSolid(noteRect, new Color(0.05f, 0.05f, 0.05f, 0.55f));
        string noteDisplay = string.IsNullOrEmpty(_selectedMemoryLog.Note) ? "-" : PawnMemoryTracker.StripBoilerplate(_selectedMemoryLog.Note);
        GUI.TextArea(noteRect.ContractedBy(4f), noteDisplay, _monoTinyStyle);
        y += noteBoxH + 10f;

        // Details / Reason Section
        Widgets.Label(new Rect(0f, y, inner.width, 18f), "RimTalk.DebugWindow.HeaderReasonContext".Translate() + ":");
        y += 20f;

        float detailsBoxH = Mathf.Max(50f, inner.height - y - 6f);
        var detRect = new Rect(0f, y, inner.width, detailsBoxH);
        Widgets.DrawBoxSolid(detRect, new Color(0.05f, 0.05f, 0.05f, 0.55f));
        string detDisplay = string.IsNullOrEmpty(_selectedMemoryLog.Details) ? "-" : _selectedMemoryLog.Details;
        GUI.TextArea(detRect.ContractedBy(4f), detDisplay, _monoTinyStyle);

        GUI.EndGroup();
    }
}
