using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Source.Data;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

[StaticConstructorOnStartup]
public static class SpeechBubbleDrawer
{
    private const int InitialPoolSize = 32;
    private const int MaxBubblesPerPawn = 3;

    private static Texture2D _bubbleBgDark, _bubbleBgLight, _bubbleBorder, _bubbleBorderThin, _bubbleBorderThick;
    public static Texture2D BubbleBgDark => UIUtil.GetTexture(ref _bubbleBgDark, "UI/SpeechBubble_BG");
    public static Texture2D BubbleBgLight => UIUtil.GetTexture(ref _bubbleBgLight, "UI/SpeechBubble_BG_Light");
    public static Texture2D BubbleBorder => UIUtil.GetTexture(ref _bubbleBorder, "UI/SpeechBubble_Border");
    public static Texture2D BubbleBorderThin => UIUtil.GetTexture(ref _bubbleBorderThin, "UI/SpeechBubble_Border_Thin");
    public static Texture2D BubbleBorderThick => UIUtil.GetTexture(ref _bubbleBorderThick, "UI/SpeechBubble_Border_Thick");

    private static readonly List<SpeechBubble> Pool = new(InitialPoolSize);
    private static readonly List<SpeechBubble> ActiveBubbles = new(InitialPoolSize);

    public static bool SuppressForScreenshot = false;

    static SpeechBubbleDrawer()
    {
        for (int i = 0; i < InitialPoolSize; i++)
        {
            Pool.Add(new SpeechBubble());
        }
    }

    public static void AddBubble(Pawn pawn, string text, int conversationId = -1, InteractionType interactionType = InteractionType.None, TalkType talkType = TalkType.Other)
    {
        if (pawn == null || string.IsNullOrWhiteSpace(text)) return;

        var settings = Settings.Get();
        if (settings == null || settings.BubbleMode != RimTalkSettings.BubbleDisplayMode.Native) return;

        // Check if drafted and whether drafted speech is suppressed
        if (!settings.DisplayTalkWhenDrafted && (pawn.drafter?.Drafted ?? false)) return;

        // Ensure max bubbles per pawn limit
        int pawnBubbleCount = 0;
        SpeechBubble oldestPawnBubble = null;
        for (int i = 0; i < ActiveBubbles.Count; i++)
        {
            if (ActiveBubbles[i].Pawn == pawn)
            {
                pawnBubbleCount++;
                if (oldestPawnBubble == null || ActiveBubbles[i].StartTick < oldestPawnBubble.StartTick)
                {
                    oldestPawnBubble = ActiveBubbles[i];
                }
            }
        }

        if (pawnBubbleCount >= MaxBubblesPerPawn && oldestPawnBubble != null)
        {
            oldestPawnBubble.Deactivate();
            ActiveBubbles.Remove(oldestPawnBubble);
        }

        SpeechBubble bubble = GetPooledBubble();
        bubble.Init(pawn, text.Trim(), conversationId, interactionType, talkType);
        ActiveBubbles.Add(bubble);
    }

    public static void AddBubble(LogEntry entry)
    {
        Pawn initiator = (entry as PlayLogEntry_RimTalkInteraction)?.Initiator ?? entry?.GetConcerns()?.FirstOrDefault() as Pawn;
        if (initiator == null || initiator.Map != Find.CurrentMap) return;

        string dialogue = entry.ToGameStringFromPOV(initiator)?.StripTags();
        if (string.IsNullOrWhiteSpace(dialogue)) return;

        var rt = entry as PlayLogEntry_RimTalkInteraction;
        AddBubble(initiator, dialogue, rt?.ConversationId ?? -1, rt?.InteractionType ?? InteractionType.None, rt?.TalkType ?? TalkType.Other);
    }

    private static int _lastZoomTier = 2;

