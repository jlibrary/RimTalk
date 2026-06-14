using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalk.Source.Data;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using Verse;
using Cache = RimTalk.Data.Cache;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service;

/// <summary>
/// Core service for generating and managing AI-driven conversations between pawns.
/// </summary>
public static class TalkService
{
    /// <summary>
    /// Initiates the process of generating a conversation. It performs initial checks and then
    /// starts a background task to handle the actual AI communication.
    /// </summary>
    public static bool GenerateTalk(TalkRequest talkRequest)
    {
        // Guard clauses to prevent generation when the feature is disabled or the AI service is busy.
        var settings = Settings.Get();
        if (!settings.IsEnabled || !CommonUtil.ShouldAiBeActiveOnSpeed()) return false;
        if (settings.GetActiveConfig() == null) return false;
        if (AIService.IsBusy()) return false;

        PawnState pawn1 = Cache.Get(talkRequest.Initiator);
        if (!talkRequest.TalkType.IsFromUser() && (pawn1 == null || !pawn1.CanGenerateTalk())) return false;
        
        if (!settings.AllowSimultaneousConversations && AnyPawnHasPendingResponses()) return false;

        // Ensure the recipient is valid and capable of talking.
        PawnState pawn2 = talkRequest.Recipient != null ? Cache.Get(talkRequest.Recipient) : null;
        if (pawn2 == null || talkRequest.Recipient?.Name == null || !pawn2.CanDisplayTalk())
        {
            talkRequest.Recipient = null;
        }

        List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(talkRequest.Initiator);
        if (talkRequest.Recipient.IsPlayer()) nearbyPawns.Insert(0, talkRequest.Recipient);
        var (status, isInDanger) = talkRequest.Initiator.GetPawnStatusFull(nearbyPawns);
        
        // Avoid spamming generations if the pawn's status hasn't changed recently.
        if (!talkRequest.TalkType.IsFromUser() && status == pawn1.LastStatus && pawn1.RejectCount < 2)
        {
            pawn1.RejectCount++;
            return false;
        }
        
        if (!talkRequest.TalkType.IsFromUser() && isInDanger) talkRequest.TalkType = TalkType.Urgent;
        
        pawn1.RejectCount = 0;
        pawn1.LastStatus = status;

        // Select the most relevant pawns for the conversation context.
        List<Pawn> pawns = new List<Pawn> { talkRequest.Initiator, talkRequest.Recipient }
            .Where(p => p != null)
            .Concat(nearbyPawns.Where(p =>
            {
                var pawnState = Cache.Get(p);
                return pawnState.CanDisplayTalk() && pawnState.TalkResponses.Empty();
            }))
            .Distinct()
            .Take(settings.Context.MaxPawnContextCount)
            .ToList();
        
        if (pawns.Count == 1) talkRequest.IsMonologue = true;

        if (!settings.AllowMonologue && talkRequest.IsMonologue && !talkRequest.TalkType.IsFromUser())
            return false;

        // Delegate prompt assembly to PromptManager (Handles Simple/Advanced modes and fallbacks)
        talkRequest.PromptMessages = PromptManager.Instance.BuildMessages(talkRequest, pawns, status);
        
        // Update prompt with the actual rendered content (important for Advanced Mode history)
        var extracted = PromptManager.ExtractUserPrompt(talkRequest.PromptMessages);
        if (!string.IsNullOrEmpty(extracted))
        {
            talkRequest.Prompt = extracted;
        }
        
        // Offload the AI request and processing to a background thread to avoid blocking the game's main thread.
        Task.Run(() => GenerateAndProcessTalkAsync(talkRequest));

        pawn1.MarkRequestSpoken(talkRequest);
        
        return true;
    }

