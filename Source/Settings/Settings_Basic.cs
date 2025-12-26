using System;
using System.Collections.Generic;
using System.Linq;
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
    
    private string GetPlayerDialogueModeLabel(PlayerDialogueMode mode)
    {
        switch (mode)
        {
            case PlayerDialogueMode.Disabled:
                return "RimTalk.Settings.Disabled".Translate().ToString();
            case PlayerDialogueMode.Manual:
                return "RimTalk.Settings.PlayerDialogueMode.Manual".Translate().ToString();
            case PlayerDialogueMode.AIDriven:
                return "RimTalk.Settings.PlayerDialogueMode.AIDriven".Translate().ToString();
            default:
                return mode.ToString();
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

        listingStandard.Gap(6f);

        // --- Checkboxes in two columns ---

        // Define column layout
        const float columnGap = 200f;
        float columnWidth = (listingStandard.ColumnWidth - columnGap) / 2;

        // Get a rect for the entire checkbox section. We'll manually manage the layout within this.
        // The height is an estimate; we will adjust the main listing's Y position later.
        float estimatedHeight = settings.AllowCustomConversation ? 340f : 240f;
        Rect checkboxSectionRect = listingStandard.GetRect(estimatedHeight);

        // --- Left Column ---
        Rect leftColumnRect = new Rect(checkboxSectionRect.x, checkboxSectionRect.y, columnWidth,
            checkboxSectionRect.height);
        Listing_Standard leftListing = new Listing_Standard();
        leftListing.Begin(leftColumnRect);

        leftListing.CheckboxLabeled("RimTalk.Settings.OverrideInteractions".Translate().ToString(),
            ref settings.ProcessNonRimTalkInteractions,
            "RimTalk.Settings.OverrideInteractionsTooltip".Translate().ToString());
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
        
        // AllowCustomConversation
        leftListing.CheckboxLabeled("RimTalk.Settings.AllowCustomConversation".Translate().ToString(),
            ref settings.AllowCustomConversation,
            "RimTalk.Settings.AllowCustomConversationTooltip".Translate().ToString());

        // Draw custom conversation options if enabled
        if (settings.AllowCustomConversation)
        {
            leftListing.Gap(6f);
            DrawCustomConversationOptions(leftListing, settings);
        }

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
        TextAnchor originalAnchor = Text.Anchor;
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

        listingStandard.Gap(24f);
        
        if (listingStandard.ButtonText("RimTalk.Settings.ResetToDefault".Translate().ToString()))
        {
            settings.TalkInterval = 7;
            settings.ProcessNonRimTalkInteractions = true;
            settings.AllowSimultaneousConversations = true;
            settings.DisplayTalkWhenDrafted = true;
            settings.AllowMonologue = true;
            settings.AllowSlavesToTalk = true;
            settings.AllowPrisonersToTalk = true;
            settings.AllowOtherFactionsToTalk = false;
            settings.AllowEnemiesToTalk = false;
            settings.AllowBabiesToTalk = true;
            settings.AllowNonHumanToTalk = true;
            settings.AllowCustomConversation = true;
            settings.PlayerDialogueMode = PlayerDialogueMode.Manual;
            settings.PlayerName = "Player";
            settings.ContinueDialogueWhileSleeping = false;
            settings.ApplyMoodAndSocialEffects = false;
            settings.UseSimpleConfig = true;
            settings.DisableAiAtSpeed = 0;
            settings.ButtonDisplay = ButtonDisplayMode.Toggle;
        }
    }
    
    private void DrawCustomConversationOptions(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        const float indent = 30f;
        const float dropdownWidth = 120f;
        const float textFieldWidth = 120f;
        
        // 1. Player Dialogue Dropdown
        Rect playerDialogueRect = listingStandard.GetRect(24f);
        playerDialogueRect.x += indent;
        playerDialogueRect.width -= indent;
        
        float labelWidth = playerDialogueRect.width - dropdownWidth - 10f;
        Rect playerToNpcRect = new Rect(playerDialogueRect.x, playerDialogueRect.y, labelWidth, playerDialogueRect.height);
        Rect playerDialogueDropdownRect = new Rect(playerToNpcRect.xMax + 10f, playerDialogueRect.y, dropdownWidth, playerDialogueRect.height);
        
        TextAnchor savedAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(playerToNpcRect, "RimTalk.Settings.PlayerToNpc".Translate().ToString());
        Text.Anchor = savedAnchor;
        
        string currentModeLabel = GetPlayerDialogueModeLabel(settings.PlayerDialogueMode);
        
        if (Widgets.ButtonText(playerDialogueDropdownRect, currentModeLabel))
        {
            var options = (from PlayerDialogueMode currentMode in Enum.GetValues(typeof(PlayerDialogueMode)) 
                select new FloatMenuOption(GetPlayerDialogueModeLabel(currentMode), () => settings.PlayerDialogueMode = currentMode)).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
        
        TooltipHandler.TipRegion(playerDialogueRect, "RimTalk.Settings.PlayerDialogueModeTooltip".Translate().ToString());
        
        // 2. Player Name TextField
        bool isPlayerDialogueEnabled = settings.PlayerDialogueMode != PlayerDialogueMode.Disabled;
        
        Rect playerNameRect = listingStandard.GetRect(30f);
        playerNameRect.x += indent;
        playerNameRect.width -= indent;
        
        float nameFieldWidth = textFieldWidth;
        float nameLabelWidth = playerNameRect.width - nameFieldWidth - 10f;
        Rect playerNameLabelRect = new Rect(playerNameRect.x, playerNameRect.y, nameLabelWidth, playerNameRect.height);
        Rect playerNameFieldRect = new Rect(playerNameLabelRect.xMax + 10f, playerNameRect.y + 3f, nameFieldWidth, 24f);
        
        Color savedColor = GUI.color;
        if (!isPlayerDialogueEnabled)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
        }
        
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(playerNameLabelRect, "RimTalk.Settings.PlayerName".Translate().ToString());
        Text.Anchor = savedAnchor;
        
        if (isPlayerDialogueEnabled)
        {
            settings.PlayerName = Widgets.TextField(playerNameFieldRect, settings.PlayerName);
        }
        else
        {
            GUI.enabled = false;
            Widgets.TextField(playerNameFieldRect, settings.PlayerName);
            GUI.enabled = true;
        }
        
        GUI.color = savedColor;
        
        TooltipHandler.TipRegion(playerNameRect, "RimTalk.Settings.PlayerNameTooltip".Translate().ToString());
    }
}