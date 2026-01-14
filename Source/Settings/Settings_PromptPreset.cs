using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RimTalk.Prompt;
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
    private const string DefaultPresetName = "RimTalk Default";

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

    private enum PresetSection { System, History, Prompt }

    private PresetSection GetSection(PromptEntry entry)
    {
        if (entry.IsMainChatHistory || entry.Position == PromptPosition.InChat)
            return PresetSection.History;
        if (entry.Role == PromptRole.System && entry.Position == PromptPosition.Relative)
            return PresetSection.System;
        return PresetSection.Prompt;
    }

    public void DrawPromptPresetSettings(Listing_Standard listingStandard, Rect inRect)
    {
        RimTalkSettings settings = Get();
        if (settings.UseAdvancedPromptMode)
            DrawAdvancedPromptMode(listingStandard, settings, inRect);
        else
            DrawSimplePromptMode(listingStandard, settings);
    }

    private void DrawSimplePromptMode(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        DrawAIInstructionSettings(listingStandard, showAdvancedSwitch: true);
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
        float y = rect.y + 5f;

        // Presets Header
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(rect.x + 5f, y, rect.width - 35f, 20f),
            "RimTalk.Settings.PromptPreset.Presets".Translate());

        GUI.color = AddGreen;
        Rect addPresetRect = new Rect(rect.x + headerButtonX, y, buttonSize, buttonSize);
        if (Widgets.ButtonText(addPresetRect, "+"))
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

            if (Widgets.ButtonText(new Rect(24f, py, viewRect.width - 48f, 24f), p.Name, false))
            {
                _selectedPresetId = p.Id;
                _selectedEntryId = p.Entries.FirstOrDefault()?.Id;
            }

            if (p.Name != DefaultPresetName && manager.Presets.Count > 1)
            {
                Rect delRect = new Rect(rowButtonX, py + 2f, buttonSize, buttonSize);
                GUI.color = DeleteRed;
                if (Widgets.ButtonText(delRect, "×"))
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
            if (Widgets.ButtonText(new Rect(rect.x + 5f, y, btnW2, 24f),
                    "RimTalk.Settings.PromptPreset.Activate".Translate()))
            {
                manager.SetActivePreset(sel.Id);
            }

            if (isAlreadyActive) GUI.enabled = true;

            if (Widgets.ButtonText(new Rect(rect.x + 10f + btnW2, y, btnW2, 24f),
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

            if (Widgets.ButtonText(new Rect(rect.x + 5f, y, btnW2, 24f),
                    "RimTalk.Settings.PromptPreset.Import".Translate())) ShowImportMenu(manager);
            if (Widgets.ButtonText(new Rect(rect.x + 10f + btnW2, y, btnW2, 24f),
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
            var sections = new[] { PresetSection.System, PresetSection.History, PresetSection.Prompt };
            var sectionNames = new Dictionary<PresetSection, string>
            {
                { PresetSection.System, "RimTalk.Settings.PromptPreset.SectionSystem".Translate() },
                { PresetSection.History, "RimTalk.Settings.PromptPreset.SectionHistory".Translate() },
                { PresetSection.Prompt, "RimTalk.Settings.PromptPreset.SectionPrompt".Translate() }
            };

            // Calculate total height for ScrollView
            float totalHeight = 0;
            foreach (var section in sections)
            {
                totalHeight += 22f; // Header
                totalHeight += sel.Entries.Count(en => GetSection(en) == section) * 25f;
            }

            Text.Font = GameFont.Small;
            Rect eListRect = new Rect(rect.x + listPaddingX, y, listWidth, rect.yMax - y - 35f);
            Rect eViewRect = new Rect(0f, 0f, viewWidth, totalHeight);
            Widgets.BeginScrollView(eListRect, ref _entryListScrollPos, eViewRect);

            foreach (var section in sections)
            {
                // Section Header
                Rect headerRect = new Rect(0f, ey, eViewRect.width, 20f);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(5f, ey, eViewRect.width - 30f, 20f), sectionNames[section]);
                GUI.color = Color.white;

                // Section Add Button
                GUI.color = AddGreen;
                Rect addRect = new Rect(rowButtonX, ey, buttonSize, buttonSize);
                if (Widgets.ButtonText(addRect, "+"))
                {
                    PromptEntry newEntry = null;
                    switch (section)
                    {
                        case PresetSection.System:
                            newEntry = new PromptEntry("RimTalk.Settings.PromptPreset.NewEntryName".Translate(), "",
                                PromptRole.System);
                            newEntry.Position = PromptPosition.Relative;
                            break;
                        case PresetSection.History:
                            // If no history marker, add it. Otherwise add InChat entry.
                            if (!sel.Entries.Any(en => en.IsMainChatHistory))
                            {
                                newEntry = new PromptEntry("Chat History", "{{chat.history}}", PromptRole.User);
                                newEntry.Position = PromptPosition.Relative;
                                newEntry.IsMainChatHistory = true;
                            }
                            else
                            {
                                newEntry = new PromptEntry("RimTalk.Settings.PromptPreset.NewEntryName".Translate(),
                                    "", PromptRole.User);
                                newEntry.Position = PromptPosition.InChat;
                                newEntry.InChatDepth = 1; // Default depth
                            }

                            break;
                        case PresetSection.Prompt:
                            newEntry = new PromptEntry("RimTalk.Settings.PromptPreset.NewEntryName".Translate(), "",
                                PromptRole.User);
                            newEntry.Position = PromptPosition.Relative;
                            break;
                    }

                    if (newEntry != null)
                    {
                        // Insert at the end of the section
                        int lastIndex = -1;
                        for (int i = 0; i < sel.Entries.Count; i++)
                        {
                            if (GetSection(sel.Entries[i]) == section) lastIndex = i;
                        }

                        if (lastIndex == -1)
                        {
                            // If section is empty, find where to insert
                            if (section == PresetSection.System) sel.Entries.Insert(0, newEntry);
                            else if (section == PresetSection.History)
                            {
                                int sysEnd = sel.Entries.FindLastIndex(en => GetSection(en) == PresetSection.System);
                                sel.Entries.Insert(sysEnd + 1, newEntry);
                            }
                            else sel.Entries.Add(newEntry);
                        }
                        else
                        {
                            sel.Entries.Insert(lastIndex + 1, newEntry);
                        }

                        _selectedEntryId = newEntry.Id;
                    }
                }

                GUI.color = Color.white;

                ey += 22f;
                Text.Font = GameFont.Small;

                var sectionEntries = sel.Entries.Where(en => GetSection(en) == section).ToList();
                for (int i = 0; i < sectionEntries.Count; i++)
                {
                    var entry = sectionEntries[i];
                    Rect erow = new Rect(0f, ey, eViewRect.width, 24f);
                    if (_selectedEntryId == entry.Id) Widgets.DrawHighlight(erow);

                    bool isHistoryMarker = entry.IsMainChatHistory;

                    bool en = entry.Enabled;
                    Widgets.Checkbox(new Vector2(4f, ey + 4f), ref en, 16f);
                    entry.Enabled = en;

                    if (Widgets.ButtonText(new Rect(24f, ey, eViewRect.width - 48f, 24f), entry.Name, false))
                        _selectedEntryId = entry.Id;

                    if (!isHistoryMarker)
                    {
                        Rect edel = new Rect(rowButtonX, ey + 2f, buttonSize, buttonSize);
                        GUI.color = DeleteRed;
                        if (Widgets.ButtonText(edel, "×"))
                        {
                            sel.RemoveEntry(entry.Id);
                            if (_selectedEntryId == entry.Id) _selectedEntryId = sel.Entries.FirstOrDefault()?.Id;
                        }

                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(edel, "RimTalk.Settings.PromptPreset.Delete".Translate());
                    }

                    ey += 25f;
                }
            }

            Widgets.EndScrollView();

            if (_selectedEntryId != null)
            {
                var selectedEntry = sel.GetEntry(_selectedEntryId);
                if (selectedEntry != null)
                {
                    var section = GetSection(selectedEntry);
                    var sectionEntries = sel.Entries.Where(en => GetSection(en) == section).ToList();
                    int indexInSection = sectionEntries.IndexOf(selectedEntry);

                    float sw = (rect.width - 15f) / 2f;
                    
                    // Up button
                    bool canMoveUp = indexInSection > 0;
                    if (canMoveUp && section == PresetSection.History)
                    {
                        var prevEntry = sectionEntries[indexInSection - 1];
                        if (prevEntry.IsMainChatHistory) canMoveUp = false;
                    }

                    if (canMoveUp)
                    {
                        if (Widgets.ButtonText(new Rect(rect.x + 5f, rect.yMax - 32f, sw, 24f), "▲"))
                        {
                            int actualIndex = sel.Entries.IndexOf(selectedEntry);
                            sel.Entries.RemoveAt(actualIndex);
                            sel.Entries.Insert(actualIndex - 1, selectedEntry);
                        }
                    }
                    else
                    {
                        GUI.enabled = false;
                        Widgets.ButtonText(new Rect(rect.x + 5f, rect.yMax - 32f, sw, 24f), "▲");
                        GUI.enabled = true;
                    }

                    // Down button
                    if (indexInSection < sectionEntries.Count - 1)
                    {
                        if (Widgets.ButtonText(new Rect(rect.x + 10f + sw, rect.yMax - 32f, sw, 24f), "▼"))
                        {
                            int actualIndex = sel.Entries.IndexOf(selectedEntry);
                            sel.Entries.RemoveAt(actualIndex);
                            sel.Entries.Insert(actualIndex + 1, selectedEntry);
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

    private void DrawEntryEditor(Rect rect, PromptManager manager, RimTalkSettings settings)
    {
        var p = manager.Presets.FirstOrDefault(p => p.Id == _selectedPresetId);
        if (p == null)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(rect, "RimTalk.Settings.PromptPreset.SelectEntryToEdit".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        float y = rect.y + 2f;

        // --- Layout Constants ---
        float labelX = rect.x + 10f;
        float inputX = rect.x + 115f;
        float inputWidth = 200f;
        float topButtonWidth = 200f;
        float topButtonX = rect.x + rect.width - topButtonWidth - 10f;
        float dropdownWidth = 120f;

        // -- Row 1: Preset Name & Simple Mode --
        Widgets.Label(new Rect(labelX, y, 100f, 24f), "RimTalk.Settings.PromptPreset.PresetName".Translate());
        p.Name = Widgets.TextField(new Rect(inputX, y, inputWidth, 24f), p.Name);

        if (Widgets.ButtonText(new Rect(topButtonX, y, topButtonWidth, 24f),
                "RimTalk.Settings.SwitchToSimpleSettings".Translate()))
            settings.UseAdvancedPromptMode = false;

        y += 28f;

        // -- Row 2: Reset Button --
        if (Widgets.ButtonText(new Rect(topButtonX, y, topButtonWidth, 24f),
                "RimTalk.Settings.ResetToDefault".Translate()))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RimTalk.Settings.ResetConfirm".Translate(), () =>
            {
                manager.ResetToDefaults();
                _selectedPresetId = null;
                _selectedEntryId = null;
            }));
        }

        var e = p.Entries.FirstOrDefault(x => x.Id == _selectedEntryId);
        if (e == null)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, y + 28f, rect.width, rect.height - (y + 28f)),
                "RimTalk.Settings.PromptPreset.SelectEntryToEdit".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        // -- Row 2 (Left Side): Entry Name --
        bool isHistoryMarker = e.IsMainChatHistory;
        var section = GetSection(e);

        Widgets.Label(new Rect(labelX, y, 100f, 24f), "RimTalk.Settings.PromptPreset.EntryName".Translate());
        if (isHistoryMarker)
        {
            GUI.enabled = false;
            Widgets.TextField(new Rect(inputX, y, inputWidth, 24f), e.Name);
            GUI.enabled = true;
        }
        else
        {
            e.Name = Widgets.TextField(new Rect(inputX, y, inputWidth, 24f), e.Name);
        }

        y += 28f;

        // -- Row 3: Role --
        Widgets.Label(new Rect(labelX, y, 80f, 24f), "RimTalk.Settings.PromptPreset.Role".Translate());

        if (section == PresetSection.System)
        {
            Widgets.Label(new Rect(inputX, y, dropdownWidth, 24f), e.Role.ToString());
        }
        else if (isHistoryMarker)
        {
            Widgets.Label(new Rect(inputX, y, dropdownWidth, 24f), "User/Assistant");
        }
        else // History (InChat entries) and Prompt Section
        {
            if (Widgets.ButtonText(new Rect(inputX, y, dropdownWidth, 24f), e.Role.ToString()))
            {
                var opts = new List<FloatMenuOption>
                {
                    new("User", () => e.Role = PromptRole.User),
                    new("Assistant", () => e.Role = PromptRole.Assistant)
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }
        }

        y += 28f;

        // -- Row 4: Position --
        float tabRowY = y;
        Widgets.Label(new Rect(labelX, tabRowY, 80f, 24f), "RimTalk.Settings.PromptPreset.Position".Translate());

        Widgets.Label(new Rect(inputX, tabRowY, dropdownWidth, 24f), e.Position.ToString());

        if (e.Position == PromptPosition.InChat)
        {
            float depthLabelX = inputX + dropdownWidth + 10f;
            Widgets.Label(new Rect(depthLabelX, tabRowY, 50f, 24f), "RimTalk.Settings.PromptPreset.Depth".Translate());
            
            if (_depthBufferEntryId != e.Id)
            {
                _depthBuffer = e.InChatDepth.ToString();
                _depthBufferEntryId = e.Id;
            }

            _depthBuffer = Widgets.TextField(new Rect(depthLabelX + 55f, tabRowY, 60f, 24f), _depthBuffer);
            if (int.TryParse(_depthBuffer, out int res)) e.InChatDepth = res;
        }

        // -- TABS (Toggles) --
        float tabWidth = 70f;
        float rightEdge = rect.xMax - 5f;

        void DrawToggleTab(string label, ref bool isOpen, int indexFromRight, bool isRadio = false, int radioMode = 0)
        {
            Rect tabRect = new Rect(rightEdge - (tabWidth * (indexFromRight + 1)) - (5f * indexFromRight), tabRowY,
                tabWidth, 24f);

            bool active = isRadio
                ? (_showSidePanel && _sidePanelMode == radioMode)
                : isOpen;

            GUI.color = active ? Color.green : Color.white;

            if (Widgets.ButtonText(tabRect, label))
            {
                if (isRadio)
                {
                    if (active) _showSidePanel = false;
                    else
                    {
                        _showSidePanel = true;
                        _sidePanelMode = radioMode;
                        _auxScrollPos = Vector2.zero;
                    }
                }
                else
                {
                    isOpen = !isOpen;
                    if (isOpen) _previewScrollPos = Vector2.zero;
                }
            }

            GUI.color = Color.white;
        }

        // Help (Side Panel Mode 1)
        DrawToggleTab("RimTalk.Settings.PromptHelp".Translate(), ref _showSidePanel, 0, true, 1);

        // Variables (Side Panel Mode 0)
        DrawToggleTab("RimTalk.Settings.ShowVariables".Translate(), ref _showSidePanel, 1, true, 0);

        // Preview (Bottom Panel)
        DrawToggleTab("RimTalk.Settings.PromptPreset.ModePreview".Translate(), ref _showPreview, 2);

        y += 28f;

        // -- MAIN AREA (Layout Split Logic) --
        Rect bottomArea = new Rect(rect.x + 10f, y, rect.width - 20f, rect.yMax - y - 5f);

        // 1. Calculate Horizontal Split (Main vs Side Panel)
        Rect mainWorkRect = bottomArea;
        Rect sidePanelRect = Rect.zero;
        Rect splitHorizRect = Rect.zero;

        float splitterSize = 6f;

        if (_showSidePanel)
        {
            float minMainW = 150f;
            float minSideW = 150f;
            float maxRatioH = (bottomArea.width - minSideW - splitterSize) / bottomArea.width;
            float minRatioH = minMainW / bottomArea.width;

            _splitRatioHoriz = Mathf.Clamp(_splitRatioHoriz, minRatioH, maxRatioH);

            float leftW = (bottomArea.width * _splitRatioHoriz) - (splitterSize / 2f);
            float rightW = bottomArea.width - leftW - splitterSize;

            mainWorkRect = new Rect(bottomArea.x, bottomArea.y, leftW, bottomArea.height);
            splitHorizRect = new Rect(bottomArea.x + leftW, bottomArea.y, splitterSize, bottomArea.height);
            sidePanelRect = new Rect(bottomArea.x + leftW + splitterSize, bottomArea.y, rightW, bottomArea.height);
        }

        // 2. Calculate Vertical Split (Editor vs Preview) within mainWorkRect
        Rect editorRect = mainWorkRect;
        Rect previewRect = Rect.zero;
        Rect splitVertRect = Rect.zero;

        if (_showPreview)
        {
            float minEditorH = 100f;
            float minPrevH = 60f;
            float maxRatioV = (mainWorkRect.height - minPrevH - splitterSize) / mainWorkRect.height;
            float minRatioV = minEditorH / mainWorkRect.height;

            _splitRatioVert = Mathf.Clamp(_splitRatioVert, minRatioV, maxRatioV);

            float topH = (mainWorkRect.height * _splitRatioVert) - (splitterSize / 2f);
            float botH = mainWorkRect.height - topH - splitterSize;

            editorRect = new Rect(mainWorkRect.x, mainWorkRect.y, mainWorkRect.width, topH);
            splitVertRect = new Rect(mainWorkRect.x, mainWorkRect.y + topH, mainWorkRect.width, splitterSize);
            previewRect = new Rect(mainWorkRect.x, mainWorkRect.y + topH + splitterSize, mainWorkRect.width, botH);
        }

        // --- DRAWING ---

        // A. Draw Editor
        float editorInnerWidth = editorRect.width - 20f;
        float editorContentHeight = Mathf.Ceil(Mathf.Max(editorRect.height, Text.CalcHeight(e.Content, editorInnerWidth) + 25f));
        Rect editorViewRect = new Rect(0f, 0f, editorInnerWidth, editorContentHeight);

        const string editorControlName = "PromptEntryEditor";
        Widgets.BeginScrollView(editorRect, ref _promptContentScrollPos, editorViewRect);
        GUI.SetNextControlName(editorControlName);
        
        string newContent;
        if (isHistoryMarker)
        {
            Widgets.TextArea(new Rect(0f, 0f, editorInnerWidth, editorContentHeight), e.Content, readOnly: true);
            newContent = e.Content;
        }
        else
        {
            newContent = Widgets.TextArea(new Rect(0f, 0f, editorInnerWidth, editorContentHeight), e.Content);
        }
        
        // Auto-scroll logic: only scroll if the cursor position changed
        if (GUI.GetNameOfFocusedControl() == editorControlName)
        {
            TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (te != null && te.cursorIndex != _lastPromptEditorCursorPos)
            {
                _lastPromptEditorCursorPos = te.cursorIndex;
                float cursorY = te.graphicalCursorPos.y;
                if (cursorY < _promptContentScrollPos.y)
                    _promptContentScrollPos.y = cursorY;
                else if (cursorY + 25f > _promptContentScrollPos.y + editorRect.height)
                    _promptContentScrollPos.y = cursorY + 25f - editorRect.height;
            }
        }
        Widgets.EndScrollView();

        if (newContent != e.Content)
        {
            e.Content = newContent;
            if (_showSidePanel && _sidePanelMode == 0) UpdateSmartFilter(newContent);
        }

        // B. Draw Preview (if active)
        if (_showPreview)
        {
            Widgets.DrawBoxSolid(splitVertRect, new Color(0.2f, 0.2f, 0.2f));
            GUI.DrawTexture(new Rect(splitVertRect.center.x - 10f, splitVertRect.center.y - 2f, 20f, 4f),
                BaseContent.WhiteTex);
            Widgets.DrawHighlightIfMouseover(splitVertRect);

            // Handle Vertical Splitter Input
            if (Event.current.type == EventType.MouseDown && splitVertRect.Contains(Event.current.mousePosition))
            {
                _isDraggingVert = true;
                Event.current.Use();
            }

            Widgets.DrawBoxSolid(previewRect, new Color(0.1f, 0.1f, 0.1f, 0.5f));
            DrawPreviewContent(previewRect, e.Content);
        }

        // C. Draw Side Panel (if active)
        if (_showSidePanel)
        {
            Widgets.DrawBoxSolid(splitHorizRect, new Color(0.2f, 0.2f, 0.2f));
            GUI.DrawTexture(new Rect(splitHorizRect.center.x - 2f, splitHorizRect.center.y - 10f, 4f, 20f),
                BaseContent.WhiteTex);
            Widgets.DrawHighlightIfMouseover(splitHorizRect);

            // Handle Horizontal Splitter Input
            if (Event.current.type == EventType.MouseDown && splitHorizRect.Contains(Event.current.mousePosition))
            {
                _isDraggingHoriz = true;
                Event.current.Use();
            }

            DrawSidePanel(sidePanelRect, manager, e);
        }

        // D. Handle Drag Logic (Global)
        if (_isDraggingVert)
        {
            if (Event.current.type == EventType.MouseDrag)
            {
                _splitRatioVert += Event.current.delta.y / mainWorkRect.height;
                Event.current.Use();
            }

            if (Event.current.rawType == EventType.MouseUp) _isDraggingVert = false;
        }

        if (_isDraggingHoriz)
        {
            if (Event.current.type == EventType.MouseDrag)
            {
                _splitRatioHoriz += Event.current.delta.x / bottomArea.width;
                Event.current.Use();
            }

            if (Event.current.rawType == EventType.MouseUp) _isDraggingHoriz = false;
        }
    }

    private void UpdateSmartFilter(string text)
    {
        // Only trigger smart filter if we are in the Variables tab
        if (!_showSidePanel || _sidePanelMode != 0) return;

        // Try to get cursor position from Unity's TextEditor
        TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
        if (te == null || te.cursorIndex < 0) return;

        int pos = te.cursorIndex;
        if (pos > text.Length) pos = text.Length;

        // Look back from cursor to find the start of the current "word"
        int start = pos - 1;
        while (start >= 0 && !char.IsWhiteSpace(text[start]) && text[start] != '{' && text[start] != '}')
        {
            start--;
        }

        start++;

        string currentWord = "";
        if (start < pos)
        {
            currentWord = text.Substring(start, pos - start);
        }

        // Logic to decide when to update the search query
        bool shouldUpdate = false;
        
        // Check if we are inside brackets {{ ... }}
        int check = start - 1;
        while (check >= 0 && char.IsWhiteSpace(text[check])) check--;
        bool insideBrackets = (check >= 1 && text[check] == '{' && text[check-1] == '{');

        if (insideBrackets)
        {
            // 1. Always update if it contains a dot (property access)
            if (currentWord.Contains(".")) shouldUpdate = true;
            
            // 2. Update if length >= 2 (standard word)
            else if (currentWord.Length >= 2) shouldUpdate = true;
            
            // 3. Update (clear/shorten) if we are backspacing from a previously longer query
            else if (_variableSearchQuery.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase) && _variableSearchQuery.Length > currentWord.Length)
            {
                shouldUpdate = true;
            }
            
            // 4. Always update if it's empty but inside brackets (to show all variables)
            else if (currentWord.Length == 0) shouldUpdate = true;
        }

        if (shouldUpdate)
        {
            _variableSearchQuery = currentWord;
        }
    }

    private void DrawPreviewContent(Rect rect, string content)
    {
        string text = PresetPreviewGenerator.GeneratePreview(content);
        Text.Font = GameFont.Small;

        Rect innerRect = rect.ContractedBy(5f);
        float viewWidth = innerRect.width - 16f;
        float height = Text.CalcHeight(text, viewWidth);

        Rect viewRect = new Rect(0f, 0f, viewWidth, height);

        Widgets.BeginScrollView(innerRect, ref _previewScrollPos, viewRect);
        Widgets.TextArea(viewRect, text, readOnly: true);
        Widgets.EndScrollView();
    }

    private void DrawSidePanel(Rect rect, PromptManager manager, PromptEntry entry)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.5f));
        Rect contentRect = rect.ContractedBy(5f);

        if (_sidePanelMode == 0)
        {
            // Variables
            DrawVariablePreviewContent(contentRect, manager, entry);
        }
        else
        {
            // Help
            string text = "RimTalk.Settings.AdvancedHelpContent".Translate();
            Text.Font = GameFont.Small;
            float viewWidth = contentRect.width - 16f;
            float height = Text.CalcHeight(text, viewWidth);

            Rect viewRect = new Rect(0f, 0f, viewWidth, height);

            Widgets.BeginScrollView(contentRect, ref _auxScrollPos, viewRect);
            Widgets.TextArea(viewRect, text, readOnly: true);
            Widgets.EndScrollView();
        }
    }

    private void DrawVariablePreviewContent(Rect rect, PromptManager manager, PromptEntry entry)
    {
        // 1. Search Bar
        Rect searchRect = new Rect(rect.x, rect.y, rect.width, 24f);
        _variableSearchQuery = Widgets.TextField(searchRect, _variableSearchQuery);
        if (string.IsNullOrEmpty(_variableSearchQuery))
        {
            GUI.color = new Color(1, 1, 1, 0.3f);
            Widgets.Label(searchRect.ContractedBy(2f, 0f), "RimTalk.Settings.PromptPreset.SearchPlaceholder".Translate());
            GUI.color = Color.white;
        }

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(rect.x, rect.y + 26f, rect.width, 20f),
            "RimTalk.Settings.PromptPreset.VariablePreviewHint".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        Rect listRect = new Rect(rect.x, rect.y + 45f, rect.width, rect.height - 45f);
        var builtin = VariableDefinitions.GetScribanVariables();

        // 2. Filter Logic
        string query = _variableSearchQuery.Trim().ToLowerInvariant();
        var filteredBuiltin = new Dictionary<string, List<(string, string)>>();

        foreach (var cat in builtin)
        {
            var matches = cat.Value.Where(v =>
                v.Item1.ToLowerInvariant().Contains(query) ||
                v.Item2.ToLowerInvariant().Contains(query) ||
                cat.Key.ToLowerInvariant().Contains(query)
            ).ToList();

            if (matches.Any()) filteredBuiltin[cat.Key] = matches;
        }

        // --- Dynamic Variable Discovery ---
        if (_variableSearchQuery.Contains("."))
        {
            var dynamicVars = VariableDefinitions.GetDynamicVariables(_variableSearchQuery, entry.Content);
            foreach (var kvp in dynamicVars)
            {
                filteredBuiltin[kvp.Key] = kvp.Value;
            }
        }

        var runtimeVars = manager.VariableStore.GetAllVariables()
            .Where(kvp => kvp.Key.ToLowerInvariant().Contains(query) || kvp.Value.ToLowerInvariant().Contains(query))
            .ToList();

        // 3. Dynamic height calculation
        float totalRows = filteredBuiltin.Sum(c => c.Value.Count + 1);
        if (runtimeVars.Any()) totalRows += runtimeVars.Count + 1;

        Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, totalRows * 22f);

        Widgets.BeginScrollView(listRect, ref _auxScrollPos, viewRect);
        float vy = 0f;

        // 4. Render Filtered Builtin
        string prefixToStrip = "";
        int lastDotIndex = _variableSearchQuery.LastIndexOf('.');
        if (lastDotIndex >= 0)
        {
            prefixToStrip = _variableSearchQuery.Substring(0, lastDotIndex + 1);
        }

        foreach (var cat in filteredBuiltin)
        {
            GUI.color = Color.cyan;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, vy, viewRect.width, 20f), $"▼ {cat.Key}");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            vy += 22f;
            foreach (var v in cat.Value)
            {
                string displayLabel = null;
                if (!string.IsNullOrEmpty(prefixToStrip) && v.Item1.StartsWith(prefixToStrip, StringComparison.OrdinalIgnoreCase))
                {
                    displayLabel = v.Item1.Substring(prefixToStrip.Length);
                }
                DrawVariableRow(ref vy, viewRect.width, v.Item1, v.Item2, null, entry, displayLabel);
            }
        }

        // 5. Render Filtered Runtime
        if (runtimeVars.Any())
        {
            GUI.color = Color.green;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, vy, viewRect.width, 20f), "▼ Runtime Variables");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            vy += 22f;
            foreach (var kvp in runtimeVars) DrawVariableRow(ref vy, viewRect.width, kvp.Key, "", kvp.Value, entry);
        }

        Widgets.EndScrollView();
    }

    private void DrawVariableRow(ref float y, float w, string n, string d, string v, PromptEntry entry, string displayLabel = null)
    {
        Rect rowRect = new Rect(0f, y, w, 20f);
        if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

        if (Widgets.ButtonInvisible(rowRect))
        {
            InsertVariable(n, entry);
        }

        Text.Font = GameFont.Tiny;
        
        string label = displayLabel ?? n;
        string fullVar = $"{{{{ {label} }}}}";
        float labelWidth = Text.CalcSize(fullVar).x;

        // 1. Draw Variable Name
        GUI.color = new Color(0.8f, 1f, 0.8f);
        Widgets.Label(new Rect(2f, y, labelWidth + 5f, 20f), fullVar);

        // 2. Draw Type Info/Params right next to it
        string typeInfo = v ?? d;
        if (!string.IsNullOrEmpty(typeInfo))
        {
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            float typeX = labelWidth + 10f;
            float typeW = w - typeX - 5f;
            if (typeW > 10f)
            {
                Widgets.Label(new Rect(typeX, y, typeW, 20f), typeInfo);
            }
        }

        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        y += 20f;
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

    private void InsertVariable(string variableName, PromptEntry entry)
    {
        // 1. Play Sound
        SoundDefOf.Click.PlayOneShotOnCamera(null);

        // 2. Get Editor State
        TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);

        if (te != null && te.cursorIndex >= 0 && te.cursorIndex <= entry.Content.Length)
        {
            string text = entry.Content;
            int cursor = te.cursorIndex;

            // --- Step A: Identify and Remove Prefix (Autocomplete) ---
            // Find the partial word to the left (e.g., "pawn.Is")
            int start = cursor - 1;
            while (start >= 0)
            {
                char c = text[start];
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_') break;
                start--;
            }

            start++;

            string prefix = "";
            if (start < cursor) prefix = text.Substring(start, cursor - start);

            // Check if the variable starts with what we typed
            bool isMatch = !string.IsNullOrEmpty(prefix) &&
                           variableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            // If it's a match, delete what we typed so we can replace it cleanly
            if (isMatch)
            {
                text = text.Remove(start, cursor - start);
                cursor = start;
            }

            // --- Step B: Check Surroundings (Context) ---

            // Scan backwards ignoring whitespace to see if we are inside {{
            int ptr = cursor - 1;
            while (ptr >= 0 && char.IsWhiteSpace(text[ptr])) ptr--;

            bool hasOpenBrackets = (ptr >= 1 && text[ptr] == '{' && text[ptr - 1] == '{');

            // --- Step C: Build the String to Insert ---
            string finalInsert;

            if (hasOpenBrackets)
            {
                // We are already inside brackets (e.g., "{{pawn.Is|")

                // 1. Ensure space after {{
                // If the character immediately to the left is '{', add a space
                bool needsLeftSpace = (cursor > 0 && text[cursor - 1] == '{');
                finalInsert = (needsLeftSpace ? " " : "") + variableName;

                // 2. Ensure closing }}
                // Scan forward to see if }} exists reasonably close
                int endPtr = cursor;
                while (endPtr < text.Length && char.IsWhiteSpace(text[endPtr])) endPtr++;

                bool hasClosingBrackets = (endPtr < text.Length - 1 && text[endPtr] == '}' && text[endPtr + 1] == '}');

                if (!hasClosingBrackets)
                {
                    finalInsert += " }}";
                }
            }
            else
            {
                // Not inside brackets. Insert the full valid block.
                finalInsert = $"{{{{ {variableName} }}}}";
            }

            // --- Step D: Apply and Fix Cursor ---
            text = text.Insert(cursor, finalInsert);
            entry.Content = text;

            // Determine final cursor position: Always after the "}}"
            // If we added "}}", it's at the end of inserted text.
            // If "}}" already existed, we need to find them and jump past them.
            int newCursorPos;

            if (hasOpenBrackets)
            {
                // Find the first }} after our insertion point
                int closeIndex = text.IndexOf("}}", cursor);
                if (closeIndex != -1)
                    newCursorPos = closeIndex + 2; // Jump past }}
                else
                    newCursorPos = cursor + finalInsert.Length;
            }
            else
            {
                newCursorPos = cursor + finalInsert.Length;
            }

            // Apply cursor change
            te.text = text; // Update internal TE text immediately
            te.cursorIndex = newCursorPos;
            te.selectIndex = newCursorPos;
        }
        else
        {
            // Fallback if no focus
            entry.Content += $"{{{{ {variableName} }}}}";
        }
    }
}