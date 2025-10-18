using UnityEngine;
using Verse;
using System.Collections.Generic;

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

        // AI Cooldown
        var cooldownLabel = "RimTalk.Settings.AICooldown".Translate(settings.TalkInterval).ToString();
        var cooldownLabelRect = listingStandard.GetRect(Text.CalcHeight(cooldownLabel, listingStandard.ColumnWidth));
        Widgets.Label(cooldownLabelRect, cooldownLabel);
        settings.TalkInterval = (int)listingStandard.Slider(settings.TalkInterval, 1, 60);

        listingStandard.Gap(12f);

        // --- Checkboxes ---
        listingStandard.CheckboxLabeled("RimTalk.Settings.OverrideInteractions".Translate().ToString(), ref settings.ProcessNonRimTalkInteractions, "RimTalk.Settings.OverrideInteractionsTooltip".Translate().ToString());
        listingStandard.Gap(12f);
        listingStandard.CheckboxLabeled("RimTalk.Settings.DisplayTalkWhenDrafted".Translate().ToString(), ref settings.DisplayTalkWhenDrafted, "RimTalk.Settings.DisplayTalkWhenDraftedTooltip".Translate().ToString());
        listingStandard.Gap(12f);
        listingStandard.CheckboxLabeled("RimTalk.Settings.AllowSlavesToTalk".Translate().ToString(), ref settings.AllowSlavesToTalk, "RimTalk.Settings.AllowSlavesToTalkTooltip".Translate().ToString());
        listingStandard.Gap(12f);
        listingStandard.CheckboxLabeled("RimTalk.Settings.AllowPrisonersToTalk".Translate().ToString(), ref settings.AllowPrisonersToTalk, "RimTalk.Settings.AllowPrisonersToTalkTooltip".Translate().ToString());
        listingStandard.Gap(12f);
        listingStandard.CheckboxLabeled("RimTalk.Settings.AllowOtherFactionsToTalk".Translate().ToString(), ref settings.AllowOtherFactionsToTalk, "RimTalk.Settings.AllowOtherFactionsToTalkTooltip".Translate().ToString());
        listingStandard.Gap(12f);
        listingStandard.CheckboxLabeled("RimTalk.Settings.AllowEnemiesToTalk".Translate().ToString(), ref settings.AllowEnemiesToTalk, "RimTalk.Settings.AllowEnemiesToTalkTooltip".Translate().ToString());
        listingStandard.Gap(12f);
        listingStandard.CheckboxLabeled("RimTalk.Settings.AllowCustomConversation".Translate().ToString(), ref settings.AllowCustomConversation, "RimTalk.Settings.AllowCustomConversationTooltip".Translate().ToString());
        listingStandard.Gap(12f);

            
        // --- Dropdown for PauseAtSpeed ---
        Rect pauseLineRect = listingStandard.GetRect(30f);
        const float dropdownWidth = 120f;
            
        Rect labelRect = new Rect(pauseLineRect.x, pauseLineRect.y, pauseLineRect.width - dropdownWidth - 10f, pauseLineRect.height);
        TextAnchor originalAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, "RimTalk.Settings.PauseAtSpeed".Translate().ToString());
        Text.Anchor = originalAnchor;

        Rect dropdownRect = new Rect(labelRect.xMax + 10f, pauseLineRect.y, dropdownWidth, pauseLineRect.height);

        // Use the helper function to determine the current label for the button
        string currentSpeedLabel = settings.DisableAiAtSpeed > (int)TimeSpeed.Normal ? 
            GetFormattedSpeedLabel((TimeSpeed)settings.DisableAiAtSpeed) : 
            "RimTalk.Settings.Disabled".Translate().ToString();

        if (Widgets.ButtonText(dropdownRect, currentSpeedLabel))
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("RimTalk.Settings.Disabled".Translate().ToString(), () => settings.DisableAiAtSpeed = 0));
                
            foreach (TimeSpeed speed in System.Enum.GetValues(typeof(TimeSpeed)))
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

        listingStandard.Gap(12f);

        // --- Dropdown for Button Display Mode ---
        var buttonDisplayRect = listingStandard.GetRect(30f);
        var buttonDisplayLabelRect = new Rect(buttonDisplayRect.x, buttonDisplayRect.y, buttonDisplayRect.width - dropdownWidth - 10f, buttonDisplayRect.height);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(buttonDisplayLabelRect, "RimTalk.Settings.ButtonDisplay".Translate().ToString());
        Text.Anchor = originalAnchor;

        var buttonDisplayDropdownRect = new Rect(buttonDisplayLabelRect.xMax + 10f, buttonDisplayRect.y, dropdownWidth, buttonDisplayRect.height);

        if (Widgets.ButtonText(buttonDisplayDropdownRect, settings.ButtonDisplay.ToString()))
        {
            var options = new List<FloatMenuOption>();
            foreach (ButtonDisplayMode mode in System.Enum.GetValues(typeof(ButtonDisplayMode)))
            {
                var currentMode = mode;
                options.Add(new FloatMenuOption(currentMode.ToString(), () => settings.ButtonDisplay = currentMode));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(buttonDisplayRect, "RimTalk.Settings.ButtonDisplayTooltip".Translate().ToString());


        listingStandard.Gap(24f);

        // --- Footer ---
        GUI.color = Color.yellow;
        var bubbleFadeTip = "RimTalk.Settings.BubbleFadeTip".Translate().ToString();
        var bubbleFadeTipRect = listingStandard.GetRect(Text.CalcHeight(bubbleFadeTip, listingStandard.ColumnWidth));
        Widgets.Label(bubbleFadeTipRect, bubbleFadeTip);
        GUI.color = Color.white;
        listingStandard.Gap(12f);

        if (listingStandard.ButtonText("RimTalk.Settings.ResetToDefault".Translate().ToString()))
        {
            settings.TalkInterval = 7;
            settings.ProcessNonRimTalkInteractions = true;
            settings.DisplayTalkWhenDrafted = true;
            settings.AllowSlavesToTalk = true;
            settings.AllowPrisonersToTalk = true;
            settings.AllowOtherFactionsToTalk = false;
            settings.AllowEnemiesToTalk = false;
            settings.AllowCustomConversation = true;
            settings.UseSimpleConfig = true;
            settings.DisableAiAtSpeed = 0;
            settings.ButtonDisplay = ButtonDisplayMode.Tab;
        }
    }
}