    /// <summary>
    /// Handles the asynchronous AI streaming and processes the responses.
    /// </summary>
    private static async Task GenerateAndProcessTalkAsync(TalkRequest talkRequest)
    {
        var initiator = talkRequest.Initiator;
        try
        {
            Cache.Get(initiator).IsGeneratingTalk = true;
            
            var receivedResponses = new List<TalkResponse>();

            // Call the streaming chat service. The callback is executed as each piece of dialogue is parsed.
            await AIService.ChatStreaming(talkRequest, talkResponse =>
                {
                    Logger.Debug($"Streamed: {talkResponse}");

                    PawnState pawnState = Cache.GetByName(talkResponse.Name);
                    talkResponse.Name = pawnState.Pawn.LabelShort;

                    // Link replies to the previous message in the conversation.
                    if (receivedResponses.Any())
                    {
                        talkResponse.ParentTalkId = receivedResponses.Last().Id;
                    }

                    receivedResponses.Add(talkResponse);

                    // Enqueue the received talk for the pawn to display later.
                    pawnState.TalkResponses.Add(talkResponse);
                }
            );

            // Once the stream is complete, save the full conversation to history.
            AddResponsesToHistory(receivedResponses, talkRequest.Prompt);
        }
        catch (Exception ex)
        {
            Logger.Error(ex.StackTrace);
        }
        finally
        {
            Cache.Get(initiator).IsGeneratingTalk = false;
        }
    }

    /// <summary>
    /// Serializes the generated responses and adds them to the message history for all involved pawns.
    /// </summary>
    private static void AddResponsesToHistory(List<TalkResponse> responses, string prompt)
    {
        if (!responses.Any()) return;
        string serializedResponses = JsonUtil.SerializeToJson(responses);
        var uniquePawns = responses
            .Select(r => Cache.GetByName(r.Name)?.Pawn)
            .Where(p => p != null)
            .Distinct();

        foreach (var pawn in uniquePawns)
        {
            TalkHistory.AddMessageHistory(pawn, prompt, serializedResponses);
        }
    }

    /// <summary>
    /// Iterates through all pawns on each game tick to display any queued talks.
    /// </summary>
    public static void DisplayTalk()
    {
        var settings = Settings.Get();
        
        // Keep menu-blocking and pause behavior consistent across all call paths.
        if (Find.TickManager.Paused && !settings.SpeakWhilePaused) return;
        if (settings.StopSpeakingInMenus && IsSpeakingBlockedByMenu(settings.AdvancedMenuAvoidance)) return;

        foreach (Pawn pawn in Cache.Keys)
        {
            PawnState pawnState = Cache.Get(pawn);
            if (pawnState == null || pawnState.TalkResponses.Empty()) continue;

            var talk = pawnState.TalkResponses.First();
            if (talk == null)
            {
                pawnState.TalkResponses.RemoveAt(0);
                continue;
            }

            var apiLog = ApiHistory.GetApiLog(talk.Id);

            // Temporary display blocking (drafted/sleeping/off-map) should not immediately delete queued speech.
            if (!pawnState.CanDisplayTalk())
            {
                if (ShouldDropUndisplayable(apiLog, settings.IgnoreWaitSeconds))
                    pawnState.IgnoreTalkResponse();
                continue;
            }

            double replyInterval = settings.DisplayTalkInterval;
            if (pawn.IsInDanger())
            {
                replyInterval = Math.Min(replyInterval, 2);
                pawnState.IgnoreAllTalkResponses([TalkType.Urgent, TalkType.User]);
            }

            int parentTalkTick = TalkHistory.GetSpokenTick(talk.ParentTalkId);
            bool isWaiting = parentTalkTick == -1 ||
                             !CommonUtil.HasPassed(parentTalkTick, replyInterval, settings.AlignTimingToNormalSpeed);
            if (isWaiting)
            {
                if (!HasWaitedLongEnough(apiLog, settings.IgnoreWaitSeconds)) continue;

                if (!settings.ForceSpeakIgnored)
                {
                    pawnState.IgnoreTalkResponse();
                    continue;
                }
            }

            CreateInteraction(pawn, talk);
            MarkAsSpokenForScheduling(talk, apiLog);
            
            break; // Display only one talk per tick to prevent overwhelming the screen.
        }
    }

    /// <summary>
    /// Retrieves the text for a pawn's current talk. Called by the game's UI system.
    /// </summary>
    public static string GetTalk(Pawn pawn)
    {
        PawnState pawnState = Cache.Get(pawn);
        if (pawnState == null) return null;

        TalkResponse talkResponse = ConsumeTalk(pawnState);
        pawnState.LastTalkTick = GenTicks.TicksGame;

        return talkResponse.Text;
    }
    
    /// <summary>
    /// Calls AI service directly for debug purpose.
    /// </summary>
    public static void GenerateTalkDebug(TalkRequest talkRequest)
    {
        Task.Run(() => GenerateAndProcessTalkAsync(talkRequest));
    }

