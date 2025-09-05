using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawBasicSettings(Rect rect)
        {
            CurrentWorkDisplayModSettings settings = Get();
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(rect);

            // API Configuration section
            if (!settings.useSimpleConfig)
            {
                DrawAdvancedApiSettings(listingStandard);
            }
            else
            {
                DrawSimpleApiSettings(listingStandard);
            }

            listingStandard.Gap(30f);
            
            listingStandard.Label("RimTalk.Settings.AICooldown".Translate(settings.talkInterval));
            settings.talkInterval = (int)listingStandard.Slider(settings.talkInterval, 1, 20);

            listingStandard.Gap(12f);

            // Add the checkbox below cooldown slider
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.OverrideInteractions".Translate(),
                ref settings.processNonRimTalkInteractions,
                "RimTalk.Settings.OverrideInteractionsTooltip".Translate()
            );
            listingStandard.Gap(12f);

            // Suppress unprocessed messages checkbox - only enabled if first checkbox is checked
            bool oldEnabled = GUI.enabled;
            GUI.enabled = settings.processNonRimTalkInteractions;

            // Automatically uncheck second checkbox when first is disabled
            if (!settings.processNonRimTalkInteractions)
                settings.suppressUnprocessedMessages = false;

            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.HideSkippedMessages".Translate(),
                ref settings.suppressUnprocessedMessages,
                "RimTalk.Settings.HideSkippedMessagesTooltip".Translate()
            );
            
            listingStandard.Gap(12f);

            // Display talk when drafted checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.DisplayTalkWhenDrafted".Translate(),
                ref settings.displayTalkWhenDrafted,
                "RimTalk.Settings.DisplayTalkWhenDraftedTooltip".Translate()
            );

            listingStandard.Gap(12f);
            
            // Bubble fade tip
            GUI.color = Color.yellow;
            Rect rateLimitRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(rateLimitRect, "RimTalk.Settings.BubbleFadeTip".Translate());
            GUI.color = Color.white;
            listingStandard.Gap(12f);

            Rect resetButtonRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
            {
                // Reset all basic settings to their default values
                settings.talkInterval = 7;
                settings.processNonRimTalkInteractions = false;
                settings.suppressUnprocessedMessages = false;
                settings.displayTalkWhenDrafted = true;
                
                // Reset API configurations
                settings.cloudConfigs.Clear();
                settings.cloudConfigs.Add(new ApiConfig());
                settings.localConfig = new ApiConfig { Provider = AIProvider.Local };
                settings.useCloudProviders = true;
            }
            
            GUI.enabled = oldEnabled; // Restore original GUI.enabled state

            listingStandard.End();
        }
    }
}
