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
        if (talkRequest.TalkType != TalkType.User && (pawn1 == null || !pawn1.CanGenerateTalk())) return false;
        
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
        if (talkRequest.TalkType != TalkType.User && status == pawn1.LastStatus && pawn1.RejectCount < 2)
        {
            pawn1.RejectCount++;
            return false;
        }
        
        if (talkRequest.TalkType != TalkType.User && isInDanger) talkRequest.TalkType = TalkType.Urgent;
        
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

        if (!settings.AllowMonologue && talkRequest.IsMonologue && talkRequest.TalkType != TalkType.User)
            return false;

        // Build the context and decorate the prompt with current status information.
        talkRequest.Context = PromptService.BuildContext(pawns);
        PromptService.DecoratePrompt(talkRequest, pawns, status);
        
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

            // Collect all pawns for context (initiator + recipient + nearby)
            var allPawns = new List<Pawn> { initiator };
            if (talkRequest.Recipient != null) allPawns.Add(talkRequest.Recipient);
            
            // Add nearby pawns from the original request's context
            var nearbyPawns = PawnSelector.GetAllNearByPawns(initiator);
            foreach (var p in nearbyPawns.Where(p => !allPawns.Contains(p)))
            {
                var pawnState = Cache.Get(p);
                if (pawnState?.CanDisplayTalk() == true && pawnState.TalkResponses.Empty())
                {
                    allPawns.Add(p);
                    if (allPawns.Count >= Settings.Get().Context.MaxPawnContextCount) break;
                }
            }

            // Build prompt messages using new PromptManager system
            var context = MustacheContext.FromTalkRequest(talkRequest, allPawns);
            
            // Set dialogue type and status from the already-computed prompt
            context.DialogueType = GetDialogueTypeDescription(talkRequest, allPawns);
            context.DialogueStatus = talkRequest.Prompt;
            
            // Set chat history in context (roles are now System/Assistant instead of User/Assistant)
            context.ChatHistory = TalkHistory.GetMessageHistory(initiator);
            
            var promptMessages = PromptManager.Instance.BuildPromptMessages(context);
            
            // Convert PromptRole to Role and build prefix messages list
            var prefixMessages = new List<(Role role, string message)>();
            foreach (var (promptRole, content) in promptMessages)
            {
                var role = promptRole switch
                {
                    PromptRole.System => Role.System,
                    PromptRole.User => Role.User,
                    PromptRole.Assistant => Role.AI,
                    _ => Role.User
                };
                prefixMessages.Add((role, content));
            }
            
            // Fallback to legacy instruction if new system returns empty
            if (prefixMessages.Count == 0)
            {
                // Use legacy instruction with context appended
                prefixMessages.Add((Role.System, $"{Constant.Instruction}\n{talkRequest.Context}"));
            }

            // Call the streaming chat service. The callback is executed as each piece of dialogue is parsed.
            // Chat history is now included in prefixMessages via BuildPromptMessages, so pass empty list
            await AIService.ChatStreaming(
                talkRequest,
                prefixMessages,
                new List<(Role role, string message)>(),  // History already included in prefixMessages
                talkResponse =>
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
        foreach (var talkResponse in responses)
        {
            Pawn pawn = Cache.GetByName(talkResponse.Name)?.Pawn;
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

            if (!talk.IsReply() && !CommonUtil.HasPassed(pawnState.LastTalkTick, Settings.Get().TalkInterval))
            {
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

    /// <summary>
    /// Generates a description of the dialogue type for mustache template
    /// </summary>
    private static string GetDialogueTypeDescription(TalkRequest request, List<Pawn> pawns)
    {
        var mainPawn = pawns[0];
        var shortName = mainPawn.LabelShort;
        
        if (request.TalkType == TalkType.User && pawns.Count > 1)
        {
            var mode = Settings.Get().PlayerDialogueMode;
            if (mode == Settings.PlayerDialogueMode.Manual)
                return $"{pawns[1].LabelShort}({pawns[1].GetRole()}) said to '{shortName}'. Generate dialogue starting after this. Do not generate any further lines for {pawns[1].LabelShort}";
            else
                return $"{pawns[1].LabelShort}({pawns[1].GetRole()}) said to '{shortName}'. Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {mainPawn.LabelShort}";
        }
        
        if (pawns.Count == 1)
        {
            return $"{shortName} short monologue";
        }
        
        if (mainPawn.IsInCombat() || mainPawn.GetMapRole() == MapRole.Invading)
        {
            return mainPawn.IsSlave || mainPawn.IsPrisoner
                ? $"{shortName} dialogue short (worry)"
                : $"{shortName} dialogue short, urgent tone ({mainPawn.GetMapRole().ToString().ToLower()}/command)";
        }
        
        return $"{shortName} starts conversation, taking turns";
    }
}