using System;
using System.Threading.Tasks;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error;

public static class AIErrorHandler
{
    public static bool QuotaWarningShown;

    public static async Task<T> HandleWithRetry<T>(Func<Task<T>> operation)
    {
        try
        {
            T result = await operation();
            return result;
        }
        catch (Exception ex)
        {
            var settings = Settings.Get();
            if (CanRetryGeneration(settings))
            {
                string nextModel = Settings.Get().GetCurrentModel();
                if (!settings.UseSimpleConfig)
                {
                    ShowRetryMessage(ex, nextModel);
                }

                try
                {
                    T result = await operation();
                    return result;
                }
                catch (Exception retryEx)
                {
                    Logger.Warning($"Retry failed: {retryEx.Message}");
                    HandleFinalFailure(ex);
                    return default(T);
                }
            }
            else
            {
                HandleFinalFailure(ex);
                return default(T);
            }
        }
    }

    private static bool CanRetryGeneration(RimTalkSettings settings)
    {
        if (settings.UseSimpleConfig)
        {
            return !settings.IsUsingFallbackModel;
        }
        else if (settings.UseCloudProviders)
        {
            int originalIndex = settings.CurrentCloudConfigIndex;
            settings.TryNextConfig();
            return settings.CurrentCloudConfigIndex != originalIndex;
        }
            
        return false;
    }

    private static void HandleFinalFailure(Exception ex)
    {
        if (ex is QuotaExceededException)
        {
            ShowQuotaWarning(ex);
        }
        else
        {
            ShowGenerationWarning(ex);
        }
    }

    public static void ResetQuotaWarning()
    {
        QuotaWarningShown = false;
    }

    public static void ShowQuotaWarning(Exception ex)
    {
        if (!QuotaWarningShown)
        {
            QuotaWarningShown = true;
            string message = "RimTalk.TalkService.QuotaExceeded".Translate();
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
            Logger.Warning(ex.Message);
        }
    }

    public static void ShowGenerationWarning(Exception ex)
    {
        Logger.Warning(ex.StackTrace);
        string message = $"{"RimTalk.TalkService.GenerationFailed".Translate()}: {ex.Message}";
        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
    }

    public static void ShowRetryMessage(Exception ex, string nextModel)
    {
        string messageKey = ex is QuotaExceededException ? "RimTalk.TalkService.QuotaReached" : "RimTalk.TalkService.APIError";
        string message = $"{messageKey.Translate()}. {"RimTalk.TalkService.TryingNextAPI".Translate(nextModel)}";
        Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
    }
}