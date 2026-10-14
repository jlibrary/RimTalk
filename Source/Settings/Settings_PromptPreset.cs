using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RimTalk.Prompt;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalk;

public partial class Settings
{
    private static readonly Color LeftPanelBackground = new(0.05f, 0.05f, 0.05f, 0.55f);
    private static readonly Color AddGreen = new(0.3f, 0.9f, 0.3f);
    private static readonly Color DeleteRed = new(1f, 0.4f, 0.4f);
    private static readonly Color SourceModColor = new(0.95f, 0.78f, 0.35f);
    private const string DefaultPresetName = PromptManager.DefaultPresetName;

    // Scroll positions
    private Vector2 _presetListScrollPos = Vector2.zero;
    private Vector2 _entryListScrollPos = Vector2.zero;
    private Vector2 _auxScrollPos = Vector2.zero;
    private Vector2 _previewScrollPos = Vector2.zero;

    // Selection state
    private string _selectedPresetId;
    private string _selectedEntryId;

    // --- Layout State ---
    // Toggles
    private bool _showPreview = false;
    private bool _showSidePanel = false;
    private int _sidePanelMode = 0; // 0 = Variables, 1 = Help

    // Split Ratios
    private float _splitRatioVert = 0.5f; // For Top/Bottom (Editor vs Preview)
    private float _splitRatioHoriz = 0.7f; // For Left/Right (Main vs Side Panel)

    // Dragging State
    private bool _isDraggingVert = false;
    private bool _isDraggingHoriz = false;
    private string _variableSearchQuery = "";
    private string _depthBuffer = "";
    private string _depthBufferEntryId = "";

    public void DrawPromptPresetSettings(Listing_Standard listingStandard, Rect inRect)
    {
        RimTalkSettings settings = Get();
        if (settings.UseAdvancedPromptMode)
            DrawAdvancedPromptMode(listingStandard, settings, inRect);
        else
            DrawSimplePromptMode(listingStandard, settings, inRect);
    }

    private void DrawSimplePromptMode(Listing_Standard listingStandard, RimTalkSettings settings, Rect containerRect)
    {
        DrawAIInstructionSettings(listingStandard, showAdvancedSwitch: true, containerRect: containerRect);
    }

    private void DrawAdvancedPromptMode(Listing_Standard listingStandard, RimTalkSettings settings, Rect containerRect)
    {
        var manager = PromptManager.Instance;
        if (string.IsNullOrEmpty(_selectedPresetId))
        {
            var active = manager.Presets.FirstOrDefault(p => p.IsActive) ??
                         manager.Presets.FirstOrDefault(p => p.Name == DefaultPresetName) ??
                         manager.Presets.FirstOrDefault();
            if (active != null)
            {
                _selectedPresetId = active.Id;
                _selectedEntryId = active.Entries.FirstOrDefault()?.Id;
            }
        }

        var selectedPreset = manager.Presets.FirstOrDefault(p => p.Id == _selectedPresetId) ??
                             manager.Presets.FirstOrDefault(p => p.IsActive) ??
                             manager.Presets.FirstOrDefault();

        if (PromptPresetAssembler.ShouldShowHistoryWarning(selectedPreset))
        {
            DrawHistoryWarningBanner(listingStandard);
        }

        float currentY = listingStandard.CurHeight;
        float availableHeight = Mathf.Max(300f, containerRect.height - currentY - 10f);

        Rect mainRect = listingStandard.GetRect(availableHeight);

        float leftPanelWidth = 200f;
        float panelGap = 4f;

        DrawPresetListPanel(new Rect(mainRect.x, mainRect.y, leftPanelWidth, mainRect.height), manager);

        Rect rightPanelRect = new Rect(mainRect.x + leftPanelWidth + panelGap, mainRect.y,
            mainRect.width - (leftPanelWidth + panelGap), mainRect.height);
        DrawEntryEditor(rightPanelRect, manager, settings);
    }