    public static int GetCurrentZoomTier()
    {
        var settings = Settings.Get();
        if (settings == null || !settings.BubbleScaleWithZoom) return 2;

        CameraDriver cameraDriver = Find.CameraDriver;
        if (cameraDriver == null) return 2;

        float rootSize = cameraDriver.ZoomRootSize;

        // 5-tier hysteresis: 0 (Closest), 1 (Close), 2 (Middle), 3 (Far), 4 (Very Far)
        return _lastZoomTier switch
        {
            0 => rootSize > 16.0f ? (rootSize > 24.0f ? (rootSize > 37.0f ? (rootSize > 49.0f ? 4 : 3) : 2) : 1) : 0,
            1 => rootSize < 14.0f ? 0 : (rootSize > 24.0f ? (rootSize > 37.0f ? (rootSize > 49.0f ? 4 : 3) : 2) : 1),
            2 => rootSize < 14.0f ? 0 : (rootSize < 22.0f ? 1 : (rootSize > 37.0f ? (rootSize > 49.0f ? 4 : 3) : 2)),
            3 => rootSize < 22.0f ? (rootSize < 14.0f ? 0 : 1) : (rootSize < 35.0f ? 2 : (rootSize > 49.0f ? 4 : 3)),
            4 => rootSize < 47.0f ? (rootSize < 35.0f ? (rootSize < 22.0f ? (rootSize < 14.0f ? 0 : 1) : 2) : 3) : 4,
            _ => rootSize < 15.0f ? 0 : (rootSize < 23.0f ? 1 : (rootSize < 36.0f ? 2 : (rootSize < 48.0f ? 3 : 4)))
        };
    }

    public static float GetEffectiveBubbleScale(RimTalkSettings settings)
    {
        float baseScale = settings?.BubbleScale ?? 1f;
        if (settings == null || !settings.BubbleScaleWithZoom) return baseScale;

        int tier = GetCurrentZoomTier();
        float mult = tier switch
        {
            0 => 1.40f,
            1 => 1.20f,
            3 => 0.70f,
            4 => 0.55f,
            _ => 1.00f
        };
        return Mathf.Clamp(baseScale * mult, 0.35f, 2.00f);
    }

    public static float GetEffectiveFontSize(RimTalkSettings settings)
    {
        float baseSize = settings?.BubbleCustomFontSize ?? 11f;
        if (settings == null || !settings.BubbleScaleWithZoom) return baseSize;

        int tier = GetCurrentZoomTier();
        float delta = tier switch
        {
            0 => 4f,
            1 => 2f,
            3 => -3f,
            4 => -4f,
            _ => 0f
        };
        return Mathf.Clamp(baseSize + delta, 6f, 26f);
    }

    public static void Clear()
    {
        for (int i = 0; i < ActiveBubbles.Count; i++)
        {
            ActiveBubbles[i].Deactivate();
        }
        ActiveBubbles.Clear();
        _lastZoomTier = 2;
    }

