using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Cache = RimTalk.Data.Cache;

namespace RimTalk;

public partial class Settings
{
    private const int MaxPersonaLength = 500;
    private static Vector2 _personaScrollPos = Vector2.zero;

    private static Texture2D _visionGizmoIcon, _announceGizmoIcon, _chatGizmoIcon;
    private static Texture2D VisionGizmoIcon => UIUtil.GetTexture(ref _visionGizmoIcon, "UI/VisionGizmo");
    private static Texture2D AnnounceGizmoIcon => UIUtil.GetTexture(ref _announceGizmoIcon, "UI/AnnounceGizmo");
    private static Texture2D ChatGizmoIcon => UIUtil.GetTexture(ref _chatGizmoIcon, "UI/ChatGizmo");

    private void DrawCustomDialogueSettings(Listing_Standard listing)
    {
        RimTalkSettings settings = Get();
        settings.EnsureDialoguePresetsLanguage();
        if (settings.DialoguePresets == null || settings.DialoguePresets.Count == 0)
        {
            settings.DialoguePresets = CustomDialoguePreset.CreateDefaultPresets();
        }

        // =========================================================================
        // 1. Master Custom Dialogue Toggle
        // =========================================================================
        Text.Font = GameFont.Small;
        Color masterColor = settings.AllowCustomConversation ? new Color(0.6f, 0.9f, 0.6f) : Color.gray;
        Rect masterRect = listing.GetRect(24f);
        UIUtil.CheckboxLabeledLeft(
            masterRect,
            "RimTalk.PlayerSettings.AllowCustomConversation".Translate(),
            ref settings.AllowCustomConversation,
            labelColor: masterColor);
        TooltipHandler.TipRegion(masterRect, "RimTalk.PlayerSettings.AllowCustomConversationDesc".Translate());

        listing.Gap(2f);
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.75f, 0.75f, 0.75f);
        listing.Label("RimTalk.PlayerSettings.AllowCustomConversationDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // If master toggle is disabled, display notice and return
        if (!settings.AllowCustomConversation)
        {
            listing.Gap(6f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            listing.Label("RimTalk.PlayerSettings.DisabledNotice".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return;
        }

        listing.Gap(8f);

        // =========================================================================
        // 2. Player Participation & Settings (Compact 2-Column Layout)
        // =========================================================================
        float colGap = 16f;
        float colWidth = (listing.ColumnWidth - colGap) / 2f;

        bool allowDirectPlayerTalk = settings.PlayerDialogueMode != PlayerDialogueMode.Disabled;
        bool allowPlayerAiGen = settings.PlayerDialogueMode == PlayerDialogueMode.AIDriven;

        // Row 1: Direct Player Talk (Left) + Announcement Toggle (Right)
        Rect row1 = listing.GetRect(24f);

        // Left: Direct Player Talk Checkbox with Gizmo Previews
        bool prevDirectTalk = allowDirectPlayerTalk;
        Rect leftRect1 = new Rect(row1.x, row1.y, colWidth, row1.height);
        string directTalkLabel = "RimTalk.PlayerSettings.AllowDirectPlayerTalk".Translate();
        UIUtil.CheckboxLabeledLeft(leftRect1, directTalkLabel, ref allowDirectPlayerTalk);
        TooltipHandler.TipRegion(leftRect1, "RimTalk.PlayerSettings.AllowDirectPlayerTalkDesc".Translate());

        float iconX = leftRect1.x + 24f + 6f + Text.CalcSize(directTalkLabel).x + 6f;
        if (!allowDirectPlayerTalk) GUI.color = new Color(1f, 1f, 1f, 0.4f);
        GUI.DrawTexture(new Rect(iconX, row1.y + 2f, 20f, 20f), ChatGizmoIcon);
        GUI.DrawTexture(new Rect(iconX + 24f, row1.y + 2f, 20f, 20f), AnnounceGizmoIcon);
        GUI.color = Color.white;

        if (prevDirectTalk != allowDirectPlayerTalk)
        {
            settings.PlayerDialogueMode = allowDirectPlayerTalk
                ? (allowPlayerAiGen ? PlayerDialogueMode.AIDriven : PlayerDialogueMode.Manual)
                : PlayerDialogueMode.Disabled;
            Cache.InitializePlayerPawn();
        }

        // Right: AllowAnnouncement Checkbox
        Rect rightRect1 = new Rect(leftRect1.xMax + colGap, row1.y, colWidth, row1.height);
        UIUtil.CheckboxLabeledLeft(
            rightRect1,
            "RimTalk.Settings.AllowAnnouncement".Translate(),
            ref settings.AllowAnnouncement);
        TooltipHandler.TipRegion(rightRect1, "RimTalk.Settings.AllowAnnouncementTooltip".Translate());

        // Row 2: AI Generation Toggle (Left) + Player Name (Right) (Shown when Direct Player Talk is enabled)
        if (allowDirectPlayerTalk)
        {
            listing.Gap(4f);
            Rect row2 = listing.GetRect(26f);

            // Left: Player AI Generation Checkbox
            Rect leftRect2 = new Rect(row2.x, row2.y, colWidth, row2.height);
            bool prevAiGen = allowPlayerAiGen;
            UIUtil.CheckboxLabeledLeft(
                leftRect2,
                "RimTalk.PlayerSettings.AllowPlayerAiGen".Translate(),
                ref allowPlayerAiGen);
            TooltipHandler.TipRegion(leftRect2, "RimTalk.PlayerSettings.AllowPlayerAiGenTooltip".Translate());

            if (prevAiGen != allowPlayerAiGen)
            {
                settings.PlayerDialogueMode = allowPlayerAiGen
                    ? PlayerDialogueMode.AIDriven
                    : PlayerDialogueMode.Manual;
            }

            // Right: Player Name Input
            Rect rightRect2 = new Rect(leftRect2.xMax + colGap, row2.y, colWidth, row2.height);
            float nameLabelWidth = 90f;
            Rect nameLabelRect = new Rect(rightRect2.x, rightRect2.y, nameLabelWidth, rightRect2.height);
            Rect nameInputRect = new Rect(nameLabelRect.xMax + 4f, rightRect2.y, rightRect2.width - nameLabelWidth - 4f, rightRect2.height);

            TextAnchor origAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameLabelRect, "RimTalk.Settings.PlayerName".Translate());
            Text.Anchor = origAnchor;

            settings.PlayerName = Widgets.TextField(nameInputRect, settings.PlayerName);
            TooltipHandler.TipRegion(rightRect2, "RimTalk.Settings.PlayerNameTooltip".Translate());

            // Player Persona Area (Compact Expandable)
            if (allowPlayerAiGen)
            {
                listing.Gap(4f);
                Rect personaHeaderRect = listing.GetRect(20f);
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

                listing.Gap(2f);

                Rect personaBoxRect = listing.GetRect(60f);
                settings.PlayerPersona = DrawScrollableTextArea(personaBoxRect, settings.PlayerPersona ?? "",
                    ref _personaScrollPos, "PersonaEditor", enabled: true);
            }
        }

        listing.Gap(12f);
        Rect divider = listing.GetRect(1f);
        Color prevDividerCol = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        Widgets.DrawLineHorizontal(divider.x, divider.y, divider.width);
        GUI.color = prevDividerCol;
        listing.Gap(12f);

        // =========================================================================
        // 3. FloatMenu Presets Management (Compact Toolbar + 2-Row Cards)
        // =========================================================================
        Rect toolbarRow = listing.GetRect(26f);

        // Left: Checkbox for EnableDialoguePresets
        float presetCheckWidth = 320f;
        Rect presetCheckRect = new Rect(toolbarRow.x, toolbarRow.y, presetCheckWidth, toolbarRow.height);
        UIUtil.CheckboxLabeledLeft(
            presetCheckRect,
            "RimTalk.PlayerSettings.EnablePresets".Translate(),
            ref settings.EnableDialoguePresets);
        TooltipHandler.TipRegion(presetCheckRect, "RimTalk.PlayerSettings.PresetsDesc".Translate());

        // Right: + Add Button
        if (settings.EnableDialoguePresets)
        {
            float addBtnWidth = 110f;
            Rect addBtnRect = new Rect(toolbarRow.xMax - addBtnWidth, toolbarRow.y, addBtnWidth, 26f);
            Color prevColor = GUI.color;
            GUI.color = new Color(0.3f, 0.9f, 0.3f);
            if (UIUtil.ButtonText(addBtnRect, "+ " + "RimTalk.PlayerSettings.AddPreset".Translate()))
            {
                settings.DialoguePresets.Add(new CustomDialoguePreset(
                    "RimTalk.PlayerSettings.NewPresetName".Translate(),
                    "",
                    includeVision: false,
                    isAnnouncement: false,
                    isEnabled: false)
                {
                    IsCustomTitle = true,
                    IsCustomPrompt = true
                });
            }
            GUI.color = prevColor;
        }

        listing.Gap(2f);
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.75f, 0.75f, 0.75f);
        listing.Label("RimTalk.PlayerSettings.PresetsDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        if (!settings.EnableDialoguePresets)
        {
            listing.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            listing.Label("RimTalk.PlayerSettings.PresetsDisabledNotice".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return;
        }

        listing.Gap(6f);

        // Render 2-Column Preset Cards (Left: Title + Controls, Right: 2-Row Prompt TextArea)
        int removeIndex = -1;
        int moveUpIndex = -1;
        int moveDownIndex = -1;

        float row1Height = 22f;
        float row2Height = 22f;
        float cardInnerGap = 4f;
        float cardPadding = 4f;
        float cardTotalHeight = row1Height + cardInnerGap + row2Height + (cardPadding * 2);

        float leftColWidth = 260f;
        float presetColGap = 8f;

        for (int i = 0; i < settings.DialoguePresets.Count; i++)
        {
            var preset = settings.DialoguePresets[i];
            Rect cardRect = listing.GetRect(cardTotalHeight);

            Widgets.DrawBoxSolid(cardRect, new Color(1f, 1f, 1f, 0.035f));
            Widgets.DrawHighlightIfMouseover(cardRect);

            Rect inner = cardRect.ContractedBy(cardPadding);

            // === Left Column: Title (Row 1) & Controls (Row 2) ===
            // Row 1: Title
            Rect titleRect = new Rect(inner.x, inner.y, leftColWidth, row1Height);
            string newTitle = DrawTextFieldWithPlaceholder(titleRect, preset.Title, "RimTalk.PlayerSettings.PresetTitlePlaceholder".Translate());
            if (newTitle != preset.Title)
            {
                preset.Title = newTitle;
                preset.IsCustomTitle = true;
            }
            TooltipHandler.TipRegion(titleRect, "RimTalk.PlayerSettings.PresetTitleTooltip".Translate());

            // Row 2: Controls
            float row2Y = inner.y + row1Height + cardInnerGap;
            float btnSize = 22f;
            float btnGap = 2f;
            float checkSize = 20f;

            // Mode Toggles (Left-aligned)
            Rect visionBtnRect = new Rect(inner.x, row2Y, btnSize, btnSize);
            DrawGizmoToggleButton(visionBtnRect, VisionGizmoIcon, ref preset.IncludeVision,
                new Color(0.2f, 0.75f, 0.95f), "RimTalk.PlayerSettings.VisionTooltip".Translate());

            Rect announceBtnRect = new Rect(inner.x + btnSize + 4f, row2Y, btnSize, btnSize);
            DrawGizmoToggleButton(announceBtnRect, AnnounceGizmoIcon, ref preset.IsAnnouncement,
                new Color(0.95f, 0.65f, 0.2f), "RimTalk.PlayerSettings.AnnounceTooltip".Translate());

            // Action & State Controls (Right-aligned in left column)
            float delX = inner.x + leftColWidth - btnSize;
            Rect delRect = new Rect(delX, row2Y, btnSize, btnSize);
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.4f, 0.4f);
            if (UIUtil.ButtonText(delRect, "×")) removeIndex = i;
            GUI.color = prevColor;

            float downX = delX - btnGap - btnSize;
            Rect downRect = new Rect(downX, row2Y, btnSize, btnSize);
            GUI.enabled = i < settings.DialoguePresets.Count - 1;
            if (UIUtil.ButtonText(downRect, "▼")) moveDownIndex = i;
            GUI.enabled = true;

            float upX = downX - btnGap - btnSize;
            Rect upRect = new Rect(upX, row2Y, btnSize, btnSize);
            GUI.enabled = i > 0;
            if (UIUtil.ButtonText(upRect, "▲")) moveUpIndex = i;
            GUI.enabled = true;

            float checkX = upX - 6f - checkSize;
            Rect checkRect = new Rect(checkX, row2Y + 1f, checkSize, checkSize);
            Widgets.Checkbox(new Vector2(checkRect.x, checkRect.y), ref preset.IsEnabled, checkSize);
            if (Mouse.IsOver(checkRect))
            {
                TooltipHandler.TipRegion(checkRect, "RimTalk.PlayerSettings.Enabled".Translate());
            }

            // === Right Column: Prompt (2-Row Height TextArea) ===
            float promptX = inner.x + leftColWidth + presetColGap;
            float promptWidth = inner.xMax - promptX;
            Rect promptRect = new Rect(promptX, inner.y, promptWidth, inner.height);

            string promptPlaceholder = "RimTalk.PlayerSettings.PresetPromptPlaceholder".Translate();
            string newPrompt = DrawTextAreaWithPlaceholder(promptRect, preset.Prompt ?? "", promptPlaceholder);
            if (newPrompt != preset.Prompt)
            {
                preset.Prompt = newPrompt;
                preset.IsCustomPrompt = true;
            }
            if (!string.IsNullOrEmpty(preset.Prompt))
            {
                TooltipHandler.TipRegion(promptRect, preset.Prompt);
            }

            listing.Gap(3f);
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
            SoundDefOf.Click.PlayOneShotOnCamera(null);
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

    private string DrawTextAreaWithPlaceholder(Rect rect, string text, string placeholder)
    {
        string result = Widgets.TextArea(rect, text ?? "");

        if (string.IsNullOrEmpty(result))
        {
            TextAnchor originalAnchor = Text.Anchor;
            Color originalColor = GUI.color;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);

            Rect labelRect = new Rect(rect.x + 5f, rect.y + 3f, rect.width - 10f, rect.height - 6f);
            Widgets.Label(labelRect, placeholder);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
        }

        return result;
    }
}
