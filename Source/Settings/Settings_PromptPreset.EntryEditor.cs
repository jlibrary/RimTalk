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

        float y = rect.y + 6f;

        var e = p.Entries.FirstOrDefault(x => x.Id == _selectedEntryId);
        if (e == null)
        {
            float emptySwitchBtnX = rect.xMax - 16f - 150f;
            Rect emptySwitchRect = new Rect(emptySwitchBtnX, y, 150f, 24f);
            if (UIUtil.ButtonText(emptySwitchRect, "RimTalk.Settings.SwitchToSimpleSettings".Translate()))
            {
                settings.UseAdvancedPromptMode = false;
                _textAreaInitialized = false;
                _aiInstructionPresetId = "";
            }

            float labelX = rect.x + 10f;
            string presetNameLabel = "RimTalk.Settings.PromptPreset.PresetName".Translate();
            float presetNameLabelW = Mathf.Max(80f, Text.CalcSize(presetNameLabel).x + 4f);
            Widgets.Label(new Rect(labelX, y, presetNameLabelW, 24f), presetNameLabel);
            p.Name = Widgets.TextField(new Rect(labelX + presetNameLabelW + 5f, y, 180f, 24f), p.Name);

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, y + 28f, rect.width, rect.height - (y + 28f)),
                "RimTalk.Settings.PromptPreset.SelectEntryToEdit".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        bool isHistoryMarker = e.IsMainChatHistory;
        bool isModEntry = !string.IsNullOrEmpty(e.SourceModId);

        const float scrollbarMargin = 16f;
        float rightEdge = rect.xMax - scrollbarMargin;

        float tabWidth = 70f;
        float tabGap = 5f;
        float tabsTotalWidth = tabWidth * 3 + tabGap * 2; // 220f

        // Row 1 Right: Switch to Simple Mode (aligned block with 3 tabs below)
        float switchBtnWidth = tabsTotalWidth;
        float switchBtnX = rightEdge - switchBtnWidth;
        Rect switchBtnRect = new Rect(switchBtnX, y, switchBtnWidth, 24f);

        if (UIUtil.ButtonText(switchBtnRect, "RimTalk.Settings.SwitchToSimpleSettings".Translate()))
        {
            settings.UseAdvancedPromptMode = false;
            _textAreaInitialized = false;
            _aiInstructionPresetId = "";
        }

        // -- Grid Columns Setup --
        float col1LabelX = rect.x + 10f;
        // Size labels to their text so longer translations don't wrap and get cut
        float col1LabelW = Mathf.Max(80f, Text.CalcSize("RimTalk.Settings.PromptPreset.PresetName".Translate()).x + 4f);
        float col1InputX = col1LabelX + col1LabelW + 6f;
        float col1InputW = 140f;

        float col2LabelX = col1InputX + col1InputW + 18f;
        float col2LabelW = Mathf.Max(70f, Text.CalcSize("RimTalk.Settings.PromptPreset.EntryName".Translate()).x + 4f);
        float col2InputX = col2LabelX + col2LabelW + 6f;
        float col2InputW = 140f;

        // -- Row 1 Left: Col 1 Preset Name & Col 2 Entry Name with Mod Badge --
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(col1LabelX, y, col1LabelW, 24f), "RimTalk.Settings.PromptPreset.PresetName".Translate());
        Text.Anchor = TextAnchor.UpperLeft;
        p.Name = Widgets.TextField(new Rect(col1InputX, y, col1InputW, 24f), p.Name);

        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(col2LabelX, y, col2LabelW, 24f), "RimTalk.Settings.PromptPreset.EntryName".Translate());
        Text.Anchor = TextAnchor.UpperLeft;

        if (e.IsBuiltIn || isModEntry)
        {
            GUI.enabled = false;
            Widgets.TextField(new Rect(col2InputX, y, col2InputW, 24f), e.Name);
            GUI.enabled = true;
        }
        else
        {
            e.Name = Widgets.TextField(new Rect(col2InputX, y, col2InputW, 24f), e.Name);
        }

        if (isModEntry)
        {
            float tagX = col2InputX + col2InputW + 10f;
            float availableTagWidth = switchBtnX - 10f - tagX;
            if (availableTagWidth > 20f)
            {
                string modName = ModLister.GetActiveModWithIdentifier(e.SourceModId)?.Name ?? e.SourceModId;
                string tagText = "RimTalk.Settings.PromptPreset.SourceMod".Translate(modName);
                Rect tagRect = new Rect(tagX, y, availableTagWidth, 24f);

                GUI.color = SourceModColor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(tagRect, tagText.Truncate(availableTagWidth));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                TooltipHandler.TipRegion(tagRect, tagText);
            }
        }

        y += 32f;

        // -- Row 2 Right: Toggle Tabs (Preview, Variables, Help) --
        void DrawToggleTab(string label, ref bool isOpen, int indexFromRight, bool isRadio = false, int radioMode = 0)
        {
            Rect tabRect = new Rect(rightEdge - (tabWidth * (indexFromRight + 1)) - (tabGap * indexFromRight), y,
                tabWidth, 24f);

            bool active = isRadio
                ? (_showSidePanel && _sidePanelMode == radioMode)
                : isOpen;

            GUI.color = active ? Color.green : Color.white;

            if (UIUtil.ButtonText(tabRect, label))
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

        // -- Row 2 Left: Col 1 Position & Col 2 Role (+ Custom Role if User) --

        // Col 1: Position (+ Depth if InChat)
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(col1LabelX, y, col1LabelW, 24f), "RimTalk.Settings.PromptPreset.Position".Translate());
        Text.Anchor = TextAnchor.UpperLeft;

        if (e.Position == PromptPosition.InChat)
        {
            float posSubW = 75f;
            if (isHistoryMarker)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(col1InputX, y, posSubW, 24f), e.Position.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (Widgets.ButtonText(new Rect(col1InputX, y, posSubW, 24f), e.Position.ToString()))
            {
                var options = new List<FloatMenuOption>
                {
                    new("RimTalk.Settings.PromptPreset.PositionRelative".Translate(),
                        () => e.Position = PromptPosition.Relative),
                    new("RimTalk.Settings.PromptPreset.PositionInChat".Translate(),
                        () => e.Position = PromptPosition.InChat)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            float depthLabelX = col1InputX + posSubW + 5f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(depthLabelX, y, 28f, 24f), "RimTalk.Settings.PromptPreset.Depth".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            if (_depthBufferEntryId != e.Id)
            {
                _depthBuffer = e.InChatDepth.ToString();
                _depthBufferEntryId = e.Id;
            }

            float depthInputX = depthLabelX + 30f;
            _depthBuffer = Widgets.TextField(new Rect(depthInputX, y, 30f, 24f), _depthBuffer);
            if (int.TryParse(_depthBuffer, out int res)) e.InChatDepth = res;
        }
        else
        {
            if (isHistoryMarker)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(col1InputX, y, col1InputW, 24f), e.Position.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else if (Widgets.ButtonText(new Rect(col1InputX, y, col1InputW, 24f), e.Position.ToString()))
            {
                var options = new List<FloatMenuOption>
                {
                    new("RimTalk.Settings.PromptPreset.PositionRelative".Translate(),
                        () => e.Position = PromptPosition.Relative),
                    new("RimTalk.Settings.PromptPreset.PositionInChat".Translate(),
                        () => e.Position = PromptPosition.InChat)
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // Col 2: Role (+ Custom Role if User)
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(col2LabelX, y, col2LabelW, 24f), "RimTalk.Settings.PromptPreset.Role".Translate());
        Text.Anchor = TextAnchor.UpperLeft;

        bool hasCustomRole = !string.IsNullOrWhiteSpace(e.CustomRole);
        if (hasCustomRole && e.Role != PromptRole.User) e.Role = PromptRole.User;

        bool isUserRole = e.Role == PromptRole.User || hasCustomRole;
        float roleDropdownW = col2InputW;

        if (isHistoryMarker)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(col2InputX, y, roleDropdownW, 24f), e.Role.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
        }
        else if (Widgets.ButtonText(new Rect(col2InputX, y, roleDropdownW, 24f), e.Role.ToString()))
        {
            var opts = new List<FloatMenuOption>
            {
                new("System", () => { e.Role = PromptRole.System; e.CustomRole = ""; }),
                new("User", () => e.Role = PromptRole.User),
                new("Assistant", () => { e.Role = PromptRole.Assistant; e.CustomRole = ""; })
            };
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        if (isUserRole)
        {
            float customInputX = col2InputX + roleDropdownW + 10f;
            float maxCustomW = (switchBtnX - 10f) - customInputX;
            float customInputW = Mathf.Clamp(maxCustomW, 110f, 150f);
            Rect customRoleRect = new Rect(customInputX, y, customInputW, 24f);

            if (isHistoryMarker)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(customRoleRect, e.CustomRole ?? "");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                string customRole = Widgets.TextField(customRoleRect, e.CustomRole ?? "");
                if (customRole != e.CustomRole) e.CustomRole = customRole;

                if (string.IsNullOrEmpty(customRole))
                {
                    GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Rect hintRect = new Rect(customRoleRect.x + 5f, customRoleRect.y, customRoleRect.width - 10f, customRoleRect.height);
                    Widgets.Label(hintRect, "RimTalk.Settings.PromptPreset.CustomRole".Translate().ToString().TrimEnd(':', '：', ' '));
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                TooltipHandler.TipRegion(customRoleRect, "RimTalk.Settings.PromptPreset.CustomRole".Translate());
            }
        }

        y += 32f;

        // -- MAIN AREA (Layout Split Logic) --
        float mainAreaY = y;
        Rect bottomArea = new Rect(rect.x + 10f, mainAreaY, rect.xMax - (rect.x + 10f), rect.yMax - mainAreaY - 5f);
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
        float editorInnerWidth = editorRect.width - scrollbarMargin;
        float editorContentHeight = Mathf.Ceil(Mathf.Max(editorRect.height, Text.CalcHeight(e.Content, editorInnerWidth) + 25f));
        Rect editorViewRect = new Rect(0f, 0f, editorInnerWidth, editorContentHeight);

        const string editorControlName = "PromptEntryEditor";
        Widgets.BeginScrollView(editorRect, ref _promptContentScrollPos, editorViewRect);
        GUI.SetNextControlName(editorControlName);
        
        string newContent = Widgets.TextArea(new Rect(0f, 0f, editorInnerWidth, editorContentHeight), e.Content);
        
        // Auto-scroll logic: only scroll if the cursor position changed
        if (GUI.GetNameOfFocusedControl() == editorControlName)
        {
            TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (te != null && te.cursorIndex != _lastPromptEditorCursorPos)
            {
                _lastPromptEditorCursorPos = te.cursorIndex;
                float cursorY = GetCursorLineTop(te, editorInnerWidth);
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
}
