using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk;

public partial class Settings
{
    private const int MaxPersonaLength = 500;
    private static Vector2 _personaScrollPos = Vector2.zero;

    private static Texture2D _visionGizmoIcon;
    private static Texture2D VisionGizmoIcon => _visionGizmoIcon ??= ContentFinder<Texture2D>.Get("UI/VisionGizmo");

    private static Texture2D _announceGizmoIcon;
    private static Texture2D AnnounceGizmoIcon => _announceGizmoIcon ??= ContentFinder<Texture2D>.Get("UI/AnnounceGizmo");

    private void DrawCustomDialogueSettings(Listing_Standard listing)
    {
        RimTalkSettings settings = Get();
        if (settings.DialoguePresets == null || settings.DialoguePresets.Count == 0)
        {
            settings.DialoguePresets = CustomDialoguePreset.CreateDefaultPresets();
        }

        // =========================================================================
        // 1. Master Custom Dialogue Toggle
        // =========================================================================
        Text.Font = GameFont.Small;
        GUI.color = settings.AllowCustomConversation ? new Color(0.6f, 0.9f, 0.6f) : Color.gray;
        listing.CheckboxLabeled(
            "RimTalk.PlayerSettings.AllowCustomConversation".Translate(),
            ref settings.AllowCustomConversation,
            "RimTalk.PlayerSettings.AllowCustomConversationTooltip".Translate());
        GUI.color = Color.white;

        listing.Gap(4f);

        // If master toggle is disabled, display notice and return
        if (!settings.AllowCustomConversation)
        {
            listing.Gap(12f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            listing.Label("RimTalk.PlayerSettings.DisabledNotice".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return;
        }

        listing.Gap(16f);

        // =========================================================================
        // 2. Player Participation & Persona Settings
        // =========================================================================
        Text.Font = GameFont.Small;
        GUI.color = new Color(1f, 0.85f, 0.5f);
        listing.Label("RimTalk.PlayerSettings.SectionPlayer".Translate());
        GUI.color = Color.white;

        listing.Gap(8f);

        bool allowDirectPlayerTalk = settings.PlayerDialogueMode != PlayerDialogueMode.Disabled;
        bool allowPlayerAiGen = settings.PlayerDialogueMode == PlayerDialogueMode.AIDriven;

        // Direct Player Talk Checkbox
        bool prevDirectTalk = allowDirectPlayerTalk;
        listing.CheckboxLabeled(
            "RimTalk.PlayerSettings.AllowDirectPlayerTalk".Translate(),
            ref allowDirectPlayerTalk,
            "RimTalk.PlayerSettings.AllowDirectPlayerTalkTooltip".Translate());

        if (prevDirectTalk != allowDirectPlayerTalk)
        {
            settings.PlayerDialogueMode = allowDirectPlayerTalk
                ? (allowPlayerAiGen ? PlayerDialogueMode.AIDriven : PlayerDialogueMode.Manual)
                : PlayerDialogueMode.Disabled;
            Cache.InitializePlayerPawn();
        }

        listing.Gap(6f);

        // Player Name Input & Player AI Generation
        if (allowDirectPlayerTalk)
        {
            Rect nameRowRect = listing.GetRect(28f);
            float nameLabelWidth = 120f;
            float nameInputWidth = 220f;

            Rect nameLabelRect = new Rect(nameRowRect.x, nameRowRect.y, nameLabelWidth, nameRowRect.height);
            Rect nameInputRect = new Rect(nameLabelRect.xMax + 6f, nameRowRect.y, nameInputWidth, nameRowRect.height);

            TextAnchor origAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameLabelRect, "RimTalk.Settings.PlayerName".Translate());
            Text.Anchor = origAnchor;

            settings.PlayerName = Widgets.TextField(nameInputRect, settings.PlayerName);
            TooltipHandler.TipRegion(nameRowRect, "RimTalk.Settings.PlayerNameTooltip".Translate());

            listing.Gap(6f);

            // Player AI Generation Checkbox
            bool prevAiGen = allowPlayerAiGen;
            listing.CheckboxLabeled(
                "RimTalk.PlayerSettings.AllowPlayerAiGen".Translate(),
                ref allowPlayerAiGen,
                "RimTalk.PlayerSettings.AllowPlayerAiGenTooltip".Translate());

            if (prevAiGen != allowPlayerAiGen)
            {
                settings.PlayerDialogueMode = allowPlayerAiGen
                    ? PlayerDialogueMode.AIDriven
                    : PlayerDialogueMode.Manual;
            }

            listing.Gap(6f);

            // Player Persona Area
            if (allowPlayerAiGen)
            {
                Rect personaHeaderRect = listing.GetRect(22f);
                Rect personaTitleRect = new Rect(personaHeaderRect.x, personaHeaderRect.y, 200f, personaHeaderRect.height);
                Widgets.Label(personaTitleRect, "RimTalk.Settings.PlayerPersona".Translate());
                TooltipHandler.TipRegion(personaTitleRect, "RimTalk.Settings.PlayerPersonaTooltip".Translate());

                string personaStr = settings.PlayerPersona ?? "";
                Text.Font = GameFont.Tiny;
                Color countColor = personaStr.Length > 300 ? Color.yellow : Color.gray;
                if (personaStr.Length >= MaxPersonaLength) countColor = Color.red;
                GUI.color = countColor;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(personaHeaderRect.xMax - 150f, personaHeaderRect.y, 150f, personaHeaderRect.height),
                    "RimTalk.PersonaEditor.Characters".Translate(personaStr.Length, 300));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                listing.Gap(4f);

                Rect personaBoxRect = listing.GetRect(85f);
                settings.PlayerPersona = DrawScrollableTextArea(personaBoxRect, settings.PlayerPersona ?? "",
                    ref _personaScrollPos, "PersonaEditor", enabled: true);

                listing.Gap(6f);
            }
        }

        // AllowAnnouncement Checkbox
        listing.CheckboxLabeled(
            "RimTalk.Settings.AllowAnnouncement".Translate(),
            ref settings.AllowAnnouncement,
            "RimTalk.Settings.AllowAnnouncementTooltip".Translate());

        listing.Gap(32f);

        // =========================================================================
        // 3. FloatMenu Presets Management
        // =========================================================================
        Text.Font = GameFont.Small;
        GUI.color = new Color(1f, 0.85f, 0.5f);
        listing.Label("RimTalk.PlayerSettings.SectionPresets".Translate());
        GUI.color = Color.white;

        listing.Gap(4f);

        // Subline: Description on left, Buttons (+ Add, Reset) on right
        Rect toolbarRow = listing.GetRect(26f);

        float addBtnWidth = 130f;
        float resetBtnWidth = 120f;
        float btnHeight = 26f;
        Rect resetBtnRect = new Rect(toolbarRow.xMax - resetBtnWidth, toolbarRow.y, resetBtnWidth, btnHeight);
        Rect addBtnRect = new Rect(resetBtnRect.x - addBtnWidth - 6f, toolbarRow.y, addBtnWidth, btnHeight);

        Rect presetDescRect = new Rect(toolbarRow.x, toolbarRow.y, addBtnRect.x - toolbarRow.x - 8f, toolbarRow.height);
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.75f, 0.75f, 0.75f);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(presetDescRect, "RimTalk.PlayerSettings.PresetsDesc".Translate());
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        GUI.color = new Color(0.3f, 0.9f, 0.3f);
        if (Widgets.ButtonText(addBtnRect, "+ " + "RimTalk.PlayerSettings.AddPreset".Translate()))
        {
            settings.DialoguePresets.Add(new CustomDialoguePreset(
                "RimTalk.PlayerSettings.NewPresetName".Translate(),
                "",
                includeVision: false,
                isAnnouncement: false,
                isEnabled: false));
        }
        GUI.color = Color.white;

