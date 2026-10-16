using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Error;

public static class AIErrorHandler
{
    private static bool _quotaWarningShown;
    private static int _pendingGenerationFailures;
    private static readonly ConcurrentQueue<Action> PendingMessages = new();

    public static void EnqueueMessage(Action action)
    {
        if (action != null)
        {
            PendingMessages.Enqueue(action);
        }
    }

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
            // If request had an image and failed, retry once silently without the image
            var currentReq = AIService.CurrentRequest;
            if (!string.IsNullOrEmpty(currentReq?.ImageBase64))
            {
                currentReq.ImageBase64 = null;
                Logger.Warning($"Request with image failed ({ex.Message}). Retrying without image as fallback...");
                try
                {
                    var result = await operation();
                    ShowVisionFallbackMessage(Settings.Get()?.GetCurrentModel());
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception retryNoImageEx)
                {
                    Logger.Warning($"Fallback retry without image also failed: {retryNoImageEx.Message}");
                }
            }

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception retryEx)
            {
                Logger.Warning($"Retry failed: {retryEx.Message}");
                HandleFinalFailure(ex); // Show the original error logic
                onFailure?.Invoke(retryEx);
                return default;
            }
            finally
            {
                // Reset so the next request tries the primary model again
                if (settings.UseSimpleConfig)
                    settings.IsUsingFallbackModel = false;
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
        // The stack trace alone cannot tell a connection timeout from a read timeout,
        // because only the message says which one it was.
        Logger.Warning($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        if (IsHiddenServerError(ex)) return;
        ReportGenerationFailure(ex.Message);
    }

    public static bool TryHandlePromptFailure(Data.TalkRequest talkRequest, Data.PawnState pawnState)
    {
        string lastError = Prompt.ScribanParser.LastError;
        if (talkRequest?.PromptMessages is { Count: > 0 })
        {
            if (!string.IsNullOrEmpty(lastError))
                ReportGenerationFailure(lastError);
            return false;
        }

        string errorMsg = lastError ?? "Prompt rendering produced no valid messages.";
        var apiLog = Data.ApiHistory.AddRequest(talkRequest, Source.Data.Channel.Stream);
        apiLog.IsError = true;
        apiLog.Response = errorMsg;
        apiLog.Payload = new Client.Payload("Unknown", Settings.Get()?.GetCurrentModel() ?? "Unknown", "", null, 0, errorMsg);
        ReportGenerationFailure(errorMsg);
        pawnState?.MarkRequestSpoken(talkRequest);
        return true;
    }

    private static void ReportGenerationFailure(string errorMessage)
    {
        // One message per drain, however many failed. PendingMessages is drained once per
        // frame, so failures that land between drains are shown together. Every failure is
        // still logged above; this only collapses what the player sees.
        if (Interlocked.Increment(ref _pendingGenerationFailures) > 1) return;

        PendingMessages.Enqueue(() =>
        {
            int failures = Interlocked.Exchange(ref _pendingGenerationFailures, 0);
            string message = $"{"RimTalk.TalkService.GenerationFailed".Translate()}: {errorMessage}";
            if (failures > 1) message = $"{message} (x{failures})";
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
        });
    }

    private static bool IsHiddenServerError(Exception ex)
    {
        int? status = (ex as AIRequestException)?.Payload?.StatusCode;
        return status >= 500 && Settings.Get()?.HideServerErrorNotifications == true;
    }

    private static void ShowRetryMessage(Exception ex, string nextModel)
    {
        if (IsHiddenServerError(ex)) return;
        PendingMessages.Enqueue(() =>
        {
            string messageKey = ex is QuotaExceededException ? "RimTalk.TalkService.QuotaReached" : "RimTalk.TalkService.APIError";
            string message = $"{messageKey.Translate()}. {"RimTalk.TalkService.TryingNextAPI".Translate(nextModel)}";
            Messages.Message(message, MessageTypeDefOf.NeutralEvent, false);
        });
    }

    private static void ShowVisionFallbackMessage(string model)
    {
        PendingMessages.Enqueue(() =>
        {
            string modelName = string.IsNullOrEmpty(model) ? "Unknown" : model;
            string message = "RimTalk.TalkService.VisionUnsupportedFallback".Translate(modelName);
            Messages.Message(message, MessageTypeDefOf.CautionInput, false);
        });
    }
}
