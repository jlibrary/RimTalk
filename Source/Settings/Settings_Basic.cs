using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawBasicSettings(Listing_Standard listingStandard)
        {
            CurrentWorkDisplayModSettings settings = Get();

            // API Configuration section
            if (!settings.UseSimpleConfig)
            {
                DrawAdvancedApiSettings(listingStandard);
            }
            else
            {
                DrawSimpleApiSettings(listingStandard);
            }

            listingStandard.Gap(30f);
            
            listingStandard.Label("RimTalk.Settings.AICooldown".Translate(settings.TalkInterval));
            settings.TalkInterval = (int)listingStandard.Slider(settings.TalkInterval, 1, 20);

            listingStandard.Gap(12f);

            // Add the checkbox below cooldown slider
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.OverrideInteractions".Translate(),
                ref settings.ProcessNonRimTalkInteractions,
                "RimTalk.Settings.OverrideInteractionsTooltip".Translate()
            );
            listingStandard.Gap(12f);

            // Display talk when drafted checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.DisplayTalkWhenDrafted".Translate(),
                ref settings.DisplayTalkWhenDrafted,
                "RimTalk.Settings.DisplayTalkWhenDraftedTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow slaves to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowSlavesToTalk".Translate(),
                ref settings.AllowSlavesToTalk,
                "RimTalk.Settings.AllowSlavesToTalkTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow prisoners to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowPrisonersToTalk".Translate(),
                ref settings.AllowPrisonersToTalk,
                "RimTalk.Settings.AllowPrisonersToTalkTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow other factions to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowOtherFactionsToTalk".Translate(),
                ref settings.AllowOtherFactionsToTalk,
                "RimTalk.Settings.AllowOtherFactionsToTalkTooltip".Translate()
            );

            listingStandard.Gap(12f);

            // Allow enemies to talk checkbox
            listingStandard.CheckboxLabeled(
                "RimTalk.Settings.AllowEnemiesToTalk".Translate(),
                ref settings.AllowEnemiesToTalk,
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
                settings.TalkInterval = 7;
                settings.ProcessNonRimTalkInteractions = true;
                settings.DisplayTalkWhenDrafted = true;
                settings.AllowSlavesToTalk = true;
                settings.AllowPrisonersToTalk = true;
                settings.AllowOtherFactionsToTalk = false;
                settings.AllowEnemiesToTalk = false;
                settings.UseSimpleConfig = true;
            }
        }
    }
}
