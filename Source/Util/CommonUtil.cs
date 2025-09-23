using System;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace RimTalk.Util
{
    public static class CommonUtil
    {
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
        
        public static int? GetInGameHour()
        {
            try
            {
                if (Find.CurrentMap?.Tile == null)
                    return null;
        
                return GenDate.HourOfDay(Find.TickManager.TicksAbs, 
                    Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile).x);
            }
            catch (Exception)
            {
               return null;
            }
        }

        public static string GetInGameHour12HString()
        {
            int? hour24 = GetInGameHour();
            if (!hour24.HasValue)
                return "N/A";
    
            int hour12 = hour24.Value % 12;
            if (hour12 == 0)
            {
                hour12 = 12;
            }
            string ampm = hour24.Value < 12 ? "am" : "pm";
            return $"{hour12}{ampm}";
        }
        
        // Returns the year, quarter, and day.
        public static string GetInGameDateString()
        {
            try
            {
                if (Find.CurrentMap?.Tile == null)
                    return "N/A";
                
                return GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile));
            }
            catch (Exception)
            {
                return "N/A";
            }
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
    }
}