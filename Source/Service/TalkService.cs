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

        // Build the context and decorate the prompt with current status information.
        talkRequest.Context = PromptService.BuildContext(pawns);
        // Get dialogue type BEFORE DecoratePrompt modifies talkRequest.Prompt
        string dialogueTypeString = MustacheContextProvider.GetDialogueTypeString(talkRequest, pawns);
        PromptService.DecoratePrompt(talkRequest, pawns, status);
        
        // Build prompt messages using PromptManager (encapsulates all Mustache variable collection)
        talkRequest.PromptMessages = PromptManager.Instance.PreparePromptForRequest(
            talkRequest, pawns, status, dialogueTypeString);
        
        // Fallback to legacy instruction if new system returns empty
        if (talkRequest.PromptMessages == null || talkRequest.PromptMessages.Count == 0)
        {
            talkRequest.PromptMessages = new List<(Role role, string content)>
            {
                (Role.System, $"{Constant.Instruction}\n{talkRequest.Context}")
            };
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

            // Skip this talk if its parent was ignored or the pawn is currently unable to speak.
            if (TalkHistory.IsTalkIgnored(talk.ParentTalkId) || !pawnState.CanDisplayTalk())
            {
                pawnState.IgnoreTalkResponse();
                continue;
            }

            int replyInterval = RimTalkSettings.ReplyInterval;
            if (pawn.IsInDanger())
            {
                replyInterval = 2;
                pawnState.IgnoreAllTalkResponses([TalkType.Urgent, TalkType.User]);
            }

            // Enforce a delay for replies to make conversations feel more natural.
            int parentTalkTick = TalkHistory.GetSpokenTick(talk.ParentTalkId);
            if (parentTalkTick == -1 || !CommonUtil.HasPassed(parentTalkTick, replyInterval)) continue;

            CreateInteraction(pawn, talk);
            
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
}