    public static void DrawBubbles()
    {
        if (SuppressForScreenshot || Overlay.SuppressForScreenshot) return;
        if (Find.CurrentMap == null) return;
        if (WorldRendererUtility.CurrentWorldRenderMode != WorldRenderMode.None) return;
        if (ActiveBubbles.Count == 0) return;

        var settings = Settings.Get();
        if (settings == null || settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.Disabled) return;

        CameraDriver cameraDriver = Find.CameraDriver;
        if (cameraDriver == null) return;

        float zoomRootSize = cameraDriver.ZoomRootSize;
        float zoomFade = zoomRootSize > 35f ? Mathf.Clamp01(1f - (zoomRootSize - 35f) / 12f) : 1f;
        if (zoomFade <= 0.01f) return; // Zoomed out to max or beyond, do not render

        CellRect currentViewRect = cameraDriver.CurrentViewRect.ExpandedBy(2);
        int curTicks = GenTicks.TicksGame;

        // Cleanup expired or invalid bubbles first
        for (int i = ActiveBubbles.Count - 1; i >= 0; i--)
        {
            SpeechBubble b = ActiveBubbles[i];
            if (!b.Active || curTicks >= b.ExpiryTick || b.Pawn == null ||
                !b.Pawn.Spawned || b.Pawn.Dead || b.Pawn.Destroyed || b.Pawn.Map != Find.CurrentMap)
            {
                b.Deactivate();
                ActiveBubbles.RemoveAt(i);
            }
        }

        if (ActiveBubbles.Count == 0) return;

        // Approach B: Dynamic stepped zoom scaling without GUI matrix manipulation
        if (settings != null && settings.BubbleScaleWithZoom)
        {
            int currentTier = GetCurrentZoomTier();
            if (currentTier != _lastZoomTier)
            {
                _lastZoomTier = currentTier;
                RecomputeAllBubbleDimensions();
            }
        }
        else
        {
            _lastZoomTier = 2;
        }

        TextAnchor originalAnchor = Text.Anchor;
        GameFont originalFont = Text.Font;
        Color originalColor = GUI.color;
        GameFont targetFont = GameFont.Small;
        int originalSize = Text.fontStyles[(int)targetFont].fontSize;
        float baseFontSize = GetEffectiveFontSize(settings);

        try
        {
            Text.Font = targetFont;
            Text.Anchor = TextAnchor.MiddleCenter;

            for (int i = 0; i < ActiveBubbles.Count; i++)
            {
                SpeechBubble bubble = ActiveBubbles[i];
                Pawn pawn = bubble.Pawn;

                // Frustum Culling: Skip immediately if not visible on screen
                if (pawn.Map.fogGrid.IsFogged(pawn.Position)) continue;
                if (!currentViewRect.Contains(pawn.Position)) continue;

                // Fade calculation: Aggressive interactions pop in instantly; others ease in smoothly (12 ticks)
                int elapsed = curTicks - bubble.StartTick;
                int remaining = bubble.ExpiryTick - curTicks;
                float fadeAlpha = 1f;
                if (elapsed < 12 && !bubble.IsAggressive) fadeAlpha = elapsed / 12f;
                else if (remaining < 25) fadeAlpha = remaining / 25f;
                fadeAlpha = Mathf.Clamp01(fadeAlpha) * zoomFade;

                if (fadeAlpha <= 0.01f) continue;

                float baseOpacity = settings?.BubbleOpacity ?? 0.90f;
                float bgAlpha = fadeAlpha * baseOpacity;

                // Anchor safely relative to pawn head
                float configuredOffset = settings?.BubbleVerticalOffset ?? 1.0f;
                float headOffset = (pawn.Downed || bubble.IsDowned) ? 0.40f : configuredOffset;
                Vector2 baseScreenPos = GenMapUI.LabelDrawPosFor(pawn, headOffset);

                // Calculate vertical stacking offset: newer bubbles sit close to the head;
                // older bubbles are gently pushed upwards so reading order is natural (top-to-bottom)
                // and newer bubbles don't jump when older ones expire.
                float yOffset = 0f;
                for (int j = i + 1; j < ActiveBubbles.Count; j++)
                {
                    if (ActiveBubbles[j].Pawn == pawn)
                    {
                        yOffset += ActiveBubbles[j].BubbleSize.y + 2f;
                    }
                }

                int drawW = Mathf.RoundToInt(bubble.BubbleSize.x);
                int drawH = Mathf.RoundToInt(bubble.BubbleSize.y);
                int drawX = Mathf.RoundToInt(baseScreenPos.x - drawW * 0.5f);
                int drawY = headOffset >= 0f
                    ? Mathf.RoundToInt(baseScreenPos.y - drawH - yOffset)
                    : Mathf.RoundToInt(baseScreenPos.y + yOffset);

                // Urgent impact shake: Ultra-fast horizontal vibration for 0.6 real-time seconds (if enabled)
                bool allowShake = settings?.BubbleUrgentShake ?? true;
                float elapsedRealSec = Time.realtimeSinceStartup - bubble.StartRealTime;
                if (allowShake && bubble.IsUrgent && elapsedRealSec < 0.6f)
                {
                    float decay = (0.6f - elapsedRealSec) / 0.6f;
                    float timeVal = elapsedRealSec * 85f; // ~85Hz crisp vibration
                    int shakeX = Mathf.RoundToInt((Mathf.Sin(timeVal * 1.0f) * 5.0f + Mathf.Sin(timeVal * 2.3f) * 3.0f) * decay);
                    drawX += shakeX;
                }

                Rect bubbleRect = new Rect(drawX, drawY, drawW, drawH);

                bool isLight = (settings?.BubbleTheme ?? RimTalkSettings.SpeechBubbleTheme.Dark) == RimTalkSettings.SpeechBubbleTheme.Light;
                Texture2D activeBg = isLight ? BubbleBgLight : BubbleBgDark;

                // 1. Draw rounded background (Anti-aliased 9-slice atlas in native integer pixels)
                if (activeBg != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, bgAlpha);
                    Widgets.DrawAtlas(bubbleRect, activeBg);
                }
                else
                {
                    Color fallbackBg = isLight
                        ? new Color(0.96f, 0.96f, 0.97f, bgAlpha)
                        : new Color(0.09f, 0.10f, 0.12f, bgAlpha);
                    Widgets.DrawBoxSolid(bubbleRect, fallbackBg);
                }

                // 2. Draw Emotion Reactive Glow / Rounded Border (Group Palette Preserved, dynamic toggle)
                Color borderColor = bubble.GetBorderColor(settings, isLight);
                Color finalBorderColor = new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * fadeAlpha);
                Texture2D activeBorder = settings?.BubbleBorderThickness switch
                {
                    RimTalkSettings.BorderThicknessMode.Thin => BubbleBorderThin ?? BubbleBorder,
                    RimTalkSettings.BorderThicknessMode.Thick => BubbleBorderThick ?? BubbleBorder,
                    _ => BubbleBorder
                };
                if (activeBorder != null)
                {
                    GUI.color = finalBorderColor;
                    Widgets.DrawAtlas(bubbleRect, activeBorder);
                }
                else
                {
                    GUI.color = finalBorderColor;
                    Widgets.DrawBox(bubbleRect, 1);
                }

                // 3. Render dialogue text (Proportional font for announcements, custom font size for normal)
                int customSize = Mathf.RoundToInt(bubble.IsAnnouncement ? baseFontSize * 1.25f : baseFontSize);
                Text.fontStyles[(int)targetFont].fontSize = customSize;

                Color textCol = bubble.IsDowned
                    ? (isLight ? new Color(0.42f, 0.44f, 0.47f, fadeAlpha) : new Color(0.72f, 0.74f, 0.77f, fadeAlpha))
                    : bubble.IsAnnouncement ? (isLight ? new Color(0.62f, 0.38f, 0.05f, fadeAlpha) : new Color(1.0f, 0.88f, 0.42f, fadeAlpha))
                    : isLight ? new Color(0f, 0f, 0f, fadeAlpha) : new Color(0.95f, 0.96f, 0.98f, fadeAlpha);

                GUI.color = textCol;
                Rect textRect = bubbleRect.ExpandedBy(0f, 2f);
                Widgets.Label(textRect, bubble.WrappedText ?? bubble.Text);
            }
        }
        finally
        {
            Text.fontStyles[(int)targetFont].fontSize = originalSize;
            Text.Anchor = originalAnchor;
            Text.Font = originalFont;
            GUI.color = originalColor;
        }
    }

    public static void RecomputeAllBubbleDimensions()
    {
        for (int i = 0; i < ActiveBubbles.Count; i++)
        {
            ActiveBubbles[i].ComputeDimensions();
        }
    }

    private static SpeechBubble GetPooledBubble()
    {
        for (int i = 0; i < Pool.Count; i++)
        {
            if (!Pool[i].Active) return Pool[i];
        }

        SpeechBubble newBubble = new SpeechBubble();
        Pool.Add(newBubble);
        return newBubble;
    }
}

[HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
public static class SpeechBubblePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        SpeechBubbleDrawer.DrawBubbles();
    }
}
