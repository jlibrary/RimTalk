using System.Text;
using RimTalk.Source.Data;
using RimTalk.Util;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class SpeechBubble
{
    public const float BasePadding = 10f;
    private const float PaddingHorizontal = 10f;
    private const float PaddingVertical = 10f;
    public const float MinBubbleWidth = 50f;
    public const float MinBubbleHeight = 14f;
    public const float CornerClearance = 5f; // Clearance for 9-slice rounded corners
    private const float DefaultMaxWidth = 260f;
    public const float WidthSafetyBuffer = 4f; // Safe buffer with pre-wrapped text

    public Pawn Pawn;
    public string Text;
    public string WrappedText;
    public Vector2 BubbleSize;
    public int StartTick;
    public int ExpiryTick;
    public float StartRealTime;
    public bool IsUrgent;
    public bool IsAnnouncement;
    public bool IsAggressive;
    public bool IsDowned;
    public int ConversationId = -1;
    public bool Active;

    public void Init(Pawn pawn, string text, int conversationId = -1, InteractionType interactionType = InteractionType.None, TalkType talkType = TalkType.Other)
    {
        Pawn = pawn;
        Text = text ?? string.Empty;
        if (Text.IndexOf('\u00a0') >= 0)
            Text = Text.Replace('\u00a0', ' ');
        if (Text.IndexOf('\r') >= 0)
            Text = Text.Replace("\r\n", "\n").Replace('\r', '\n');
        WrappedText = Text;
        StartTick = GenTicks.TicksGame;
        StartRealTime = Time.realtimeSinceStartup;
        Active = true;
        ConversationId = conversationId;

        // Determine special contextual states
        IsDowned = pawn?.Downed ?? false;
        IsAnnouncement = talkType == TalkType.Announcement;
        IsAggressive = interactionType is InteractionType.Insult or InteractionType.Slight;
        // The bubble shakes only when the speaker is in personal danger (fire, combat, fleeing, heavy bleeding), never during peaceful social interactions
        IsUrgent = !IsDowned && interactionType == InteractionType.None && pawn != null && pawn.IsInDanger();

        var settings = Settings.Get();

        float scale = settings?.BubbleScale ?? 1f;
        float durationMultiplier = settings?.BubbleDurationMultiplier ?? 1f;

        // Announcements have extended duration for calm readability
        if (IsAnnouncement)
        {
            durationMultiplier *= 1.4f;
        }

        // Precompute dimensions and lock line breaks once to ensure zero text calculation per frame
        ComputeDimensions();

        // Compute lifetime in real seconds using CommonUtil.GetTicksForDuration so fast-forwarding game speed does not cut it off
        double baseSeconds = 5.0 + Mathf.Clamp(Text.Length * 0.05f, 0f, 6.0f);
        double totalSeconds = baseSeconds * Mathf.Max(0.5f, durationMultiplier);
        int totalTicks = Mathf.Max(120, CommonUtil.GetTicksForDuration(totalSeconds));
        ExpiryTick = StartTick + totalTicks;
    }

    public void ComputeDimensions()
    {
        var settings = Settings.Get();
        float scale = SpeechBubbleDrawer.GetEffectiveBubbleScale(settings);
        ComputeDimensions(scale);
    }

    public void ComputeDimensions(float scale)
    {
        float maxWidthBase = IsAnnouncement ? DefaultMaxWidth * 1.3f : DefaultMaxWidth;
        float cornerClearance = Mathf.Round(CornerClearance * scale);
        float softLineWidth = Mathf.Clamp((maxWidthBase - CornerClearance * 2f) * scale, 100f, 650f);

        var settings = Settings.Get();
        float baseFontSize = SpeechBubbleDrawer.GetEffectiveFontSize(settings);
        int customSize = Mathf.RoundToInt(IsAnnouncement ? baseFontSize * 1.25f : baseFontSize);

        GameFont prevFont = Verse.Text.Font;
        GameFont targetFont = GameFont.Small;
        int originalSize = Verse.Text.fontStyles[(int)targetFont].fontSize;

        try
        {
            Verse.Text.Font = targetFont;
            Verse.Text.fontStyles[(int)targetFont].fontSize = customSize;

            Vector2 rawSize = Verse.Text.CalcSize(Text);

            float padMult = settings?.BubblePadding ?? 0.5f;
            float pad = Mathf.Round(BasePadding * scale * padMult);
            float minW = Mathf.Round(MinBubbleWidth * scale);

            // 1. Single-line check: If text fits on 1 line and has no line breaks, lock it as 1 line permanently!
            if (Text.IndexOf('\n') < 0 && rawSize.x <= softLineWidth)
            {
                WrappedText = Text;
                float totalW = Mathf.Max(minW, rawSize.x + pad * 2f + cornerClearance * 2f);
                float visualH = rawSize.y - 4f * scale;
                float totalH = Mathf.Max(MinBubbleHeight, visualH + pad * 2f);
                BubbleSize = new Vector2(Mathf.Round(totalW), Mathf.Round(totalH));
                return;
            }

            // 2. Multi-line: Pre-wrap text with hard newline characters so pawn movement can NEVER cause line-wrap jitter!
            WrappedText = PreWrapText(Text, softLineWidth, out float maxMeasuredLineWidth);

            // Calculate exact height of the pre-wrapped text using generous measurement bounds
            float safeMeasureWidth = maxMeasuredLineWidth + 40f;
            float textHeight = Verse.Text.CalcHeight(WrappedText, safeMeasureWidth);

            int lineCount = 1;
            for (int i = 0; i < WrappedText.Length; i++)
            {
                if (WrappedText[i] == '\n') lineCount++;
            }
            float visualMultiH = textHeight - (3f * lineCount + 1f) * scale;

            float bubbleW = Mathf.Max(minW, maxMeasuredLineWidth + pad * 2f + cornerClearance * 2f);
            float bubbleH = Mathf.Max(MinBubbleHeight, visualMultiH + pad * 2f);
            BubbleSize = new Vector2(Mathf.Round(bubbleW), Mathf.Round(bubbleH));
        }
        finally
        {
            Verse.Text.fontStyles[(int)targetFont].fontSize = originalSize;
            Verse.Text.Font = prevFont;
        }
    }

    private static string PreWrapText(string text, float maxLineWidth, out float maxMeasuredLineWidth)
    {
        maxMeasuredLineWidth = 0f;
        if (string.IsNullOrEmpty(text)) return string.Empty;

        StringBuilder sb = new();
        string current = "";

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n')
            {
                sb.Append(current).Append('\n');
                maxMeasuredLineWidth = Mathf.Max(maxMeasuredLineWidth, Verse.Text.CalcSize(current).x);
                current = "";
                continue;
            }

            string candidate = current + c;
            if (Verse.Text.CalcSize(candidate).x <= maxLineWidth)
            {
                current = candidate;
                continue;
            }

            int lastSpace = current.LastIndexOf(' ');
            if (lastSpace > 0 && c < 0x2E80)
            {
                string line = current.Substring(0, lastSpace);
                sb.Append(line).Append('\n');
                maxMeasuredLineWidth = Mathf.Max(maxMeasuredLineWidth, Verse.Text.CalcSize(line).x);
                current = current.Substring(lastSpace + 1) + c;
            }
            else
            {
                bool isPunct = c is '、' or '。' or '！' or '？' or '!' or '?' or '.' or ',' or ')' or '」';
                if (isPunct && current.Length > 1)
                {
                    string line = current.Substring(0, current.Length - 1);
                    sb.Append(line).Append('\n');
                    maxMeasuredLineWidth = Mathf.Max(maxMeasuredLineWidth, Verse.Text.CalcSize(line).x);
                    current = current[current.Length - 1].ToString() + c;
                }
                else
                {
                    sb.Append(current).Append('\n');
                    maxMeasuredLineWidth = Mathf.Max(maxMeasuredLineWidth, Verse.Text.CalcSize(current).x);
                    current = c.ToString();
                }
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            sb.Append(current);
            maxMeasuredLineWidth = Mathf.Max(maxMeasuredLineWidth, Verse.Text.CalcSize(current).x);
        }

        return sb.ToString();
    }

    public Color GetBorderColor(RimTalkSettings settings, bool isLight)
    {
        if (IsDowned)
        {
            return new Color(0.55f, 0.58f, 0.62f, 1f); // Muted faint slate
        }

        bool useColors = settings?.BubbleUseColors ?? true;
        if (useColors)
        {
            Color groupCol = UIUtil.GetConversationColor(ConversationId);
            if (groupCol != Color.clear)
            {
                return new Color(groupCol.r, groupCol.g, groupCol.b, 1f);
            }
        }

        return isLight ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
    }

    public void Deactivate()
    {
        Active = false;
        Pawn = null;
        Text = null;
        WrappedText = null;
        IsUrgent = false;
        IsAnnouncement = false;
        IsAggressive = false;
        IsDowned = false;
        ConversationId = -1;
        StartRealTime = 0f;
    }
}
