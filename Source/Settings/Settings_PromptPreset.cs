using System.Collections.Generic;
using System.Linq;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
    // Prompt preset UI state (Advanced Mode)
    private Vector2 _presetListScrollPos = Vector2.zero;
    private Vector2 _entryListScrollPos = Vector2.zero;
    private string _selectedPresetId;
    private string _selectedEntryId;
    private int _rightPanelMode; // 0=Entry Editor, 1=Variable Preview

    private void DrawPromptPresetSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();
        
        // Check which mode to display
        if (settings.UseAdvancedPromptMode)
        {
            DrawAdvancedPromptMode(listingStandard, settings);
        }
        else
        {
            DrawSimplePromptMode(listingStandard, settings);
        }
    }

    // Simple Mode: Reuse DrawAIInstructionSettings with just a mode switch button
    private void DrawSimplePromptMode(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        // Title with mode switch button
        Rect titleRect = listingStandard.GetRect(30f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(titleRect.x, titleRect.y, titleRect.width - 180f, 30f), "RimTalk.Settings.PromptPresets".Translate());
        Text.Font = GameFont.Small;
        
        // Switch to Advanced button
        if (Widgets.ButtonText(new Rect(titleRect.xMax - 170f, titleRect.y, 170f, 28f), "RimTalk.Settings.SwitchToAdvanced".Translate()))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimTalk.Settings.AdvancedModeWarning".Translate(),
                () => {
                    settings.UseAdvancedPromptMode = true;
                }));
        }
        
        listingStandard.Gap(6f);
        
        // Reuse the existing AI instruction editor
        DrawAIInstructionSettings(listingStandard);
    }

    // Advanced Mode: Full preset/entry management interface
    private void DrawAdvancedPromptMode(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        var manager = PromptManager.Instance;
        
        // Title with mode switch button
        Rect titleRect = listingStandard.GetRect(30f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(titleRect.x, titleRect.y, titleRect.width - 180f, 30f), "RimTalk.Settings.PromptPresets".Translate());
        Text.Font = GameFont.Small;
        
        // Switch to Simple button
        if (Widgets.ButtonText(new Rect(titleRect.xMax - 170f, titleRect.y, 170f, 28f), "RimTalk.Settings.SwitchToSimple".Translate()))
        {
            settings.UseAdvancedPromptMode = false;
        }
        
        listingStandard.Gap(6f);

        // Main panel area
        Rect mainRect = listingStandard.GetRect(500f);
        
        // Left side: Preset and entry list
        Rect leftPanel = new Rect(mainRect.x, mainRect.y, 200f, mainRect.height);
        DrawPresetListPanel(leftPanel, manager);

        // Right side: Display different panels based on mode
        Rect rightPanel = new Rect(mainRect.x + 210f, mainRect.y, mainRect.width - 210f, mainRect.height);
        switch (_rightPanelMode)
        {
            case 1:
                DrawVariablePreviewPanel(rightPanel, manager);
                break;
            default:
                DrawEntryEditor(rightPanel, manager);
                break;
        }

        listingStandard.Gap(10f);

        // Bottom buttons - 4 buttons
        Rect buttonRect = listingStandard.GetRect(30f);
        float buttonWidth = (buttonRect.width - 30f) / 4f;
        
        // Mode switch buttons (Entries and Preview only)
        string[] modeLabels = {
            "RimTalk.Settings.PromptPreset.ModeEntries".Translate(),
            "RimTalk.Settings.PromptPreset.ModePreview".Translate()
        };
        for (int i = 0; i < 2; i++)
        {
            var btnRect = new Rect(buttonRect.x + (buttonWidth + 10f) * i, buttonRect.y, buttonWidth, 30f);
            GUI.color = _rightPanelMode == i ? Color.green : Color.white;
            if (Widgets.ButtonText(btnRect, modeLabels[i]))
            {
                _rightPanelMode = i;
            }
            GUI.color = Color.white;
        }
        
        // Help button
        if (Widgets.ButtonText(new Rect(buttonRect.x + (buttonWidth + 10f) * 2, buttonRect.y, buttonWidth, 30f),
            "RimTalk.Settings.PromptHelp".Translate()))
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                "RimTalk.Settings.AdvancedHelpContent".Translate(),
                "OK".Translate()));
        }
        
        if (Widgets.ButtonText(new Rect(buttonRect.x + (buttonWidth + 10f) * 3, buttonRect.y, buttonWidth, 30f),
            "RimTalk.Settings.ResetToDefault".Translate()))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimTalk.Settings.ResetConfirm".Translate(),
                () => {
                    manager.ResetToDefaults();
                    _selectedPresetId = null;
                    _selectedEntryId = null;
                }));
        }
    }

    private void DrawPresetListPanel(Rect rect, PromptManager manager)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.5f));
        Widgets.DrawBox(rect);

        float y = rect.y + 5f;
        
        // Preset title
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(rect.x + 5f, y, rect.width - 10f, 20f), "RimTalk.Settings.PromptPreset.Presets".Translate());
        y += 22f;
        Text.Font = GameFont.Small;

        // Preset list
        Rect presetListRect = new Rect(rect.x + 2f, y, rect.width - 4f, 100f);
        Rect presetViewRect = new Rect(0f, 0f, presetListRect.width - 16f, manager.Presets.Count * 25f);
        
        Widgets.BeginScrollView(presetListRect, ref _presetListScrollPos, presetViewRect);
        float presetY = 0f;
        foreach (var preset in manager.Presets)
        {
            Rect presetRow = new Rect(0f, presetY, presetViewRect.width, 24f);
            
            // Selection highlight
            if (_selectedPresetId == preset.Id)
            {
                Widgets.DrawHighlight(presetRow);
            }
            
            // Active indicator
            string prefix = preset.IsActive ? "▶ " : "  ";
            if (Widgets.ButtonText(presetRow, prefix + preset.Name, false))
            {
                _selectedPresetId = preset.Id;
                _selectedEntryId = null;
            }
            
            presetY += 25f;
        }
        Widgets.EndScrollView();

        y += 105f;

        // Preset action buttons
        if (Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.NewPreset".Translate()))
        {
            var newPreset = new PromptPreset("RimTalk.Settings.PromptPreset.NewPresetName".Translate());
            manager.AddPreset(newPreset);
            _selectedPresetId = newPreset.Id;
        }
        y += 26f;

        var selectedPreset = manager.Presets.FirstOrDefault(p => p.Id == _selectedPresetId);
        if (selectedPreset != null)
        {
            if (!selectedPreset.IsActive && Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.Activate".Translate()))
            {
                manager.SetActivePreset(selectedPreset.Id);
            }
            y += 26f;
            
            if (Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.Rename".Translate()))
            {
                Find.WindowStack.Add(new Dialog_RenamePreset(selectedPreset));
            }
            y += 26f;
            
            if (Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.Duplicate".Translate()))
            {
                var clone = manager.DuplicatePreset(selectedPreset.Id);
                if (clone != null) _selectedPresetId = clone.Id;
            }
            y += 26f;
            
            // Export button
            if (Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.Export".Translate()))
            {
                if (PresetSerializer.ExportToFile(selectedPreset))
                {
                    var exportDir = PresetSerializer.GetExportDirectory();
                    Messages.Message("RimTalk.Settings.PromptPreset.ExportSuccess".Translate(exportDir), MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message("RimTalk.Settings.PromptPreset.ExportFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }
            y += 26f;
            
            if (manager.Presets.Count > 1 && Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.Delete".Translate()))
            {
                manager.RemovePreset(selectedPreset.Id);
                _selectedPresetId = manager.Presets.FirstOrDefault()?.Id;
                _selectedEntryId = null;
            }
            y += 30f;
        }
        
        // Import button (always visible)
        if (Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.Import".Translate()))
        {
            ShowImportMenu(manager);
        }
        y += 26f;

        // Separator line
        Widgets.DrawLineHorizontal(rect.x + 5f, y, rect.width - 10f);
        y += 5f;

        // Entry title
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(rect.x + 5f, y, rect.width - 10f, 20f), "RimTalk.Settings.PromptPreset.Entries".Translate());
        y += 22f;
        Text.Font = GameFont.Small;

        // Entry list
        if (selectedPreset != null)
        {
            Rect entryListRect = new Rect(rect.x + 2f, y, rect.width - 4f, rect.height - y + rect.y - 70f);
            Rect entryViewRect = new Rect(0f, 0f, entryListRect.width - 16f, selectedPreset.Entries.Count * 25f);
            
            Widgets.BeginScrollView(entryListRect, ref _entryListScrollPos, entryViewRect);
            float entryY = 0f;
            foreach (var entry in selectedPreset.Entries)
            {
                Rect entryRow = new Rect(0f, entryY, entryViewRect.width, 24f);
                
                // Selection highlight
                if (_selectedEntryId == entry.Id)
                {
                    Widgets.DrawHighlight(entryRow);
                }
                
                // Enabled state
                bool enabled = entry.Enabled;
                Widgets.Checkbox(new Vector2(0f, entryY + 2f), ref enabled, 20f);
                entry.Enabled = enabled;
                
                // Entry name
                if (Widgets.ButtonText(new Rect(22f, entryY, entryViewRect.width - 22f, 24f), entry.Name, false))
                {
                    _selectedEntryId = entry.Id;
                }
                
                entryY += 25f;
            }
            Widgets.EndScrollView();

            y = rect.y + rect.height - 65f;
            
            // Entry action buttons
            if (Widgets.ButtonText(new Rect(rect.x + 5f, y, rect.width - 10f, 24f), "RimTalk.Settings.PromptPreset.NewEntry".Translate()))
            {
                var newEntry = new PromptEntry("RimTalk.Settings.PromptPreset.NewEntryName".Translate(), "", PromptRole.System);
                selectedPreset.AddEntry(newEntry);
                _selectedEntryId = newEntry.Id;
            }
            y += 26f;

            // Move and delete buttons
            if (_selectedEntryId != null)
            {
                float btnWidth = (rect.width - 20f) / 3f;
                if (Widgets.ButtonText(new Rect(rect.x + 5f, y, btnWidth, 24f), "↑"))
                {
                    selectedPreset.MoveEntry(_selectedEntryId, -1);
                }
                if (Widgets.ButtonText(new Rect(rect.x + 10f + btnWidth, y, btnWidth, 24f), "↓"))
                {
                    selectedPreset.MoveEntry(_selectedEntryId, 1);
                }
                if (Widgets.ButtonText(new Rect(rect.x + 15f + btnWidth * 2, y, btnWidth, 24f), "×"))
                {
                    selectedPreset.RemoveEntry(_selectedEntryId);
                    _selectedEntryId = null;
                }
            }
        }
    }

    private void DrawEntryEditor(Rect rect, PromptManager manager)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.3f));
        Widgets.DrawBox(rect);

        var selectedPreset = manager.Presets.FirstOrDefault(p => p.Id == _selectedPresetId);
        var selectedEntry = selectedPreset?.Entries.FirstOrDefault(e => e.Id == _selectedEntryId);

        if (selectedEntry == null)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "RimTalk.Settings.PromptPreset.SelectEntryToEdit".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        float y = rect.y + 10f;
        float labelWidth = 80f;
        float inputWidth = rect.width - labelWidth - 20f;

        // Name
        Widgets.Label(new Rect(rect.x + 10f, y, labelWidth, 24f), "RimTalk.Settings.PromptPreset.Name".Translate());
        selectedEntry.Name = Widgets.TextField(new Rect(rect.x + labelWidth + 10f, y, inputWidth, 24f), selectedEntry.Name);
        y += 30f;

        // Role
        Widgets.Label(new Rect(rect.x + 10f, y, labelWidth, 24f), "RimTalk.Settings.PromptPreset.Role".Translate());
        if (Widgets.ButtonText(new Rect(rect.x + labelWidth + 10f, y, 120f, 24f), selectedEntry.Role.ToString()))
        {
            var options = new List<FloatMenuOption>
            {
                new("System", () => selectedEntry.Role = PromptRole.System),
                new("User", () => selectedEntry.Role = PromptRole.User),
                new("Assistant", () => selectedEntry.Role = PromptRole.Assistant)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
        y += 30f;

        // Position
        Widgets.Label(new Rect(rect.x + 10f, y, labelWidth, 24f), "RimTalk.Settings.PromptPreset.Position".Translate());
        if (Widgets.ButtonText(new Rect(rect.x + labelWidth + 10f, y, 120f, 24f), selectedEntry.Position.ToString()))
        {
            var options = new List<FloatMenuOption>
            {
                new("RimTalk.Settings.PromptPreset.PositionRelative".Translate(), () => selectedEntry.Position = PromptPosition.Relative),
                new("RimTalk.Settings.PromptPreset.PositionInChat".Translate(), () => selectedEntry.Position = PromptPosition.InChat)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        // InChatDepth (only shown for InChat position)
        if (selectedEntry.Position == PromptPosition.InChat)
        {
            Widgets.Label(new Rect(rect.x + labelWidth + 140f, y, 50f, 24f), "RimTalk.Settings.PromptPreset.Depth".Translate());
            string depthStr = selectedEntry.InChatDepth.ToString();
            depthStr = Widgets.TextField(new Rect(rect.x + labelWidth + 195f, y, 60f, 24f), depthStr);
            if (int.TryParse(depthStr, out int depth))
            {
                selectedEntry.InChatDepth = depth;
            }
        }
        y += 30f;

        // Content label
        Widgets.Label(new Rect(rect.x + 10f, y, labelWidth, 24f), "RimTalk.Settings.PromptPreset.Content".Translate());
        
        // Insert variable button
        if (Widgets.ButtonText(new Rect(rect.x + labelWidth + 10f, y, 120f, 24f), "RimTalk.Settings.PromptPreset.InsertVariable".Translate()))
        {
            ShowVariableInsertMenu(selectedEntry);
        }
        y += 30f;

        // Content editing area
        Rect contentRect = new Rect(rect.x + 10f, y, rect.width - 20f, rect.height - y + rect.y - 10f);
        selectedEntry.Content = Widgets.TextArea(contentRect, selectedEntry.Content);
    }

    private void ShowVariableInsertMenu(PromptEntry entry)
    {
        var options = new List<FloatMenuOption>();
        
        // Get dynamic variable list from MustacheParser
        var builtinVars = MustacheParser.GetBuiltinVariables();
        foreach (var category in builtinVars)
        {
            options.Add(new FloatMenuOption($"--- {category.Key} ---", null));
            foreach (var (name, desc) in category.Value)
            {
                var varText = $"{{{{{name}}}}}";
                var displayText = string.IsNullOrEmpty(desc) ? varText : $"{varText} - {desc}";
                options.Add(new FloatMenuOption(displayText, () => InsertAtCursor(entry, varText)));
            }
        }
        
        // Mod registered custom variables
        var customVars = ContextHookRegistry.GetAllCustomVariables().ToList();
        if (customVars.Count > 0)
        {
            options.Add(new FloatMenuOption("--- " + "RimTalk.Settings.PromptPreset.ModVariables".Translate() + " ---", null));
            foreach (var (name, modId, desc, type) in customVars)
            {
                var displayText = string.IsNullOrEmpty(desc) ? $"{{{{{name}}}}}" : $"{{{{{name}}}}} - {desc}";
                options.Add(new FloatMenuOption(displayText,
                    () => InsertAtCursor(entry, $"{{{{{name}}}}}")));
            }
        }

        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void InsertAtCursor(PromptEntry entry, string text)
    {
        // Simply append to end (Unity's TextArea doesn't easily provide cursor position)
        entry.Content += text;
    }

    private Vector2 _previewScrollPos = Vector2.zero;

    private void DrawVariablePreviewPanel(Rect rect, PromptManager manager)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.3f));
        Widgets.DrawBox(rect);

        float y = rect.y + 10f;
        
        // Title
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(rect.x + 10f, y, rect.width - 20f, 30f), "RimTalk.Settings.PromptPreset.VariablePreview".Translate());
        Text.Font = GameFont.Small;
        y += 35f;

        // Description
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(rect.x + 10f, y, rect.width - 20f, 20f),
            "RimTalk.Settings.PromptPreset.VariablePreviewHint".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        y += 25f;

        // Variable list area
        Rect listRect = new Rect(rect.x + 5f, y, rect.width - 10f, rect.height - y + rect.y - 10f);
        
        // Calculate content height
        var builtinVars = MustacheParser.GetBuiltinVariables();
        int totalItems = builtinVars.Sum(c => c.Value.Count + 1); // +1 for category header
        totalItems += manager.VariableStore.Count + 1; // setvar vars
        
        Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, totalItems * 22f);
        
        Widgets.BeginScrollView(listRect, ref _previewScrollPos, viewRect);
        
        float varY = 0f;
        
        // Built-in variables
        foreach (var category in builtinVars)
        {
            // Category title
            GUI.color = Color.cyan;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, varY, viewRect.width, 20f), $"▼ {category.Key}");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            varY += 22f;
            
            foreach (var (name, desc) in category.Value)
            {
                DrawVariableRow(ref varY, viewRect.width, name, desc, null);
            }
        }
        
        // setvar stored variables (runtime variables set by AI)
        if (manager.VariableStore.Count > 0)
        {
            GUI.color = Color.green;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0f, varY, viewRect.width, 20f), "▼ " + "RimTalk.Settings.PromptPreset.RuntimeVariables".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            varY += 22f;
            
            foreach (var kvp in manager.VariableStore.GetAllVariables())
            {
                var displayValue = kvp.Value.Length > 50 ? kvp.Value.Substring(0, 47) + "..." : kvp.Value;
                DrawVariableRow(ref varY, viewRect.width, kvp.Key, "", displayValue);
            }
        }
        
        Widgets.EndScrollView();
    }

    private void DrawVariableRow(ref float y, float width, string name, string desc, string value)
    {
        float col1Width = 150f;
        float col2Width = width - col1Width - 10f;
        
        // Variable name
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.8f, 1f, 0.8f);
        Widgets.Label(new Rect(10f, y, col1Width, 20f), $"{{{{{name}}}}}");
        
        // Description or current value
        GUI.color = Color.gray;
        string displayText = value ?? desc;
        if (!string.IsNullOrEmpty(displayText))
        {
            displayText = displayText.Length > 60 ? displayText.Substring(0, 57) + "..." : displayText;
            Widgets.Label(new Rect(col1Width + 10f, y, col2Width, 20f), displayText);
        }
        
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        y += 20f;
    }

    /// <summary>
    /// Shows import menu with available preset files
    /// </summary>
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
            var fileName = System.IO.Path.GetFileNameWithoutExtension(file);
            options.Add(new FloatMenuOption(fileName, () =>
            {
                var preset = PresetSerializer.ImportFromFile(file);
                if (preset != null)
                {
                    manager.AddPreset(preset);
                    _selectedPresetId = preset.Id;
                    _selectedEntryId = null;
                    Messages.Message("RimTalk.Settings.PromptPreset.ImportSuccess".Translate(preset.Name), MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Messages.Message("RimTalk.Settings.PromptPreset.ImportFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }));
        }
        
        // Add option to open folder
        options.Add(new FloatMenuOption("RimTalk.Settings.PromptPreset.OpenFolder".Translate(), () =>
        {
            var exportDir = PresetSerializer.GetExportDirectory();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exportDir,
                UseShellExecute = true
            });
        }));
        
        Find.WindowStack.Add(new FloatMenu(options));
    }

    /// <summary>
    /// Simple dialog for renaming a preset
    /// </summary>
    private class Dialog_RenamePreset : Window
    {
        private readonly PromptPreset _preset;
        private string _newName;

        public override Vector2 InitialSize => new(400f, 150f);

        public Dialog_RenamePreset(PromptPreset preset)
        {
            _preset = preset;
            _newName = preset.Name;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "RimTalk.Settings.PromptPreset.Rename".Translate());
            Text.Font = GameFont.Small;

            _newName = Widgets.TextField(new Rect(0f, 40f, inRect.width, 30f), _newName);

            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 35f, 120f, 35f), "OK".Translate()))
            {
                if (!string.IsNullOrWhiteSpace(_newName))
                {
                    _preset.Name = _newName.Trim();
                }
                Close();
            }
        }
    }
}