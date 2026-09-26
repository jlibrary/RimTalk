using System;
using System.Collections.Generic;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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

    private static void CheckboxLeft(Listing_Standard listing, string label, ref bool checkOn, string tooltip = null, Action onGearClicked = null)
    {
        Rect rowRect = listing.GetRect(24f);
        const float checkSize = 24f;
        const float gap = 6f;
        const float gearSize = 22f;

        Widgets.DrawHighlightIfMouseover(rowRect);

        Vector2 checkPos = new Vector2(rowRect.x, rowRect.y);
        Widgets.CheckboxDraw(checkPos.x, checkPos.y, checkOn, false, checkSize);

        float labelX = rowRect.x + checkSize + gap;
        float labelWidth = rowRect.width - (checkSize + gap) - (onGearClicked != null ? (gearSize + gap) : 0f);
        Rect labelRect = new Rect(labelX, rowRect.y, labelWidth, 24f);

        TextAnchor oldAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, label);
        Text.Anchor = oldAnchor;

        Rect clickableArea = new Rect(rowRect.x, rowRect.y, rowRect.width - (onGearClicked != null ? (gearSize + gap) : 0f), 24f);
        if (Widgets.ButtonInvisible(clickableArea))
        {
            checkOn = !checkOn;
            if (checkOn)
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
        }

        if (onGearClicked != null)
        {
            Rect gearRect = new Rect(rowRect.xMax - gearSize, rowRect.y + 1f, gearSize, gearSize);
            var gearIcon = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
            if (Widgets.ButtonImage(gearRect, gearIcon, new Color(0.85f, 0.85f, 0.85f), GenUI.MouseoverColor))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                onGearClicked();
            }
            TooltipHandler.TipRegion(gearRect, "RimTalk.Settings.FastTrackInteractionsTitle".Translate());
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            TooltipHandler.TipRegion(clickableArea, tooltip);
        }
    }

    private void DrawBasicSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // 3-Card Mode Selector Header (Google | Player2 | Advanced)
        DrawApiModeSelector(listingStandard, settings);
        listingStandard.Gap(8f);

        // API Configuration section
        if (!settings.UseSimpleConfig)
        {
            DrawAdvancedApiSettings(listingStandard);
        }
        else
        {
            DrawSimpleApiSettings(listingStandard);
        }

        listingStandard.Gap(42f);

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
        float cooldownFieldX = cooldownRect.xMax - intervalFieldWidth - 2f;
        Rect cooldownLabelRect = new Rect(cooldownRect.x, cooldownRect.y, cooldownFieldX - cooldownRect.x - 10f, cooldownRect.height);
        Rect cooldownFieldRect = new Rect(cooldownFieldX, cooldownRect.y, intervalFieldWidth, 24f);

        TextAnchor originalAnchor = Text.Anchor;
        TextAnchor middleLeft = TextAnchor.MiddleLeft;
        Text.Anchor = middleLeft;
        Widgets.Label(cooldownLabelRect, "RimTalk.Settings.AICooldown".Translate().ToString());

        Widgets.TextFieldNumeric(cooldownFieldRect, ref settings.TalkInterval, ref _talkIntervalBuffer, 1, 9999);
        TooltipHandler.TipRegion(cooldownRect, "RimTalk.Settings.AICooldownTooltip".Translate().ToString());

        leftListing.Gap(6f);

        // 2. Reply Interval
        Rect replyRect = leftListing.GetRect(24f);
        float replyFieldX = replyRect.xMax - intervalFieldWidth - 2f;
        Rect replyLabelRect = new Rect(replyRect.x, replyRect.y, replyFieldX - replyRect.x - 10f, replyRect.height);
        Rect replyFieldRect = new Rect(replyFieldX, replyRect.y, intervalFieldWidth, 24f);

        Widgets.Label(replyLabelRect, "RimTalk.Settings.ReplyInterval".Translate().ToString());
        Text.Anchor = originalAnchor;

        Widgets.TextFieldNumeric(replyFieldRect, ref settings.ReplyInterval, ref _replyIntervalBuffer, 0, 9999);
        TooltipHandler.TipRegion(replyRect, "RimTalk.Settings.ReplyIntervalTooltip".Translate().ToString());

        leftListing.Gap(6f);

        // 3. Checkboxes in Left Column (Left-aligned checkmarks)
        CheckboxLeft(leftListing, "RimTalk.Settings.OverrideInteractions".Translate().ToString(),
            ref settings.ProcessNonRimTalkInteractions,
            "RimTalk.Settings.OverrideInteractionsTooltip".Translate().ToString(),
            settings.ProcessNonRimTalkInteractions ? () => Find.WindowStack.Add(new Dialog_FastTrackInteractions()) : null);
        leftListing.Gap(6f);
        CheckboxLeft(leftListing, "RimTalk.Settings.AllowSimultaneousConversations".Translate().ToString(),
            ref settings.AllowSimultaneousConversations,
            "RimTalk.Settings.AllowSimultaneousConversationsTooltip".Translate().ToString());
        leftListing.Gap(6f);
        CheckboxLeft(leftListing, "RimTalk.Settings.DisplayTalkWhenDrafted".Translate().ToString(),
            ref settings.DisplayTalkWhenDrafted,
            "RimTalk.Settings.DisplayTalkWhenDraftedTooltip".Translate().ToString());
        leftListing.Gap(6f);
        CheckboxLeft(leftListing, "RimTalk.Settings.ContinueDialogueWhileSleeping".Translate().ToString(),
            ref settings.ContinueDialogueWhileSleeping,
            "RimTalk.Settings.ContinueDialogueWhileSleepingTooltip".Translate().ToString());
        leftListing.Gap(6f);
        CheckboxLeft(leftListing, "RimTalk.Settings.EnableSleepDialogue".Translate().ToString(),
            ref settings.EnableSleepDialogue,
            "RimTalk.Settings.EnableSleepDialogueTooltip".Translate().ToString());
        leftListing.Gap(6f);
        CheckboxLeft(leftListing, "RimTalk.Settings.ApplyMoodAndSocialEffects".Translate().ToString(),
            ref settings.ApplyMoodAndSocialEffects,
            "RimTalk.Settings.ApplyMoodAndSocialEffectsTooltip".Translate().ToString());
        leftListing.End();

        // --- Right Column ---
        Rect rightColumnRect = new Rect(leftColumnRect.xMax + columnGap, checkboxSectionRect.y, columnWidth,
            checkboxSectionRect.height);
        Listing_Standard rightListing = new Listing_Standard();
        rightListing.Begin(rightColumnRect);

        CheckboxLeft(rightListing, "RimTalk.Settings.AllowMonologue".Translate().ToString(),
            ref settings.AllowMonologue, "RimTalk.Settings.AllowMonologueTooltip".Translate().ToString());
        rightListing.Gap(6f);
        CheckboxLeft(rightListing, "RimTalk.Settings.AllowSlavesToTalk".Translate().ToString(),
            ref settings.AllowSlavesToTalk, "RimTalk.Settings.AllowSlavesToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        CheckboxLeft(rightListing, "RimTalk.Settings.AllowPrisonersToTalk".Translate().ToString(),
            ref settings.AllowPrisonersToTalk, "RimTalk.Settings.AllowPrisonersToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        CheckboxLeft(rightListing, "RimTalk.Settings.AllowOtherFactionsToTalk".Translate().ToString(),
            ref settings.AllowOtherFactionsToTalk,
            "RimTalk.Settings.AllowOtherFactionsToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        CheckboxLeft(rightListing, "RimTalk.Settings.AllowEnemiesToTalk".Translate().ToString(),
            ref settings.AllowEnemiesToTalk, "RimTalk.Settings.AllowEnemiesToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        CheckboxLeft(rightListing, "RimTalk.Settings.AllowBabiesToTalk".Translate().ToString(),
            ref settings.AllowBabiesToTalk, "RimTalk.Settings.AllowBabiesToTalkTooltip".Translate().ToString());
        rightListing.Gap(6f);
        CheckboxLeft(rightListing, "RimTalk.Settings.AllowNonHumanToTalk".Translate().ToString(),
            ref settings.AllowNonHumanToTalk, "RimTalk.Settings.AllowNonHumanToTalkTooltip".Translate().ToString());
        rightListing.End();

        // Advance the main listing standard's vertical position based on the taller of the two columns.
        float tallerColumnHeight = Mathf.Max(leftListing.CurHeight, rightListing.CurHeight);
        listingStandard.Gap(tallerColumnHeight - estimatedHeight);

        // Comfortable spacing between checkboxes and bottom buttons
        listingStandard.Gap(28f);

        // --- 3 Compact Centered Buttons: [Label embedded in button] ---
        const float btnWidth = 220f;
        const float btnHeight = 30f;
        const float btnGap = 14f;
        float totalBtnWidth = btnWidth * 3f + btnGap * 2f;

        Rect rowRect = listingStandard.GetRect(btnHeight);
        float startX = rowRect.x + (rowRect.width - totalBtnWidth) / 2f;

        // 1. Button Display
        Rect btn1Rect = new Rect(startX, rowRect.y, btnWidth, btnHeight);
        string btn1Text = $"{"RimTalk.Settings.ButtonDisplay".Translate()}: {settings.ButtonDisplay}";
        if (Widgets.ButtonText(btn1Rect, btn1Text))
        {
            var options = new List<FloatMenuOption>();
            foreach (ButtonDisplayMode mode in Enum.GetValues(typeof(ButtonDisplayMode)))
            {
                var currentMode = mode;
                options.Add(new FloatMenuOption(currentMode.ToString(), () => settings.ButtonDisplay = currentMode));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(btn1Rect, "RimTalk.Settings.ButtonDisplayTooltip".Translate().ToString());

        // 2. Pause AI at Speed
        Rect btn2Rect = new Rect(startX + btnWidth + btnGap, rowRect.y, btnWidth, btnHeight);
        string currentSpeedLabel = settings.DisableAiAtSpeed > (int)TimeSpeed.Normal
            ? GetFormattedSpeedLabel((TimeSpeed)settings.DisableAiAtSpeed)
            : "RimTalk.Settings.Disabled".Translate().ToString();
        string btn2Text = $"{"RimTalk.Settings.PauseAtSpeed".Translate()}: {currentSpeedLabel}";
        if (Widgets.ButtonText(btn2Rect, btn2Text))
        {
            var options = new List<FloatMenuOption>
            {
                new("RimTalk.Settings.Disabled".Translate().ToString(), () => settings.DisableAiAtSpeed = 0)
            };

            foreach (TimeSpeed speed in Enum.GetValues(typeof(TimeSpeed)))
            {
                if ((int)speed > (int)TimeSpeed.Normal)
                {
                    string label = GetFormattedSpeedLabel(speed);
                    TimeSpeed currentSpeed = speed;
                    options.Add(new FloatMenuOption(label, () => settings.DisableAiAtSpeed = (int)currentSpeed));
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(btn2Rect, "RimTalk.Settings.DisableAiAtSpeedTooltip".Translate().ToString());

        // 3. Bubble Settings
        Rect btn3Rect = new Rect(startX + (btnWidth + btnGap) * 2f, rowRect.y, btnWidth, btnHeight);
        string btn3Text = $"{"RimTalk.BubbleSettings.Title".Translate()}...";
        if (UIUtil.ButtonText(btn3Rect, btn3Text))
        {
            Find.WindowStack.Add(new Dialog_BubbleSettings());
        }
        TooltipHandler.TipRegion(btn3Rect, "RimTalk.Settings.BubbleModeTooltip".Translate().ToString());
    }

    internal void ResetBasicSettings(RimTalkSettings settings)
    {
        settings.TalkInterval = 10;
        settings.ReplyInterval = 4;
        _talkIntervalBuffer = "10";
        _replyIntervalBuffer = "4";
        settings.ProcessNonRimTalkInteractions = true;
        settings.AllowSimultaneousConversations = false;
        settings.ResetBubbleSettings();
        SpeechBubbleDrawer.RecomputeAllBubbleDimensions();
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
        settings.DisableAiAtSpeed = 0;
        settings.ButtonDisplay = ButtonDisplayMode.Toggle;
    }
}
