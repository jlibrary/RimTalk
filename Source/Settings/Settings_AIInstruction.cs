using RimTalk.Data;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawAIInstructionSettings(Rect rect)
        {
            CurrentWorkDisplayModSettings settings = Get();

            // Initialize buffer if needed
            if (!textAreaInitialized)
            {
                textAreaBuffer = string.IsNullOrWhiteSpace(settings.customInstruction)
                    ? Constant.DefaultInstruction
                    : settings.customInstruction;
                lastSavedInstruction = settings.customInstruction;
                textAreaInitialized = true;
            }

            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(rect);

            var activeConfig = settings.GetActiveConfig();
            var modelName = activeConfig?.SelectedModel ?? "N/A";
            listingStandard.Label("RimTalk.Settings.AIInstructionPrompt".Translate(modelName));
            listingStandard.Gap(6f);

            // Instructions for external editor
            Text.Font = GameFont.Tiny;
            GUI.color = Color.cyan;
            Rect externalEditorRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(externalEditorRect, "RimTalk.Settings.ExternalEditorTip".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
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
            int currentTokens = CommonUtil.EstimateTokenCount(Constant.Instruction);
            int maxAllowedTokens = CommonUtil.GetMaxAllowedTokens(settings.talkInterval);
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

            // Calculate available height for text area (fill remaining space minus button)
            float buttonHeight = 30f;
            float buttonGap = 6f;
            float usedHeight = listingStandard.CurHeight;
            float availableHeight = rect.height - usedHeight - buttonHeight - buttonGap;

            // Ensure minimum height
            availableHeight = Mathf.Max(availableHeight, 100f);

            Rect textAreaRect = listingStandard.GetRect(availableHeight);
            string newInstruction = GUI.TextArea(textAreaRect, textAreaBuffer);

            // Update buffer and settings logic
            textAreaBuffer = newInstruction;
            string newInstructionWithJson = newInstruction +
                                            "\n\nReturn JSON array with objects containing \"name\" and \"text\" string keys.";
            int newTokenCount = CommonUtil.EstimateTokenCount(newInstructionWithJson);
            if (newTokenCount <= maxAllowedTokens)
            {
                if (newInstruction == Constant.DefaultInstruction)
                {
                    settings.customInstruction = "";
                }
                else
                {
                    settings.customInstruction = newInstruction;
                }
            }

            listingStandard.Gap(buttonGap);

            // Reset to default button
            Rect resetButtonRect = listingStandard.GetRect(buttonHeight);
            if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
            {
                settings.customInstruction = "";
                textAreaBuffer = Constant.DefaultInstruction;
            }

            listingStandard.End();
        }
    }
}