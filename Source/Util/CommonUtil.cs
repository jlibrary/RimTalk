using System;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using Vector2 = UnityEngine.Vector2;

namespace RimTalk.Util;

public static class CommonUtil
{
    public static bool HasPassed(int pastTick, double seconds)
    {
        return GenTicks.TicksGame - pastTick >= GetTicksForDuration(seconds);
    }
    public static int GetTicksForDuration(double seconds)
    {
        var tickRate = GetCurrentTickRate();
        return (int)(seconds * tickRate);
    }

    private static int GetCurrentTickRate()
    {
        switch (Find.TickManager.CurTimeSpeed)
        {
            case TimeSpeed.Paused:
                return 0;
            case TimeSpeed.Normal:
                return 60;
            case TimeSpeed.Fast:
                return 180;
            case TimeSpeed.Superfast:
                return 360;
            case TimeSpeed.Ultrafast:
                return 1500;
            default:
                return 60; // Default to normal speed if unknown
        }
    }

    // Struct containing all necessary in-game data
    public struct InGameData
    {
        public string Hour12HString;
        public string DateString;
        public string SeasonString;
        public string WeatherString;
    }

    public static InGameData GetInGameData()
    {
        // Default values in case data is invalid
        InGameData mapData = new InGameData
            { Hour12HString = "N/A", DateString = "N/A", SeasonString = "N/A", WeatherString = "N/A" };

        try
        {
            // Basic condition check
            Map currentMap = Find.CurrentMap;
            if (currentMap?.Tile == null)
            {
                return mapData; // Return default value if invalid
            }

            // Perform redundant calculations only once beforehand
            long absTicks = Find.TickManager.TicksAbs;
            Vector2 longLat = Find.WorldGrid.LongLatOf(currentMap.Tile);

            // Store various information in the struct
            mapData.Hour12HString = GetInGameHour12HString(absTicks, longLat);
            mapData.DateString = GetInGameDateString(absTicks, longLat);
            mapData.SeasonString = GetInGameSeasonString(absTicks, longLat);
            mapData.WeatherString = GetInGameWeatherString(currentMap);

            return mapData;
        }
        catch (Exception)
        {
            // Return default data in case of an exception
            return new InGameData
                { Hour12HString = "N/A", DateString = "N/A", SeasonString = "N/A", WeatherString = "N/A" };
        }
    }

    // No path for null to occur as it only receives values and calculates. Changed from int? to int
    public static int GetInGameHour(long absTicks, Vector2 longLat)
    {
        return GenDate.HourOfDay(absTicks, longLat.x);
    }

    // Converts 24h to 12h format with am/pm suffix
    public static string GetInGameHour12HString(long absTicks, Vector2 longLat)
    {
        int hour24 = GetInGameHour(absTicks, longLat);

        int hour12 = hour24 % 12;
        if (hour12 == 0)
        {
            hour12 = 12;
        }

        string ampm = hour24 < 12 ? "am" : "pm";
        return $"{hour12}{ampm}";
    }

    // Overload for Map parameter - convenience method for MustacheParser
    public static string GetInGameHour12HString(Map map)
    {
        if (map == null) return "";
        long absTicks = Find.TickManager.TicksAbs;
        Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
        return GetInGameHour12HString(absTicks, longLat);
    }

    // Returns the year, quarter, and day.
    private static string GetInGameDateString(long absTicks, Vector2 longLat)
    {
        return GenDate.DateFullStringAt(absTicks, longLat);
    }

    private static string GetInGameSeasonString(long absTicks, Vector2 longLat)
    {
        return GenDate.Season(absTicks, longLat).Label();
    }

    private static string GetInGameWeatherString(Map currentMap)
    {
        return currentMap.weatherManager?.curWeather?.label ?? "N/A";
    }

    // Simple token estimation algorithm (approximate)
    public static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        // More accurate estimation for modern tokenizers like Gemma
        // Modern tokenizers use subword tokenization (BPE/SentencePiece)

        // Remove extra whitespace and normalize
        string normalizedText = Regex.Replace(text.Trim(), @"\s+", " ");

        double totalTokens = 0.0;
        string[] words = normalizedText.Split(new char[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string word in words)
        {
            // Clean word of leading/trailing punctuation for length calculation
            string cleanWord = word.Trim('!', '?', '.', ',', ':', ';', '"', '\'', '(', ')', '[', ']', '{', '}');

            if (cleanWord.Length == 0)
            {
                // Pure punctuation word
                totalTokens += 1.0;
            }
            else if (cleanWord.Length <= 3)
            {
                // Short words are usually 1 token, plus punctuation if present
                totalTokens += 1.0;
                if (cleanWord.Length != word.Length) totalTokens += 0.5; // Attached punctuation
            }
            else if (cleanWord.Length <= 6)
            {
                // Medium words: roughly 1-1.5 tokens
                totalTokens += 1.0;
                if (cleanWord.Length > 4) totalTokens += 0.5;
                if (cleanWord.Length != word.Length) totalTokens += 0.5; // Attached punctuation
            }
            else
            {
                // Long words: modern tokenizers break these into subwords
                // Estimate ~3.5 characters per token for long sequences
                totalTokens += Math.Max(1.0, Math.Ceiling(cleanWord.Length / 3.5));
                if (cleanWord.Length != word.Length) totalTokens += 0.5; // Attached punctuation
            }
        }

        // Add small overhead for special tokens and formatting, but less aggressive
        totalTokens += Math.Max(1.0, totalTokens * 0.02);

        // Round up and convert to int
        return Math.Max(1, (int)Math.Ceiling(totalTokens));
    }


    // Calculate max allowed tokens based on cooldown
    public static int GetMaxAllowedTokens(int cooldownSeconds)
    {
        return Math.Min(80 * cooldownSeconds, 800);
    }


    public static bool ShouldAiBeActiveOnSpeed()
    {
        RimTalkSettings settings = Settings.Get();
        if (settings.DisableAiAtSpeed == 0)
            return true;
        TimeSpeed currentGameSpeed = Find.TickManager.CurTimeSpeed;
        return (int)currentGameSpeed < settings.DisableAiAtSpeed;
    }
    
    public static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }
    
    public static string StripFormattingTags(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
    
        // Remove common RimWorld rich text tags
        text = Regex.Replace(text, @"<color[^>]*>|</color>", string.Empty);
        text = Regex.Replace(text, @"<b>|</b>", string.Empty);
        text = Regex.Replace(text, @"<i>|</i>", string.Empty);
        text = Regex.Replace(text, @"<size[^>]*>|</size>", string.Empty);
    
        return text;
    }
}