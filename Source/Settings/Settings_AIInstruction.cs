using System;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalk.UI;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
    private static readonly Color SoftGreen = new(0.6f, 0.9f, 0.6f);
    private static readonly Color SoftYellow = new(1f, 0.85f, 0.5f);

    private void DrawAIInstructionSettings(Listing_Standard listingStandard, bool showAdvancedSwitch = false, Rect containerRect = default)
    {
        RimTalkSettings settings = Get();

        bool isSimpleMode = !settings.UseAdvancedPromptMode;

        PromptEntry baseEntry = null;
        string currentContent;

        if (isSimpleMode)
        {
            currentContent = settings.SimpleModeInstruction;
            if (string.IsNullOrWhiteSpace(currentContent))
            {
                currentContent = Constant.DefaultInstruction;
                settings.SimpleModeInstruction = currentContent;
            }
        }
        else
        {
            var manager = PromptManager.Instance;
            manager.EnsureInitialized();
            var activePreset = manager.GetActivePreset();
            baseEntry = GetOrCreateBaseInstructionEntry(activePreset);
            currentContent = baseEntry?.Content ?? Constant.DefaultInstruction;

            if (_aiInstructionPresetId != (activePreset?.Id ?? ""))
            {
                _textAreaInitialized = false;
                _aiInstructionPresetId = activePreset?.Id ?? "";
            }
        }

        if (!_textAreaInitialized)
        {
            _textAreaBuffer = currentContent;
            _textAreaInitialized = true;
        }

        var aiInstructionPrompt = "RimTalk.Settings.AIInstructionPrompt".Translate();
        float textHeight = Text.CalcHeight(aiInstructionPrompt, listingStandard.ColumnWidth);
        Rect headerRect = listingStandard.GetRect(textHeight);
        Widgets.Label(headerRect, aiInstructionPrompt);

        listingStandard.Gap(6f);

        // Context information tip
        Text.Font = GameFont.Tiny;
        GUI.color = SoftGreen;
        Rect contextTipRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(contextTipRect, "RimTalk.Settings.AutoIncludedTip".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(6f);

        // Warning about rate limits & switch to advanced settings directly above the text box
        const float buttonWidth = 190f;
        const float textBorderMargin = 16f;
        float warningRowHeight = showAdvancedSwitch ? 26f : Text.LineHeight;
        Rect warningRowRect = listingStandard.GetRect(warningRowHeight);

        if (showAdvancedSwitch)
        {
            Rect buttonRect = new Rect(warningRowRect.xMax - textBorderMargin - buttonWidth, warningRowRect.y, buttonWidth, 26f);

            if (UIUtil.ButtonText(buttonRect, "RimTalk.Settings.SwitchToAdvancedSettings".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "RimTalk.Settings.AdvancedModeWarning".Translate(),
                    () =>
                    {
                        settings.UseAdvancedPromptMode = true;
                        _textAreaInitialized = false;
                        _aiInstructionPresetId = "";
                    }));
            }

            Rect warningLabelRect = new Rect(warningRowRect.x, warningRowRect.y, buttonRect.x - warningRowRect.x - 10f, warningRowHeight);
            Text.Font = GameFont.Tiny;
            GUI.color = SoftYellow;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(warningLabelRect, "RimTalk.Settings.RateLimitWarning".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
        else
        {
            Text.Font = GameFont.Tiny;
            GUI.color = SoftYellow;
            Widgets.Label(warningRowRect, "RimTalk.Settings.RateLimitWarning".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        listingStandard.Gap(6f);

        const float countHeight = 20f;
        float remainingHeight = containerRect.height > 0f
            ? containerRect.height - listingStandard.CurHeight - countHeight - 15f
            : 350f;
        float textAreaHeight = Mathf.Max(200f, remainingHeight);
        Rect textAreaRect = listingStandard.GetRect(textAreaHeight);

        float innerWidth = textAreaRect.width - 16f;
        float contentHeight = Mathf.Max(textAreaHeight, Text.CalcHeight(_textAreaBuffer, innerWidth) + 40f);
        Rect viewRect = new Rect(0f, 0f, innerWidth, contentHeight);

        const string controlName = "RimTalk_AIInstruction_TextArea";
        Widgets.BeginScrollView(textAreaRect, ref _aiInstructionScrollPos, viewRect);
        GUI.SetNextControlName(controlName);
        string newInstruction = Widgets.TextArea(new Rect(0f, 0f, innerWidth, contentHeight), _textAreaBuffer);

        // Auto-scroll logic: only scroll if the cursor position changed
        if (GUI.GetNameOfFocusedControl() == controlName)
        {
            TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (te != null && te.cursorIndex != _lastTextAreaCursorPos)
            {
                _lastTextAreaCursorPos = te.cursorIndex;
                float cursorY = te.graphicalCursorPos.y;
                if (cursorY < _aiInstructionScrollPos.y)
                    _aiInstructionScrollPos.y = cursorY;
                else if (cursorY + 25f > _aiInstructionScrollPos.y + textAreaHeight)
                    _aiInstructionScrollPos.y = cursorY + 25f - textAreaHeight;
            }
        }

        Widgets.EndScrollView();

        // Token count display
        listingStandard.Gap(2f);
        Rect countRect = listingStandard.GetRect(18f);
        countRect.width = innerWidth;
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Text.Anchor = TextAnchor.MiddleRight;
        int currentTokens = CommonUtil.EstimateTokenCount(_textAreaBuffer);
        Widgets.Label(countRect, "RimTalk.Settings.TokenInfo".Translate(currentTokens));
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        if (newInstruction != _textAreaBuffer)
        {
            _textAreaBuffer = newInstruction;

            // Write back to correct target (isolated per mode)
            if (isSimpleMode)
            {
                settings.SimpleModeInstruction = newInstruction;
            }
            else if (baseEntry != null)
            {
                baseEntry.Content = newInstruction;
            }
        }
    }

    private static PromptEntry GetOrCreateBaseInstructionEntry(PromptPreset preset)
    {
        if (preset == null) return null;

        var entry = preset.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "Base Instruction", StringComparison.OrdinalIgnoreCase));
        if (entry != null) return entry;

        entry = preset.Entries.FirstOrDefault(e =>
            e.Role == PromptRole.System && e.Position == PromptPosition.Relative);
        if (entry != null) return entry;

        entry = new PromptEntry
        {
            Name = "Base Instruction",
            Role = PromptRole.System,
            Position = PromptPosition.Relative,
            Content = Constant.DefaultInstruction
        };
        preset.Entries.Insert(0, entry);
        return entry;
    }
}