        if (Widgets.ButtonText(resetBtnRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimTalk.PlayerSettings.ResetPresetsConfirm".Translate(),
                () => settings.DialoguePresets = CustomDialoguePreset.CreateDefaultPresets()));
        }

        listing.Gap(10f);

        // Table Header
        Rect tableHeaderRect = listing.GetRect(20f);
        float totalWidth = tableHeaderRect.width;
        float enabledWidth = 50f;
        float controlsWidth = 145f;
        float titleHeaderWidth = totalWidth - enabledWidth - controlsWidth - 10f;

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(tableHeaderRect.x + 5f, tableHeaderRect.y, enabledWidth, 20f), "RimTalk.Settings.EnabledHeader".Translate());
        Widgets.Label(new Rect(tableHeaderRect.x + enabledWidth + 5f, tableHeaderRect.y, titleHeaderWidth, 20f), "RimTalk.PlayerSettings.PresetTitleHeader".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listing.Gap(4f);

        // Render Flat Preset Rows
        int removeIndex = -1;
        int moveUpIndex = -1;
        int moveDownIndex = -1;

        float promptBoxHeight = 60f;
        float rowTotalHeight = 28f + promptBoxHeight + 8f;

        for (int i = 0; i < settings.DialoguePresets.Count; i++)
        {
            var preset = settings.DialoguePresets[i];
            Rect cardRect = listing.GetRect(rowTotalHeight);

            // Unified single flat background color
            Widgets.DrawBoxSolid(cardRect, new Color(1f, 1f, 1f, 0.035f));
            Widgets.DrawHighlightIfMouseover(cardRect);

            var inner = cardRect.ContractedBy(6f);
            float innerY = inner.y;

            // Row 1: Checkbox, Title Input, Vision Gizmo Icon, Announce Gizmo Icon, Up, Down, Delete
            float row1Height = 24f;
            float btnSize = 22f;
            float btnGap = 4f;
            float rightZoneWidth = (btnSize * 5) + (btnGap * 4) + 10f;
            float titleWidth = inner.width - enabledWidth - rightZoneWidth;

            // Enable Checkbox
            Rect checkRect = new Rect(inner.x + 4f, innerY + 1f, 22f, 22f);
            Widgets.Checkbox(new Vector2(checkRect.x, checkRect.y), ref preset.IsEnabled, 20f);
            if (Mouse.IsOver(new Rect(inner.x, innerY, enabledWidth, row1Height)))
            {
                TooltipHandler.TipRegion(new Rect(inner.x, innerY, enabledWidth, row1Height), "RimTalk.PlayerSettings.Enabled".Translate());
            }

            // Title TextField
            Rect titleRect = new Rect(inner.x + enabledWidth, innerY, titleWidth, row1Height);
            preset.Title = DrawTextFieldWithPlaceholder(titleRect, preset.Title, "RimTalk.PlayerSettings.PresetTitlePlaceholder".Translate());
            TooltipHandler.TipRegion(titleRect, "RimTalk.PlayerSettings.PresetTitleTooltip".Translate());

            float currentBtnX = titleRect.xMax + 10f;

            // Vision Toggle Icon Button
            Rect visionBtnRect = new Rect(currentBtnX, innerY + 1f, btnSize, btnSize);
            DrawGizmoToggleButton(visionBtnRect, VisionGizmoIcon, ref preset.IncludeVision,
                new Color(0.2f, 0.75f, 0.95f), "RimTalk.PlayerSettings.VisionTooltip".Translate());
            currentBtnX += btnSize + btnGap;

            // Announce Toggle Icon Button
            Rect announceBtnRect = new Rect(currentBtnX, innerY + 1f, btnSize, btnSize);
            DrawGizmoToggleButton(announceBtnRect, AnnounceGizmoIcon, ref preset.IsAnnouncement,
                new Color(0.95f, 0.65f, 0.2f), "RimTalk.PlayerSettings.AnnounceTooltip".Translate());
            currentBtnX += btnSize + btnGap + 4f;

            // Up Button
            Rect upRect = new Rect(currentBtnX, innerY + 1f, btnSize, btnSize);
            GUI.enabled = i > 0;
            if (Widgets.ButtonText(upRect, "▲")) moveUpIndex = i;
            GUI.enabled = true;
            currentBtnX += btnSize + btnGap;

            // Down Button
            Rect downRect = new Rect(currentBtnX, innerY + 1f, btnSize, btnSize);
            GUI.enabled = i < settings.DialoguePresets.Count - 1;
            if (Widgets.ButtonText(downRect, "▼")) moveDownIndex = i;
            GUI.enabled = true;
            currentBtnX += btnSize + btnGap;

            // Delete Button
            Rect delRect = new Rect(currentBtnX, innerY + 1f, btnSize, btnSize);
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.4f, 0.4f);
            if (Widgets.ButtonText(delRect, "×")) removeIndex = i;
            GUI.color = prevColor;

            innerY += row1Height + 4f;

            // Row 2: Scrollable Prompt TextArea with Horizontal & Vertical scroll
            Rect promptBoxRect = new Rect(inner.x + 4f, innerY, inner.width - 8f, promptBoxHeight);
            preset.Prompt = DrawScrollableTextArea(promptBoxRect, preset.Prompt ?? "",
                ref preset.ScrollPosition, $"PresetPrompt_{preset.Id}");

            listing.Gap(4f);
        }

        // Handle collection changes
        if (removeIndex >= 0 && removeIndex < settings.DialoguePresets.Count)
        {
            settings.DialoguePresets.RemoveAt(removeIndex);
        }
        else if (moveUpIndex > 0)
        {
            var item = settings.DialoguePresets[moveUpIndex];
            settings.DialoguePresets.RemoveAt(moveUpIndex);
            settings.DialoguePresets.Insert(moveUpIndex - 1, item);
        }
        else if (moveDownIndex >= 0 && moveDownIndex < settings.DialoguePresets.Count - 1)
        {
            var item = settings.DialoguePresets[moveDownIndex];
            settings.DialoguePresets.RemoveAt(moveDownIndex);
            settings.DialoguePresets.Insert(moveDownIndex + 1, item);
        }
    }

    private void DrawGizmoToggleButton(Rect rect, Texture2D icon, ref bool isToggled, Color activeHighlightColor, string tooltip)
    {
        if (isToggled)
        {
            Color bgColor = activeHighlightColor;
            bgColor.a = 0.35f;
            Widgets.DrawBoxSolid(rect, bgColor);
            Widgets.DrawHighlightSelected(rect);
        }
        else
        {
            Widgets.DrawHighlightIfMouseover(rect);
        }

        Color iconColor = isToggled ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.7f);
        if (Widgets.ButtonImage(rect, icon, iconColor, isToggled ? activeHighlightColor : GenUI.MouseoverColor))
        {
            isToggled = !isToggled;
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            TooltipHandler.TipRegion(rect, tooltip);
        }
    }

    private static string DrawScrollableTextArea(Rect rect, string text, ref Vector2 scrollPos,
        string controlId, bool enabled = true)
    {
        // Calculate Content Width for Horizontal Scrolling when line length exceeds viewport
        float maxLineWidth = 0f;
        if (!string.IsNullOrEmpty(text))
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                float lineWidth = Text.CalcSize(lines[i]).x;
                if (lineWidth > maxLineWidth) maxLineWidth = lineWidth;
            }
        }

        float innerViewWidth = Mathf.Max(rect.width - 16f, maxLineWidth + 30f);
        float calculatedTextHeight = Mathf.Max(rect.height - 4f, Text.CalcHeight(text, innerViewWidth) + 10f);
        Rect contentRect = new Rect(0f, 0f, innerViewWidth, calculatedTextHeight);

        Color savedColor = GUI.color;
        if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.4f);

        Widgets.BeginScrollView(rect, ref scrollPos, contentRect);
        GUI.SetNextControlName(controlId);

        string result = text;
        if (enabled)
        {
            result = Widgets.TextArea(new Rect(0f, 0f, innerViewWidth, calculatedTextHeight), text);
        }
        else
        {
            GUI.enabled = false;
            Widgets.TextArea(new Rect(0f, 0f, innerViewWidth, calculatedTextHeight), text);
            GUI.enabled = true;
        }

        Widgets.EndScrollView();
        GUI.color = savedColor;

        return result;
    }
}