    private static void DrawHistoryWarningBanner(Listing_Standard listing)
    {
        const float bannerHeight = 26f;
        Rect bannerRect = listing.GetRect(bannerHeight);
        listing.Gap(4f);

        // Background & Outline
        Widgets.DrawBoxSolid(bannerRect, new Color(0.22f, 0.16f, 0.08f, 0.90f));
        GUI.color = new Color(0.95f, 0.65f, 0.20f, 0.85f);
        Widgets.DrawBox(bannerRect, 1);

        // Icon
        Rect iconRect = new Rect(bannerRect.x + 6f, bannerRect.y + 5f, 16f, 16f);
        GUI.color = new Color(1f, 0.75f, 0.20f);
        GUI.DrawTexture(iconRect, TexButton.Info);

        // Warning Label
        Rect textRect = new Rect(iconRect.xMax + 6f, bannerRect.y, bannerRect.width - iconRect.xMax - 12f, bannerHeight);
        TextAnchor prevAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(1f, 0.92f, 0.80f);
        Widgets.Label(textRect, "RimTalk.Settings.PromptPreset.HistoryDisabledWarning".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = prevAnchor;
    }

    private void DrawPresetListPanel(Rect rect, PromptManager manager)
    {
        Widgets.DrawBoxSolid(rect, LeftPanelBackground);

        float buttonSize = 20f;
        float listPaddingX = 2f;
        float scrollBarWidth = 16f;
        float listWidth = rect.width - (listPaddingX * 2);
        float viewWidth = listWidth - scrollBarWidth;
        float rowButtonX = viewWidth - buttonSize - 2f;
        float headerButtonX = listPaddingX + rowButtonX;
        float y = rect.y + 6f;

        // Presets Header
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(rect.x + 5f, y, rect.width - 35f, 20f),
            "RimTalk.Settings.PromptPreset.Presets".Translate());

        GUI.color = AddGreen;
        Rect addPresetRect = new Rect(rect.x + headerButtonX, y, buttonSize, buttonSize);
        if (UIUtil.ButtonText(addPresetRect, "+"))
        {
            // Use CreateNewPreset to generate from factory default instead of duplicating current
            var p = manager.CreateNewPreset("RimTalk.Settings.PromptPreset.NewPresetName".Translate());
            if (p != null)
            {
                _selectedPresetId = p.Id;
                _selectedEntryId = p.Entries.FirstOrDefault()?.Id;
            }
        }

        GUI.color = Color.white;
        TooltipHandler.TipRegion(addPresetRect, "RimTalk.Settings.PromptPreset.NewPreset".Translate());
        y += 22f;

        // Preset ScrollView
        Text.Font = GameFont.Small;
        Rect listRect = new Rect(rect.x + listPaddingX, y, listWidth, 150f);
        Rect viewRect = new Rect(0f, 0f, viewWidth, manager.Presets.Count * 25f);
        Widgets.BeginScrollView(listRect, ref _presetListScrollPos, viewRect);
        float py = 0f;
        for (int i = 0; i < manager.Presets.Count; i++)
        {
            var p = manager.Presets[i];
            Rect row = new Rect(0f, py, viewRect.width, 24f);
            if (_selectedPresetId == p.Id) Widgets.DrawHighlight(row);

            if (p.IsActive)
            {
                GUI.color = Color.green;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(4f, py, 16f, 24f), "▶");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            if (UIUtil.ButtonText(new Rect(24f, py, viewRect.width - 48f, 24f), p.Name, false))
            {
                _selectedPresetId = p.Id;
                _selectedEntryId = p.Entries.FirstOrDefault()?.Id;
            }

            if (p.Name != DefaultPresetName && manager.Presets.Count > 1)
            {
                Rect delRect = new Rect(rowButtonX, py + 2f, buttonSize, buttonSize);
                GUI.color = DeleteRed;
                if (UIUtil.ButtonText(delRect, "×"))
                {
                    manager.RemovePreset(p.Id);
                    if (_selectedPresetId == p.Id)
                    {
                        var next = manager.Presets.FirstOrDefault();
                        _selectedPresetId = next?.Id;
                        _selectedEntryId = next?.Entries.FirstOrDefault()?.Id;
                    }
                }

                GUI.color = Color.white;
                TooltipHandler.TipRegion(delRect, "RimTalk.Settings.PromptPreset.Delete".Translate());
            }

            py += 25f;
        }

        Widgets.EndScrollView();
        y += 155f;

        var sel = manager.Presets.FirstOrDefault(x => x.Id == _selectedPresetId);
        if (sel != null)
        {
            float btnW2 = (rect.width - 15f) / 2f;

            bool isAlreadyActive = sel.IsActive;
            if (isAlreadyActive) GUI.enabled = false;
            if (UIUtil.ButtonText(new Rect(rect.x + 5f, y, btnW2, 24f),
                    "RimTalk.Settings.PromptPreset.Activate".Translate()))
            {
                manager.SetActivePreset(sel.Id);
            }

            if (isAlreadyActive) GUI.enabled = true;

            if (UIUtil.ButtonText(new Rect(rect.x + 10f + btnW2, y, btnW2, 24f),
                    "RimTalk.Settings.PromptPreset.Duplicate".Translate()))
            {
                var c = manager.DuplicatePreset(sel.Id);
                if (c != null)
                {
                    _selectedPresetId = c.Id;
                    _selectedEntryId = c.Entries.FirstOrDefault()?.Id;
                }
            }

            y += 28f;

            if (UIUtil.ButtonText(new Rect(rect.x + 5f, y, btnW2, 24f),
                    "RimTalk.Settings.PromptPreset.Import".Translate())) ShowImportMenu(manager);
            if (UIUtil.ButtonText(new Rect(rect.x + 10f + btnW2, y, btnW2, 24f),
                    "RimTalk.Settings.PromptPreset.Export".Translate()))
            {
                if (PresetSerializer.ExportToFile(sel))
                {
                    var exportDir = PresetSerializer.GetExportDirectory();
                    Messages.Message("RimTalk.Settings.PromptPreset.ExportSuccess".Translate(exportDir),
                        MessageTypeDefOf.PositiveEvent, false);
                }
                else
                    Messages.Message("RimTalk.Settings.PromptPreset.ExportFailed".Translate(),
                        MessageTypeDefOf.RejectInput, false);
            }

            y += 32f;
        }

        y += 5f;

        if (sel != null)
        {
            float ey = 0f;
            const float entryHeaderHeight = 22f;
            GUI.color = AddGreen;
            Rect addRect = new Rect(rect.x + headerButtonX, y, buttonSize, buttonSize);
            if (UIUtil.ButtonText(addRect, "+"))
            {
                var newEntry = new PromptEntry("RimTalk.Settings.PromptPreset.NewEntryName".Translate(), "",
                    PromptRole.User)
                {
                    Position = PromptPosition.Relative
                };

                int insertIndex = sel.Entries.Count;
                if (!string.IsNullOrEmpty(_selectedEntryId))
                {
                    int currentIndex = sel.Entries.FindIndex(en => en.Id == _selectedEntryId);
                    if (currentIndex >= 0) insertIndex = currentIndex + 1;
                }

                sel.Entries.Insert(insertIndex, newEntry);
                _selectedEntryId = newEntry.Id;
            }

            GUI.color = Color.white;
            TooltipHandler.TipRegion(addRect, "RimTalk.Settings.PromptPreset.NewEntry".Translate());

            Text.Font = GameFont.Small;
            float listTop = y + entryHeaderHeight;
            Rect eListRect = new Rect(rect.x + listPaddingX, listTop, listWidth, rect.yMax - listTop - 35f);
            Rect eViewRect = new Rect(0f, 0f, viewWidth, sel.Entries.Count * 25f);
            Widgets.BeginScrollView(eListRect, ref _entryListScrollPos, eViewRect);

            for (int i = 0; i < sel.Entries.Count; i++)
            {
                var entry = sel.Entries[i];
                Rect erow = new Rect(0f, ey, eViewRect.width, 24f);
                if (_selectedEntryId == entry.Id) Widgets.DrawHighlight(erow);

                bool isHistoryMarker = entry.IsMainChatHistory;

                bool en = entry.Enabled;
                Widgets.Checkbox(new Vector2(4f, ey + 4f), ref en, 16f);
                entry.Enabled = en;

                if (UIUtil.ButtonText(new Rect(24f, ey, eViewRect.width - 48f, 24f), entry.Name, false))
                    _selectedEntryId = entry.Id;

                if (!isHistoryMarker)
                {
                    Rect edel = new Rect(rowButtonX, ey + 2f, buttonSize, buttonSize);
                    GUI.color = DeleteRed;
                    if (UIUtil.ButtonText(edel, "×"))
                    {
                        sel.RemoveEntry(entry.Id);
                        if (_selectedEntryId == entry.Id) _selectedEntryId = sel.Entries.FirstOrDefault()?.Id;
                    }

                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(edel, "RimTalk.Settings.PromptPreset.Delete".Translate());
                }

                ey += 25f;
            }

            Widgets.EndScrollView();

            if (_selectedEntryId != null)
            {
                var selectedEntry = sel.GetEntry(_selectedEntryId);
                if (selectedEntry != null)
                {
                    int index = sel.Entries.IndexOf(selectedEntry);

                    float sw = (rect.width - 15f) / 2f;
                    
                    // Up button
                    if (index > 0)
                    {
                        if (UIUtil.ButtonText(new Rect(rect.x + 5f, rect.yMax - 32f, sw, 24f), "▲"))
                        {
                            sel.Entries.RemoveAt(index);
                            sel.Entries.Insert(index - 1, selectedEntry);
                        }
                    }
                    else
                    {
                        GUI.enabled = false;
                        Widgets.ButtonText(new Rect(rect.x + 5f, rect.yMax - 32f, sw, 24f), "▲");
                        GUI.enabled = true;
                    }

                    // Down button
                    if (index < sel.Entries.Count - 1)
                    {
                        if (UIUtil.ButtonText(new Rect(rect.x + 10f + sw, rect.yMax - 32f, sw, 24f), "▼"))
                        {
                            sel.Entries.RemoveAt(index);
                            sel.Entries.Insert(index + 1, selectedEntry);
                        }
                    }
                    else
                    {
                        GUI.enabled = false;
                        Widgets.ButtonText(new Rect(rect.x + 10f + sw, rect.yMax - 32f, sw, 24f), "▼");
                        GUI.enabled = true;
                    }
                }
            }
        }
    }