    /// <summary>
    /// Dequeues a talk and updates its history as either spoken or ignored.
    /// </summary>
    private static TalkResponse ConsumeTalk(PawnState pawnState)
    {
        // Failsafe check
        if (pawnState.TalkResponses.Empty()) 
            return new TalkResponse(TalkType.Other, null!, "");
        
        var talkResponse = pawnState.TalkResponses.First();
        pawnState.TalkResponses.Remove(talkResponse);
        TalkHistory.AddSpoken(talkResponse.Id);
        var apiLog = ApiHistory.GetApiLog(talkResponse.Id);
        if (apiLog != null)
            apiLog.SpokenTick = GenTicks.TicksGame;

        Overlay.NotifyLogUpdated();
        return talkResponse;
    }

    private static void CreateInteraction(Pawn pawn, TalkResponse talk)
    {
        // Create the interaction log entry, which triggers the display of the talk bubble in-game.
        InteractionDef intDef = DefDatabase<InteractionDef>.GetNamed("RimTalkInteraction");
        var recipient = talk.GetTarget() ?? pawn;
        var playLogEntryInteraction = new PlayLogEntry_RimTalkInteraction(intDef, pawn, recipient, null);

        Find.PlayLog.Add(playLogEntryInteraction);

        if (Settings.Get().ApplyMoodAndSocialEffects && pawn != recipient)
        {
            var interactionType = talk.GetInteractionType();
            var memory = interactionType.GetThoughtDef();
            if (memory != null)
            {
                recipient.needs?.mood?.thoughts?.memories?.TryGainMemory(memory, pawn);
                if (interactionType is InteractionType.Chat)
                {
                    pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(memory, recipient);
                }
            }
        }
    }

    private static bool AnyPawnHasPendingResponses()
    {
        return Cache.GetAll().Any(pawnState => pawnState.TalkResponses.Count > 0);
    }

    private static bool HasWaitedLongEnough(ApiLog apiLog, int waitSeconds)
    {
        return apiLog != null && (DateTime.Now - apiLog.Timestamp).TotalSeconds >= waitSeconds;
    }

    private static bool ShouldDropUndisplayable(ApiLog apiLog, int waitSeconds)
    {
        // Drop stale lines for non-displayable pawns using a tighter cap derived from ignore-wait.
        int dropAfterSeconds = Math.Min(waitSeconds * 2, 20);
        return apiLog != null && (DateTime.Now - apiLog.Timestamp).TotalSeconds >= dropAfterSeconds;
    }

    private static void MarkAsSpokenForScheduling(TalkResponse talk, ApiLog apiLog)
    {
        TalkHistory.AddSpoken(talk.Id);
        if (apiLog != null)
            apiLog.SpokenTick = GenTicks.TicksGame;
        Overlay.NotifyLogUpdated();
    }

    public static int IgnoreAllPendingTalks()
    {
        int ignoredCount = 0;
        foreach (var pawnState in Cache.GetAll())
        {
            if (pawnState == null) continue;
            while (pawnState.TalkResponses.Count > 0)
            {
                pawnState.IgnoreTalkResponse();
                ignoredCount++;
            }
        }
        return ignoredCount;
    }
    
    public static int ClearAllPendingTalksForce()
    {
        int clearedCount = 0;
        foreach (var pawnState in Cache.GetAll())
        {
            if (pawnState == null) continue;
            foreach (var response in pawnState.TalkResponses)
            {
                TalkHistory.AddIgnored(response.Id);
                var log = ApiHistory.GetApiLog(response.Id);
                if (log != null) log.SpokenTick = -1;
                clearedCount++;
            }
            pawnState.TalkResponses.Clear();
        }
        Overlay.NotifyLogUpdated();
        return clearedCount;
    }

    public static bool IsSpeakingBlockedByMenu(bool advancedMenuAvoidance)
    {
        WindowStack windowStack = Find.WindowStack;
        if (windowStack == null) return false;

        foreach (Window window in windowStack.Windows)
        {
            if (window.layer == WindowLayer.Dialog)
            {
                Type type = window.GetType();
                string ns = type.Namespace;
                string name = type.Name;
                if (ns == null || !ns.StartsWith("RimTalk") || (advancedMenuAvoidance && name == "CustomDialogueWindow"))
                    return true;
            }
            else if (advancedMenuAvoidance && !(window is MainTabWindow_Inspect) && window is MainTabWindow)
            {
                return true;
            }
        }
        return false;
    }
}
