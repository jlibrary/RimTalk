using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error;

public static class AIErrorHandler
{
    private static bool _quotaWarningShown;
    private static readonly ConcurrentQueue<Action> PendingMessages = new();

    public static void DrainPendingMessages()
    {
        while (PendingMessages.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex) // Don't let one bad message stop the rest of the queue draining
            {
                Logger.Warning($"Failed to display queued message: {ex.Message}");
            }
        }
    }

    public static async Task<T> HandleWithRetry<T>(Func<Task<T>> operation, Action<Exception> onFailure = null)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var settings = Settings.Get();
            if (!CanRetryGeneration(settings))
            {
                HandleFinalFailure(ex);
                onFailure?.Invoke(ex);
                return default;
            }

            // Prepare for retry
            var nextModel = settings.GetCurrentModel();
            if (!settings.UseSimpleConfig)
            {
                ShowRetryMessage(ex, nextModel);
            }

            try
            {
                return await operation();
            }
            catch (Exception retryEx)
            {
                Logger.Warning($"Retry failed: {retryEx.Message}");
                HandleFinalFailure(ex); // Show the original error logic
                onFailure?.Invoke(retryEx);
                return default;
            }
        }
    }

    private static bool CanRetryGeneration(RimTalkSettings settings)
    {
        if (settings.UseSimpleConfig)
        {
            if (settings.IsUsingFallbackModel) return false;
            settings.IsUsingFallbackModel = true;
            return true;
        }

        if (!settings.UseCloudProviders) return false;
        int originalIndex = settings.CurrentCloudConfigIndex;
        settings.TryNextConfig();
        return settings.CurrentCloudConfigIndex != originalIndex;

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
        _quotaWarningShown = false;
    }

    private static void ShowQuotaWarning(Exception ex)
    {
        if (!_quotaWarningShown)
        {
            _quotaWarningShown = true;
            Logger.Warning(ex.Message);
            PendingMessages.Enqueue(() =>
            {
                string message = "RimTalk.TalkService.QuotaReached".Translate();
                Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
            });
        }
    }

    private static void ShowGenerationWarning(Exception ex)
    {
        Logger.Warning(ex.StackTrace);
        PendingMessages.Enqueue(() =>
        {
            string message = $"{"RimTalk.TalkService.GenerationFailed".Translate()}: {ex.Message}";
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
        });
    }

    private static void ShowRetryMessage(Exception ex, string nextModel)
    {
        PendingMessages.Enqueue(() =>
        {
            string messageKey = ex is QuotaExceededException ? "RimTalk.TalkService.QuotaReached" : "RimTalk.TalkService.APIError";
            string message = $"{messageKey.Translate()}. {"RimTalk.TalkService.TryingNextAPI".Translate(nextModel)}";
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
        });
    }
}
