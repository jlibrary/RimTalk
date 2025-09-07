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

            // Display talk when drafted checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.DisplayTalkWhenDrafted".Translate(),
                ref settings.displayTalkWhenDrafted,
                "RimTalk.Settings.DisplayTalkWhenDraftedTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow slaves to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowSlavesToTalk".Translate(),
                ref settings.allowSlavesToTalk,
                "RimTalk.Settings.AllowSlavesToTalkTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow prisoners to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowPrisonersToTalk".Translate(),
                ref settings.allowPrisonersToTalk,
                "RimTalk.Settings.AllowPrisonersToTalkTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow other factions to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowOtherFactionsToTalk".Translate(),
                ref settings.allowOtherFactionsToTalk,
                "RimTalk.Settings.AllowOtherFactionsToTalkTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow enemies to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowEnemiesToTalk".Translate(),
                ref settings.allowEnemiesToTalk,
                "RimTalk.Settings.AllowEnemiesToTalkTooltip".Translate()
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
                settings.processNonRimTalkInteractions = true;
                settings.displayTalkWhenDrafted = true;
                settings.allowSlavesToTalk = true;
                settings.allowPrisonersToTalk = true;
                settings.allowOtherFactionsToTalk = false;
                settings.allowEnemiesToTalk = false;
                settings.useSimpleConfig = true;
            }
            
            listingStandard.End();
        }
    }
}
