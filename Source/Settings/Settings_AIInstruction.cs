using RimTalk.Data;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
    private void DrawAIInstructionSettings(Listing_Standard listingStandard, bool showAdvancedSwitch = false)
    {
        RimTalkSettings settings = Get();

        if (!_textAreaInitialized)
        {
            _textAreaBuffer = string.IsNullOrWhiteSpace(settings.CustomInstruction)
                ? Constant.DefaultInstruction
                : settings.CustomInstruction;
            _textAreaInitialized = true;
        }

        var activeConfig = settings.GetActiveConfig();
        var modelName = activeConfig?.SelectedModel ?? "N/A";
        var aiInstructionPrompt = "RimTalk.Settings.AIInstructionPrompt".Translate(modelName);
        
        float textHeight = Text.CalcHeight(aiInstructionPrompt,
            listingStandard.ColumnWidth - (showAdvancedSwitch ? 180f : 0f));
        float headerHeight = Mathf.Max(textHeight, 30f);

        Rect headerRect = listingStandard.GetRect(headerHeight);

        if (showAdvancedSwitch)
        {
            float buttonWidth = 170f;
            Rect buttonRect = new Rect(headerRect.xMax - buttonWidth, headerRect.y, buttonWidth, 28f);

            if (Widgets.ButtonText(buttonRect, "RimTalk.Settings.SwitchToAdvancedSettings".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "RimTalk.Settings.AdvancedModeWarning".Translate(),
                    () => settings.UseAdvancedPromptMode = true));
            }

            Rect labelRect = new Rect(headerRect.x, headerRect.y, headerRect.width - buttonWidth - 10f,
                headerRect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, aiInstructionPrompt);
            Text.Anchor = TextAnchor.UpperLeft;
        }
        else
        {
            Widgets.Label(headerRect, aiInstructionPrompt);
        }
        
        listingStandard.Gap(6f);

        // Context information tip
        Text.Font = GameFont.Tiny;
        GUI.color = Color.green;
        Rect contextTipRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(contextTipRect, "RimTalk.Settings.AutoIncludedTip".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(6f);
        
        // Warning about rate limits
        Text.Font = GameFont.Tiny;
        GUI.color = Color.yellow;
        Rect rateLimitRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(rateLimitRect, "RimTalk.Settings.RateLimitWarning".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(6f);

        // Token info display
        int currentTokens = CommonUtil.EstimateTokenCount(_textAreaBuffer);
        int maxAllowedTokens = CommonUtil.GetMaxAllowedTokens(settings.TalkInterval);
        string tokenInfo = "RimTalk.Settings.TokenInfo".Translate(currentTokens, maxAllowedTokens);

        GUI.color = currentTokens > maxAllowedTokens ? Color.red : Color.green;

        Text.Font = GameFont.Tiny;
        Rect tokenInfoRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(tokenInfoRect, tokenInfo);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(6f);

        float textAreaHeight = 350f;
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

        if (newInstruction != _textAreaBuffer)
        {
            _textAreaBuffer = newInstruction;
            settings.CustomInstruction = newInstruction == Constant.DefaultInstruction ? "" : newInstruction;
        }

        listingStandard.Gap(6f);

        Rect resetButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            settings.CustomInstruction = "";
            _textAreaBuffer = Constant.DefaultInstruction;
        }

        listingStandard.Gap(10f);
    }
}