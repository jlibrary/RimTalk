using RimTalk.Compatibility;
using RimTalk.UI;
using UnityEngine;
using Verse;

namespace RimTalk;

public class Dialog_BubbleSettings : Window
{


    public override Vector2 InitialSize => new(540f, 700f);

    public Dialog_BubbleSettings()
    {
        draggable = true;
        preventCameraMotion = false;
        closeOnClickedOutside = false;
        doCloseX = true;
        absorbInputAroundWindow = false;
        forcePause = false;
    }

    public override void PostClose()
    {
        base.PostClose();
        Settings.Get()?.Write();
    }

    public override void DoWindowContents(Rect inRect)
    {
        var settings = Settings.Get();
        if (settings == null) return;

        // Title
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "RimTalk.BubbleSettings.Title".Translate());
        Text.Font = GameFont.Small;

        // 0. Bubble Mode Selector at the very top: [ None / Disabled ] [ RimTalk Native ] [ Interaction Bubbles ]
        Rect modeRowRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, 28f);
        float modeBtnW = (modeRowRect.width - 16f) / 3f;
        Rect disabledBtnRect = new Rect(modeRowRect.x, modeRowRect.y, modeBtnW, 28f);
        Rect nativeBtnRect = new Rect(disabledBtnRect.xMax + 8f, modeRowRect.y, modeBtnW, 28f);
        Rect ibBtnRect = new Rect(nativeBtnRect.xMax + 8f, modeRowRect.y, modeBtnW, 28f);

        bool isDisabled = settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.Disabled;
        bool isNative = settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.Native;
        bool isIB = settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.InteractionBubbles;
        bool isIBAvailable = BubbleCompatibilityPatch.IsBubblesModLoaded;

        if (DrawSelectableButton(disabledBtnRect, "RimTalk.BubbleSettings.ModeDisabled".Translate(), isDisabled))
        {
            settings.BubbleMode = RimTalkSettings.BubbleDisplayMode.Disabled;
            SpeechBubbleDrawer.Clear();
        }

        if (DrawSelectableButton(nativeBtnRect, "RimTalk.BubbleSettings.ModeNative".Translate(), isNative))
        {
            settings.BubbleMode = RimTalkSettings.BubbleDisplayMode.Native;
        }

        if (isIBAvailable)
        {
            if (DrawSelectableButton(ibBtnRect, "RimTalk.BubbleSettings.ModeInteractionBubbles".Translate(), isIB))
            {
                settings.BubbleMode = RimTalkSettings.BubbleDisplayMode.InteractionBubbles;
            }
        }
        else
        {
            Color origCol = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Widgets.ButtonText(ibBtnRect, "RimTalk.BubbleSettings.ModeInteractionBubbles".Translate());
            GUI.color = origCol;
            TooltipHandler.TipRegion(ibBtnRect, "RimTalk.BubbleSettings.InteractionBubblesNotLoaded".Translate());
        }

        // Description placeholder (always occupies constant height so UI elements never shift)
        float nextY = modeRowRect.yMax + 4f;
        Rect descRect = new Rect(inRect.x, nextY, inRect.width, 20f);
        if (isNative)
        {
            Color prevColor = GUI.color;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            GameFont prevFont = Text.Font;
            Text.Font = GameFont.Tiny;
            TextAnchor prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(descRect, "RimTalk.BubbleSettings.NativeDesc".Translate());
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
            GUI.color = prevColor;
        }
        nextY += 24f;

        // Live Preview Box (compact height to preserve vertical space)
        Rect previewBoxRect = new Rect(inRect.x, nextY, inRect.width, 60f);
        Widgets.DrawBoxSolid(previewBoxRect, new Color(0.10f, 0.11f, 0.13f, 0.5f));
        Widgets.DrawHighlightIfMouseover(previewBoxRect);

        if (isNative)
        {
            DrawPreviewBubble(previewBoxRect, settings);
        }
        else
        {
            TextAnchor prevA = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Color prevC = GUI.color;
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            string statusMsg = isDisabled
                ? "RimTalk.BubbleSettings.PreviewDisabledMsg".Translate()
                : "RimTalk.BubbleSettings.PreviewIBMsg".Translate();
            Widgets.Label(previewBoxRect, statusMsg);
            GUI.color = prevC;
            Text.Anchor = prevA;
        }

        // Settings Listing
        float listingY = previewBoxRect.yMax + 12f;
        Rect listingRect = new Rect(inRect.x, listingY, inRect.width, inRect.height - listingY - 45f);
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(listingRect);
        GUI.enabled = isNative;

        // 1. Theme (Light / Dark)
        Rect themeRow = listing.GetRect(28f);
        Widgets.Label(new Rect(themeRow.x, themeRow.y, 140f, 28f), "RimTalk.BubbleSettings.Theme".Translate());
        float btnW = 100f;
        Rect lightBtn = new Rect(themeRow.xMax - btnW * 2f - 10f, themeRow.y, btnW, 28f);
        Rect darkBtn = new Rect(themeRow.xMax - btnW, themeRow.y, btnW, 28f);

        bool isDark = settings.BubbleTheme == RimTalkSettings.SpeechBubbleTheme.Dark;
        if (DrawSelectableButton(lightBtn, "RimTalk.BubbleSettings.ThemeLight".Translate(), !isDark))
        {
            settings.BubbleTheme = RimTalkSettings.SpeechBubbleTheme.Light;
        }
        if (DrawSelectableButton(darkBtn, "RimTalk.BubbleSettings.ThemeDark".Translate(), isDark))
        {
            settings.BubbleTheme = RimTalkSettings.SpeechBubbleTheme.Dark;
        }

        listing.Gap(8f);

        // 2. Font Size Slider (5pt ~ 20pt)
        listing.Label("RimTalk.BubbleSettings.FontSize".Translate() + $": {Mathf.RoundToInt(settings.BubbleCustomFontSize)}pt");
        float newFontSize = listing.Slider(settings.BubbleCustomFontSize, 5f, 20f);
        if (Mathf.Abs(newFontSize - settings.BubbleCustomFontSize) > 0.01f)
        {
            settings.BubbleCustomFontSize = Mathf.Round(newFontSize);
            if (settings.BubbleCustomFontSize <= 11.5f) settings.BubbleFontSize = GameFont.Tiny;
            else if (settings.BubbleCustomFontSize >= 17.5f) settings.BubbleFontSize = GameFont.Medium;
            else settings.BubbleFontSize = GameFont.Small;
            SpeechBubbleDrawer.RecomputeAllBubbleDimensions();
        }

        listing.Gap(8f);

        // 3. Border Thickness (Thin / Normal / Thick)
        Rect borderRow = listing.GetRect(28f);
        Widgets.Label(new Rect(borderRow.x, borderRow.y, 140f, 28f), "RimTalk.BubbleSettings.BorderThickness".Translate());
        float borderBtnW = 75f;
        Rect thinBtn = new Rect(borderRow.xMax - borderBtnW * 3f - 10f, borderRow.y, borderBtnW, 28f);
        Rect normBtn = new Rect(borderRow.xMax - borderBtnW * 2f - 5f, borderRow.y, borderBtnW, 28f);
        Rect thickBtn = new Rect(borderRow.xMax - borderBtnW, borderRow.y, borderBtnW, 28f);

        if (DrawSelectableButton(thinBtn, "RimTalk.BubbleSettings.BorderThin".Translate(), settings.BubbleBorderThickness == RimTalkSettings.BorderThicknessMode.Thin))
        {
            settings.BubbleBorderThickness = RimTalkSettings.BorderThicknessMode.Thin;
        }
        if (DrawSelectableButton(normBtn, "RimTalk.BubbleSettings.BorderNormal".Translate(), settings.BubbleBorderThickness == RimTalkSettings.BorderThicknessMode.Normal))
        {
            settings.BubbleBorderThickness = RimTalkSettings.BorderThicknessMode.Normal;
        }
        if (DrawSelectableButton(thickBtn, "RimTalk.BubbleSettings.BorderThick".Translate(), settings.BubbleBorderThickness == RimTalkSettings.BorderThicknessMode.Thick))
        {
            settings.BubbleBorderThickness = RimTalkSettings.BorderThicknessMode.Thick;
        }

        listing.Gap(8f);

        // 4. Opacity Slider (10% ~ 100%)
        listing.Label("RimTalk.BubbleSettings.Opacity".Translate() + $": {Mathf.RoundToInt(settings.BubbleOpacity * 100)}%");
        settings.BubbleOpacity = listing.Slider(settings.BubbleOpacity, 0.10f, 1.0f);

        // 5. Scale Slider (50% ~ 140%)
        listing.Label("RimTalk.BubbleSettings.Scale".Translate() + $": {settings.BubbleScale:F2}x");
        float newScale = listing.Slider(settings.BubbleScale, 0.50f, 1.40f);
        if (Mathf.Abs(newScale - settings.BubbleScale) > 0.001f)
        {
            settings.BubbleScale = newScale;
            SpeechBubbleDrawer.RecomputeAllBubbleDimensions();
        }

        // 6. Padding Slider (50% ~ 160%)
        listing.Label("RimTalk.BubbleSettings.Padding".Translate() + $": {Mathf.RoundToInt(settings.BubblePadding * 100)}%");
        float newPadding = listing.Slider(settings.BubblePadding, 0.00f, 1.50f);
        if (Mathf.Abs(newPadding - settings.BubblePadding) > 0.001f)
        {
            settings.BubblePadding = newPadding;
            SpeechBubbleDrawer.RecomputeAllBubbleDimensions();
        }

        // 7. Duration Multiplier (0.5x ~ 2.0x)
        listing.Label("RimTalk.Settings.BubbleDuration".Translate() + $": {settings.BubbleDurationMultiplier:F1}x");
        settings.BubbleDurationMultiplier = listing.Slider(settings.BubbleDurationMultiplier, 0.5f, 2.0f);

        // 8. Vertical Offset (-1.5 ~ 1.5)
        listing.Label("RimTalk.BubbleSettings.VerticalOffset".Translate() + $": {settings.BubbleVerticalOffset:F2}");
        settings.BubbleVerticalOffset = listing.Slider(settings.BubbleVerticalOffset, -1.5f, 1.5f);

        listing.Gap(4f);

        // 9. Toggles: Emotion/Group Colors, Urgent Shake & Zoom Scaling
        listing.CheckboxLabeled("RimTalk.BubbleSettings.UseColors".Translate(), ref settings.BubbleUseColors, "RimTalk.BubbleSettings.UseColorsTooltip".Translate());
        listing.CheckboxLabeled("RimTalk.BubbleSettings.UrgentShake".Translate(), ref settings.BubbleUrgentShake, "RimTalk.BubbleSettings.UrgentShakeTooltip".Translate());
        bool prevScaleWithZoom = settings.BubbleScaleWithZoom;
        listing.CheckboxLabeled("RimTalk.BubbleSettings.ScaleWithZoom".Translate(), ref settings.BubbleScaleWithZoom, "RimTalk.BubbleSettings.ScaleWithZoomTooltip".Translate());
        if (prevScaleWithZoom != settings.BubbleScaleWithZoom)
        {
            SpeechBubbleDrawer.RecomputeAllBubbleDimensions();
        }

        GUI.enabled = true;
        listing.End();

        // Bottom action buttons: Reset to Default (left), Close (right)
        float bottomBtnH = 32f;
        float bottomBtnY = inRect.height - bottomBtnH;
        Rect resetBtnRect = new Rect(inRect.x, bottomBtnY, 150f, bottomBtnH);
        if (Widgets.ButtonText(resetBtnRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            settings.ResetBubbleSettings();
            SpeechBubbleDrawer.RecomputeAllBubbleDimensions();
        }

        Rect closeBtnRect = new Rect(inRect.xMax - 110f, bottomBtnY, 110f, bottomBtnH);
        if (Widgets.ButtonText(closeBtnRect, "CloseButton".Translate()))
        {
            Close();
        }
    }

    private static bool DrawSelectableButton(Rect rect, string label, bool isSelected)
    {
        Color origColor = GUI.color;
        if (isSelected)
        {
            GUI.color = Color.green;
        }

        bool clicked = Widgets.ButtonText(rect, label);

        GUI.color = origColor;
        return clicked;
    }

    private void DrawPreviewBubble(Rect previewBoxRect, RimTalkSettings settings)
    {
        string sampleText = "RimTalk.BubbleSettings.PreviewText".Translate();
        bool isLight = settings.BubbleTheme == RimTalkSettings.SpeechBubbleTheme.Light;
        Texture2D bgTex = isLight ? SpeechBubbleDrawer.BubbleBgLight : SpeechBubbleDrawer.BubbleBgDark;

        GameFont prevFont = Text.Font;
        GameFont targetFont = GameFont.Small;
        int origSize = Text.fontStyles[(int)targetFont].fontSize;
        int customSize = Mathf.RoundToInt(settings.BubbleCustomFontSize);

        TextAnchor prevAnchor = Text.Anchor;
        Color prevColor = GUI.color;

        float scale = settings.BubbleScale;
        float padMult = settings.BubblePadding;
        float pad = Mathf.Round(SpeechBubble.BasePadding * scale * padMult);
        float minW = Mathf.Round(SpeechBubble.MinBubbleWidth * scale);
        float cornerClearance = Mathf.Round(SpeechBubble.CornerClearance * scale);

        try
        {
            Text.Font = targetFont;
            Text.fontStyles[(int)targetFont].fontSize = customSize;
            Vector2 textSize = Text.CalcSize(sampleText);

            float visualTextHeight = textSize.y - 4f * scale;
            float bubbleW = Mathf.Round(Mathf.Max(minW, textSize.x + pad * 2f + cornerClearance * 2f));
            float bubbleH = Mathf.Round(Mathf.Max(SpeechBubble.MinBubbleHeight, visualTextHeight + pad * 2f));

            Vector2 center = previewBoxRect.center;
            float bx = Mathf.Round(center.x - bubbleW * 0.5f);
            float by = Mathf.Round(center.y - bubbleH * 0.5f);
            Rect bubbleRect = new Rect(bx, by, bubbleW, bubbleH);

            // Background
            GUI.color = new Color(1f, 1f, 1f, settings.BubbleOpacity);
            if (bgTex != null)
            {
                Widgets.DrawAtlas(bubbleRect, bgTex);
            }
            else
            {
                Color solidBg = isLight
                    ? new Color(0.96f, 0.96f, 0.97f, settings.BubbleOpacity)
                    : new Color(0.09f, 0.10f, 0.12f, settings.BubbleOpacity);
                Widgets.DrawBoxSolid(bubbleRect, solidBg);
            }

            // Border (Reflects BubbleUseColors & Theme)
            Color sampleBorderColor = isLight ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
            if (settings.BubbleUseColors)
            {
                Color groupCol = UIUtil.GetConversationColor(1);
                if (groupCol != Color.clear)
                {
                    sampleBorderColor = new Color(groupCol.r, groupCol.g, groupCol.b, 1f);
                }
            }

            GUI.color = sampleBorderColor;
            Texture2D activeBorder = settings.BubbleBorderThickness switch
            {
                RimTalkSettings.BorderThicknessMode.Thin => SpeechBubbleDrawer.BubbleBorderThin ?? SpeechBubbleDrawer.BubbleBorder,
                RimTalkSettings.BorderThicknessMode.Thick => SpeechBubbleDrawer.BubbleBorderThick ?? SpeechBubbleDrawer.BubbleBorder,
                _ => SpeechBubbleDrawer.BubbleBorder
            };
            if (activeBorder != null)
            {
                Widgets.DrawAtlas(bubbleRect, activeBorder);
            }

            // Text
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = isLight ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
            Rect textRect = bubbleRect.ExpandedBy(0f, 2f);
            Widgets.Label(textRect, sampleText);
        }
        finally
        {
            Text.fontStyles[(int)targetFont].fontSize = origSize;
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
            GUI.color = prevColor;
        }
    }
}
