using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error
{
    public static class TalkErrorHandler
    {
        public static bool QuotaWarningShown;

        public static async Task<string> HandleGenerationException(Exception ex, List<Pawn> pawns, string prompt)
        {
            var settings = Settings.Get();
            
            // Check if we can try a fallback/retry
            bool canRetry = CanRetryWithFallback(settings);
            
            if (canRetry)
            {
                return await TryFallbackGeneration(ex, pawns, prompt, settings);
            }
            else
            {
                ShowFinalWarning(ex);
                Logger.Warning($"{prompt}\n{null}");
                return null;
            }
        }

        public static void ResetQuotaWarning()
        {
            QuotaWarningShown = false;
        }

        private static bool CanRetryWithFallback(CurrentWorkDisplayModSettings settings)
        {
            if (settings.useSimpleConfig)
            {
                return !settings.isUsingFallbackModel;
            }
            else if (settings.useCloudProviders)
            {
                int originalIndex = settings.currentCloudConfigIndex;
                settings.TryNextConfig();
                return settings.currentCloudConfigIndex != originalIndex;
            }
            
            return false;
        }

        private static async Task<string> TryFallbackGeneration(Exception ex, List<Pawn> pawns, string prompt, CurrentWorkDisplayModSettings settings)
        {
            // Set fallback state for simple config
            if (settings.useSimpleConfig)
            {
                settings.isUsingFallbackModel = true;
            }
            else
            {
                // Show retry message
                ShowRetryMessage(ex, settings.GetCurrentModel());
            }

            try
            {
                return await GenerateWithAI(pawns, prompt);
            }
            catch (Exception retryEx)
            {
                Logger.Warning($"Retry failed: {retryEx.Message}");
                ShowFinalWarning(ex);
                Logger.Warning($"{prompt}\n{null}");
                return null;
            }
        }

        private static async Task<string> GenerateWithAI(List<Pawn> pawns, string prompt)
        {
            // This delegates back to TalkService.Generate - we need to make that method internal
            // For now, we'll duplicate the AI call logic to avoid circular dependencies
            string response;
            if (AIService.IsFirstInstruction())
                prompt += $" in {Constant.Lang}";

            Cache.Get(pawns[0]).IsGeneratingTalk = true;
            response = await AIService.Chat(prompt);
            
            return response;
        }

        private static void ShowRetryMessage(Exception ex, string nextModel)
        {
            string messageKey = ex is QuotaExceededException ? "RimTalk.TalkService.QuotaReached" : "RimTalk.TalkService.APIError";
            string message = $"{messageKey.Translate()}. {"RimTalk.TalkService.TryingNextAPI".Translate(nextModel)}";
            Messages.Message(message, MessageTypeDefOf.NegativeEvent, false);
        }
 
        private static void ShowFinalWarning(Exception ex)
        {
            if (ex is QuotaExceededException)
            {
                if (!QuotaWarningShown)
                {
                    QuotaWarningShown = true;
                    string message = "RimTalk.TalkService.QuotaExceeded".Translate();
                    Messages.Message(message, MessageTypeDefOf.NegativeEvent, false);
                    Logger.Warning("Quota exceeded");
                }
            }
            else
            {
                ShowGenerationWarning(ex);
            }
        }

        private static void ShowGenerationWarning(Exception ex)
        {
            Logger.Warning(ex.Message);
            string message = $"{"RimTalk.TalkService.GenerationFailed".Translate()}: {ex.Message}";
            Messages.Message(message, MessageTypeDefOf.NegativeEvent, false);
        }

    }
}