using System;
using System.Linq;
using RimTalk.Data;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawAIInstructionSettings(Listing_Standard listingStandard)
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
            int currentTokens = CommonUtil.EstimateTokenCount(textAreaBuffer);
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

            // Text area with minimum height to fill remaining space
            float remainingHeight = listingStandard.CurHeight - 50f; // Leave space for reset button
            float minHeight = 360f; // Minimum height in pixels

            // Check if text has extremely long words that could break CalcHeight
            bool hasLongWords = false;
            if (!string.IsNullOrEmpty(textAreaBuffer))
            {
                string[] words = textAreaBuffer.Split(new char[] { ' ', '\t', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries);
                hasLongWords = words.Any(word => word.Length > 100);
            }

            float calculatedHeight;
            if (hasLongWords)
            {
                // Use simple estimation for problematic text to avoid CalcHeight issues
                int lineCount = Mathf.Max(10, textAreaBuffer.Split('\n').Length + textAreaBuffer.Length / 80);
                calculatedHeight = lineCount * Text.LineHeight;
            }
            else
            {
                // Safe to use CalcHeight for normal text
                calculatedHeight = Text.CalcHeight(textAreaBuffer, listingStandard.ColumnWidth);
            }

            // Use the maximum of: minimum height, calculated height, or remaining space
            float textHeight = Mathf.Max(minHeight, calculatedHeight, remainingHeight);

            Rect textAreaRect = listingStandard.GetRect(textHeight);
            string newInstruction = Widgets.TextArea(textAreaRect, textAreaBuffer);

            // Update buffer and settings logic
            textAreaBuffer = newInstruction;
            if (newInstruction == Constant.DefaultInstruction)
            {
                settings.customInstruction = "";
            }
            else
            {
                settings.customInstruction = newInstruction;
            }

            listingStandard.Gap(6f);

            // Reset to default button
            Rect resetButtonRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
            {
                settings.customInstruction = "";
                textAreaBuffer = Constant.DefaultInstruction;
            }
        }
    }
}