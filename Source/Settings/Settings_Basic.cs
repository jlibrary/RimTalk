using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.UI;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
    private string GetFormattedSpeedLabel(TimeSpeed speed)
    {
        switch (speed)
        {
            case TimeSpeed.Normal:
                return "1x";
            case TimeSpeed.Fast:
                return "2x";
            case TimeSpeed.Superfast:
                return "3x";
            case TimeSpeed.Ultrafast:
                return "4x";
            default:
                return speed.ToString();
        }
    }

    private void DrawBasicSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

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

        // Define column layout
        const float columnGap = 200f;
        float columnWidth = (listingStandard.ColumnWidth - columnGap) / 2;
        const float intervalFieldWidth = 60f;

        // Get a rect for the entire two-column section.
        float estimatedHeight = 250f;
        Rect checkboxSectionRect = listingStandard.GetRect(estimatedHeight);

        // --- Left Column ---
        Rect leftColumnRect = new Rect(checkboxSectionRect.x, checkboxSectionRect.y, columnWidth,
            checkboxSectionRect.height);
        Listing_Standard leftListing = new Listing_Standard();
        leftListing.Begin(leftColumnRect);

        // 1. AI Cooldown
        Rect cooldownRect = leftListing.GetRect(24f);
        float cooldownLabelWidth = cooldownRect.width - intervalFieldWidth - 10f;
        Rect cooldownLabelRect = new Rect(cooldownRect.x, cooldownRect.y, cooldownLabelWidth, cooldownRect.height);
        Rect cooldownFieldRect = new Rect(cooldownLabelRect.xMax + 10f, cooldownRect.y, intervalFieldWidth, 24f);

        TextAnchor originalAnchor = Text.Anchor;
        TextAnchor middleLeft = TextAnchor.MiddleLeft;
        Text.Anchor = middleLeft;
        Widgets.Label(cooldownLabelRect, "RimTalk.Settings.AICooldown".Translate().ToString());

        Widgets.TextFieldNumeric(cooldownFieldRect, ref settings.TalkInterval, ref _talkIntervalBuffer, 1, 9999);
        TooltipHandler.TipRegion(cooldownRect, "RimTalk.Settings.AICooldownTooltip".Translate().ToString());

        leftListing.Gap(6f);

        // 2. Reply Interval
        Rect replyRect = leftListing.GetRect(24f);
        float replyLabelWidth = replyRect.width - intervalFieldWidth - 10f;
        Rect replyLabelRect = new Rect(replyRect.x, replyRect.y, replyLabelWidth, replyRect.height);
        Rect replyFieldRect = new Rect(replyLabelRect.xMax + 10f, replyRect.y, intervalFieldWidth, 24f);

        Widgets.Label(replyLabelRect, "RimTalk.Settings.ReplyInterval".Translate().ToString());
        Text.Anchor = originalAnchor;

        Widgets.TextFieldNumeric(replyFieldRect, ref settings.ReplyInterval, ref _replyIntervalBuffer, 0, 9999);
        TooltipHandler.TipRegion(replyRect, "RimTalk.Settings.ReplyIntervalTooltip".Translate().ToString());

        leftListing.Gap(6f);

        // 3. Checkboxes in Left Column
        Rect overrideRowRect = leftListing.GetRect(24f);
        if (settings.ProcessNonRimTalkInteractions)
        {
            const float btnWidth = 75f;
            Rect checkboxRect = new Rect(overrideRowRect.x, overrideRowRect.y, overrideRowRect.width - btnWidth - 6f, overrideRowRect.height);
            Rect btnRect = new Rect(checkboxRect.xMax + 6f, overrideRowRect.y, btnWidth, 24f);

            Widgets.CheckboxLabeled(checkboxRect, "RimTalk.Settings.OverrideInteractions".Translate().ToString(),
                ref settings.ProcessNonRimTalkInteractions);
            TooltipHandler.TipRegion(checkboxRect, "RimTalk.Settings.OverrideInteractionsTooltip".Translate().ToString());

            if (Widgets.ButtonText(btnRect, "RimTalk.Settings.SettingsButton".Translate().ToString()))
            {
                Find.WindowStack.Add(new Dialog_FastTrackInteractions());
            }
        }
        else
        {
            Widgets.CheckboxLabeled(overrideRowRect, "RimTalk.Settings.OverrideInteractions".Translate().ToString(),
                ref settings.ProcessNonRimTalkInteractions);
            TooltipHandler.TipRegion(overrideRowRect, "RimTalk.Settings.OverrideInteractionsTooltip".Translate().ToString());
        }

        leftListing.Gap(6f);
        leftListing.CheckboxLabeled("RimTalk.Settings.AllowSimultaneousConversations".Translate().ToString(),
            ref settings.AllowSimultaneousConversations,
            "RimTalk.Settings.AllowSimultaneousConversationsTooltip".Translate().ToString());
        leftListing.Gap(6f);
        leftListing.CheckboxLabeled("RimTalk.Settings.DisplayTalkWhenDrafted".Translate().ToString(),
            ref settings.DisplayTalkWhenDrafted,
            "RimTalk.Settings.DisplayTalkWhenDraftedTooltip".Translate().ToString());
        leftListing.Gap(6f);
        leftListing.CheckboxLabeled("RimTalk.Settings.ContinueDialogueWhileSleeping".Translate().ToString(),
            ref settings.ContinueDialogueWhileSleeping,
            "RimTalk.Settings.ContinueDialogueWhileSleepingTooltip".Translate().ToString());
        leftListing.Gap(6f);
        leftListing.CheckboxLabeled("RimTalk.Settings.ApplyMoodAndSocialEffects".Translate().ToString(),
            ref settings.ApplyMoodAndSocialEffects,
            "RimTalk.Settings.ApplyMoodAndSocialEffectsTooltip".Translate().ToString());
        leftListing.Gap(6f);
        
        leftListing.End();

        // --- Right Column ---
        Rect rightColumnRect = new Rect(leftColumnRect.xMax + columnGap, checkboxSectionRect.y, columnWidth,
            checkboxSectionRect.height);
        Listing_Standard rightListing = new Listing_Standard();
        rightListing.Begin(rightColumnRect);
        
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowMonologue".Translate().ToString(),
            ref settings.AllowMonologue, "RimTalk.Settings.AllowMonologueTooltip".Translate().ToString());
        rightListing.Gap(6f);
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowSlavesToTalk".Translate().ToString(),
            ref settings.AllowSlavesToTalk, "RimTalk.Settings.AllowSlavesToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowPrisonersToTalk".Translate().ToString(),
            ref settings.AllowPrisonersToTalk, "RimTalk.Settings.AllowPrisonersToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowOtherFactionsToTalk".Translate().ToString(),
            ref settings.AllowOtherFactionsToTalk,
            "RimTalk.Settings.AllowOtherFactionsToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowEnemiesToTalk".Translate().ToString(),
            ref settings.AllowEnemiesToTalk, "RimTalk.Settings.AllowEnemiesToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowBabiesToTalk".Translate().ToString(),
            ref settings.AllowBabiesToTalk, "RimTalk.Settings.AllowBabiesToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        rightListing.CheckboxLabeled("RimTalk.Settings.AllowNonHumanToTalk".Translate().ToString(),
            ref settings.AllowNonHumanToTalk, "RimTalk.Settings.AllowNonHumanToTalkTooltip".Translate().ToString());
        rightListing.End();

        // Advance the main listing standard's vertical position based on the taller of the two columns.
        float tallerColumnHeight = Mathf.Max(leftListing.CurHeight, rightListing.CurHeight);
        listingStandard.Gap(tallerColumnHeight - estimatedHeight); // Adjust for the initial GetRect height

        listingStandard.Gap();

        // --- Dropdown for PauseAtSpeed ---
        Rect pauseLineRect = listingStandard.GetRect(30f);
        const float dropdownWidth = 120f;

        Rect labelRect = new Rect(pauseLineRect.x, pauseLineRect.y, pauseLineRect.width - dropdownWidth - 10f,
            pauseLineRect.height);
        originalAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, "RimTalk.Settings.PauseAtSpeed".Translate().ToString());
        Text.Anchor = originalAnchor;

        Rect dropdownRect = new Rect(labelRect.xMax + 10f, pauseLineRect.y, dropdownWidth, pauseLineRect.height);

        // Use the helper function to determine the current label for the button
        string currentSpeedLabel = settings.DisableAiAtSpeed > (int)TimeSpeed.Normal
            ? GetFormattedSpeedLabel((TimeSpeed)settings.DisableAiAtSpeed)
            : "RimTalk.Settings.Disabled".Translate().ToString();

        if (Widgets.ButtonText(dropdownRect, currentSpeedLabel))
        {
            var options = new List<FloatMenuOption>
            {
                new("RimTalk.Settings.Disabled".Translate().ToString(),
                    () => settings.DisableAiAtSpeed = 0)
            };

            foreach (TimeSpeed speed in Enum.GetValues(typeof(TimeSpeed)))
            {
                // Only include speeds faster than Normal
                if ((int)speed > (int)TimeSpeed.Normal)
                {
                    // Use the helper function for the dropdown option text
                    string label = GetFormattedSpeedLabel(speed);
                    TimeSpeed currentSpeed = speed; // Capture the loop variable for the lambda
                    options.Add(new FloatMenuOption(label, () => settings.DisableAiAtSpeed = (int)currentSpeed));
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        TooltipHandler.TipRegion(pauseLineRect, "RimTalk.Settings.DisableAiAtSpeedTooltip".Translate().ToString());

        listingStandard.Gap();

        // --- Dropdown for Button Display Mode ---
        var buttonDisplayRect = listingStandard.GetRect(30f);
        var buttonDisplayLabelRect = new Rect(buttonDisplayRect.x, buttonDisplayRect.y,
            buttonDisplayRect.width - dropdownWidth - 10f, buttonDisplayRect.height);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(buttonDisplayLabelRect, "RimTalk.Settings.ButtonDisplay".Translate().ToString());
        Text.Anchor = originalAnchor;

        var buttonDisplayDropdownRect = new Rect(buttonDisplayLabelRect.xMax + 10f, buttonDisplayRect.y, dropdownWidth,
            buttonDisplayRect.height);

        if (Widgets.ButtonText(buttonDisplayDropdownRect, settings.ButtonDisplay.ToString()))
        {
            var options = new List<FloatMenuOption>();
            foreach (ButtonDisplayMode mode in Enum.GetValues(typeof(ButtonDisplayMode)))
            {
                var currentMode = mode;
                options.Add(new FloatMenuOption(currentMode.ToString(), () => settings.ButtonDisplay = currentMode));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        TooltipHandler.TipRegion(buttonDisplayRect, "RimTalk.Settings.ButtonDisplayTooltip".Translate().ToString());

        listingStandard.Gap(12f);
        VersionSwitcher.DrawVersionSwitcher(listingStandard);

        listingStandard.Gap(24f);
        
        if (listingStandard.ButtonText("RimTalk.Settings.ResetToDefault".Translate().ToString()))
        {
            settings.TalkInterval = 10;
            settings.ReplyInterval = 4;
            _talkIntervalBuffer = "10";
            _replyIntervalBuffer = "4";
            settings.ProcessNonRimTalkInteractions = true;
            settings.AllowSimultaneousConversations = false;
            settings.DisplayTalkWhenDrafted = true;
            settings.AllowMonologue = true;
            settings.AllowSlavesToTalk = true;
            settings.AllowPrisonersToTalk = true;
            settings.AllowOtherFactionsToTalk = false;
            settings.AllowEnemiesToTalk = false;
            settings.AllowBabiesToTalk = true;
            settings.AllowNonHumanToTalk = true;
            settings.AllowAnnouncement = true;
            settings.AllowCustomConversation = true;
            settings.PlayerDialogueMode = PlayerDialogueMode.Manual;
            settings.ContinueDialogueWhileSleeping = false;
            settings.EnableSleepDialogue = true;
            settings.ApplyMoodAndSocialEffects = false;
            settings.UseSimpleConfig = true;
            settings.DisableAiAtSpeed = 0;
            settings.ButtonDisplay = ButtonDisplayMode.Toggle;
        }
    }
}