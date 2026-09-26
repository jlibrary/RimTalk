using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_FastTrackInteractions : Window
{
    private Vector2 _scrollPosition = Vector2.zero;
    private string _filterText = "";
    private readonly HashSet<string> _collapsedMods = new();
    private List<IGrouping<string, InteractionDef>> _cachedGroups;
    private string _lastFilter;

    public Dialog_FastTrackInteractions()
    {
        doCloseX = true;
        closeOnAccept = true;
        closeOnCancel = true;
        draggable = true;
        absorbInputAroundWindow = true;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new(650f, 620f);

    private static IEnumerable<InteractionDef> GetEligibleInteractionDefs()
    {
        return DefDatabase<InteractionDef>.AllDefsListForReading
            .Where(def => def.defName != "RimTalkInteraction" && !def.defName.StartsWith("RimTalk"));
    }

    private List<IGrouping<string, InteractionDef>> GetInteractionGroups()
    {
        if (_cachedGroups != null && _filterText == _lastFilter)
            return _cachedGroups;

        _lastFilter = _filterText;
        var allDefs = GetEligibleInteractionDefs();

        IEnumerable<InteractionDef> filtered = allDefs;
        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            string search = _filterText.Trim();
            filtered = allDefs.Where(def =>
                (def.label != null && def.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (def.defName != null && def.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (def.modContentPack?.Name != null && def.modContentPack.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        _cachedGroups = filtered
            .OrderBy(def => def.label ?? def.defName)
            .GroupBy(def => def.modContentPack?.Name ?? "Core")
            .OrderBy(g => g.Key.Equals("Core", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(g => g.Key)
            .ToList();

        return _cachedGroups;
    }

    public override void DoWindowContents(Rect inRect)
    {
        var settings = Settings.Get();

        // 1. Title
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "RimTalk.Settings.FastTrackInteractionsTitle".Translate());
        Text.Font = GameFont.Small;

        // 2. Description
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.85f, 0.85f, 0.85f);
        string desc = "RimTalk.Settings.FastTrackInteractionsDesc".Translate();
        float descHeight = Text.CalcHeight(desc, inRect.width);
        Widgets.Label(new Rect(0f, 34f, inRect.width, descHeight), desc);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // 3. Search Bar & Action Buttons
        float controlY = 38f + descHeight;
        float searchWidth = inRect.width - 230f;
        Rect searchRect = new Rect(0f, controlY, searchWidth, 28f);
        _filterText = Widgets.TextField(searchRect, _filterText);

        Rect selectAllRect = new Rect(searchRect.xMax + 10f, controlY, 100f, 28f);
        if (UIUtil.ButtonText(selectAllRect, "RimTalk.Settings.SelectAll".Translate()))
        {
            foreach (var def in GetEligibleInteractionDefs())
            {
                settings.FastTrackInteractions[def.defName] = true;
            }
            settings.Write();
        }

        Rect deselectAllRect = new Rect(selectAllRect.xMax + 10f, controlY, 100f, 28f);
        if (UIUtil.ButtonText(deselectAllRect, "RimTalk.Settings.DeselectAll".Translate()))
        {
            foreach (var def in GetEligibleInteractionDefs())
            {
                settings.FastTrackInteractions[def.defName] = false;
            }
            settings.Write();
        }

        // 4. Scrollable List
        float listY = controlY + 36f;
        float listHeight = inRect.height - listY - 45f;
        Rect outRect = new Rect(0f, listY, inRect.width, listHeight);

        var groups = GetInteractionGroups();

        // Calculate total content height
        float viewHeight = 0f;
        foreach (var group in groups)
        {
            viewHeight += 28f; // Group header
            if (!_collapsedMods.Contains(group.Key))
            {
                viewHeight += group.Count() * 26f;
            }
        }
        viewHeight += 20f;

        Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(viewHeight, listHeight));
        Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);

        float curY = 0f;
        foreach (var group in groups)
        {
            bool isCollapsed = _collapsedMods.Contains(group.Key);

            // Group header
            Rect groupHeaderRect = new Rect(0f, curY, viewRect.width, 24f);
            Widgets.DrawHighlightIfMouseover(groupHeaderRect);

            Rect toggleRect = new Rect(groupHeaderRect.x + 2f, groupHeaderRect.y + (groupHeaderRect.height - 18f) / 2f, 18f, 18f);
            Texture2D toggleIcon = isCollapsed ? TexButton.Reveal : TexButton.Collapse;
            if (Widgets.ButtonImage(toggleRect, toggleIcon))
            {
                if (isCollapsed) _collapsedMods.Remove(group.Key);
                else _collapsedMods.Add(group.Key);
            }

            int totalCount = 0;
            int enabledCount = 0;
            foreach (var def in group)
            {
                totalCount++;
                if (settings.IsFastTrackInteraction(def.defName))
                {
                    enabledCount++;
                }
            }

            MultiCheckboxState state;
            if (enabledCount == 0)
            {
                state = MultiCheckboxState.Off;
            }
            else if (enabledCount == totalCount)
            {
                state = MultiCheckboxState.On;
            }
            else
            {
                state = MultiCheckboxState.Partial;
            }

            Rect headerCheckAndLabelRect = new Rect(toggleRect.xMax + 4f, groupHeaderRect.y, groupHeaderRect.width - (toggleRect.xMax + 4f), 24f);
            MultiCheckboxState newState = UIUtil.CheckboxMultiLabeledLeft(headerCheckAndLabelRect, $"{group.Key} ({group.Count()})", state, Color.cyan);
            if (newState != state)
            {
                bool target = state != MultiCheckboxState.On;
                foreach (var def in group)
                {
                    settings.FastTrackInteractions[def.defName] = target;
                }
                settings.Write();
            }

            curY += 26f;

            if (!isCollapsed)
            {
                foreach (var def in group)
                {
                    Rect rowRect = new Rect(40f, curY, viewRect.width - 40f, 24f);

                    bool isEnabled = settings.IsFastTrackInteraction(def.defName);
                    bool newVal = isEnabled;

                    string displayLabel = !string.IsNullOrWhiteSpace(def.label)
                        ? $"{def.LabelCap} ({def.defName})"
                        : def.defName;

                    if (UIUtil.CheckboxLabeledLeft(rowRect, displayLabel, ref newVal, def.description))
                    {
                        settings.FastTrackInteractions[def.defName] = newVal;
                        settings.Write();
                    }

                    curY += 26f;
                }
            }
        }

        Widgets.EndScrollView();

        // 5. Close Button (bottom)
        Rect closeBtnRect = new Rect((inRect.width - 120f) / 2f, inRect.height - 35f, 120f, 30f);
        if (UIUtil.ButtonText(closeBtnRect, "Close".Translate()))
        {
            Close();
        }
    }
}