    private void ShowImportMenu(PromptManager manager)
    {
        var files = PresetSerializer.GetAvailablePresetFiles();

        if (files.Count == 0)
        {
            var exportDir = PresetSerializer.GetExportDirectory();
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimTalk.Settings.PromptPreset.NoPresetsToImport".Translate(exportDir),
                "OK".Translate()));
            return;
        }

        var options = new List<FloatMenuOption>();

        foreach (var file in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            options.Add(new FloatMenuOption(fileName, () =>
            {
                var preset = PresetSerializer.ImportFromFile(file);
                if (preset != null)
                {
                    preset.Name = manager.GetUniqueName(preset.Name);
                    manager.AddPreset(preset);
                    _selectedPresetId = preset.Id;
                    _selectedEntryId = null;
                    Messages.Message("RimTalk.Settings.PromptPreset.ImportSuccess".Translate(preset.Name),
                        MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message("RimTalk.Settings.PromptPreset.ImportFailed".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
            }));
        }

        options.Add(new FloatMenuOption("RimTalk.Settings.PromptPreset.OpenFolder".Translate(), () =>
        {
            var exportDir = PresetSerializer.GetExportDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = exportDir,
                UseShellExecute = true
            });
        }));

        Find.WindowStack.Add(new FloatMenu(options));
    }
}
