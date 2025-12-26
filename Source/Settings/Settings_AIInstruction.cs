using RimTalk.Data;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
    private void DrawAIInstructionSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // Initialize buffer if needed
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
        var aiInstructionPromptRect = listingStandard.GetRect(Text.CalcHeight(aiInstructionPrompt, listingStandard.ColumnWidth));
        Widgets.Label(aiInstructionPromptRect, aiInstructionPrompt);
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

        if (currentTokens > maxAllowedTokens)
        {
            GUI.color = Color.red;
            tokenInfo += "RimTalk.Settings.OverLimit".Translate();
        }
        else
        {
            GUI.color = Color.green;
        }

        Text.Font = GameFont.Tiny;
        Rect tokenInfoRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(tokenInfoRect, tokenInfo);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(6f);

        // Use a fixed height for the text area
        float textAreaHeight = 350f;
        Rect textAreaRect = listingStandard.GetRect(textAreaHeight);
            
        // Draw the text area - Unity's TextArea handles its own scrolling internally
        string newInstruction = Widgets.TextArea(textAreaRect, _textAreaBuffer);

        // Update buffer and settings logic
        if (newInstruction != _textAreaBuffer)
        {
            _textAreaBuffer = newInstruction;
            settings.CustomInstruction = newInstruction == Constant.DefaultInstruction ? "" : newInstruction;
        }

        listingStandard.Gap(6f);

        // Reset to default button
        Rect resetButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            settings.CustomInstruction = "";
            _textAreaBuffer = Constant.DefaultInstruction;
        }

        // Add some extra space at the bottom to ensure everything is visible
        listingStandard.Gap(10f);
    }
}