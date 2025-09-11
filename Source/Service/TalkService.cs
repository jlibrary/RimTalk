using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Cache = RimTalk.Data.Cache;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Service
{
    public static class TalkService
    {
        public static bool GenerateTalk(string prompt, Pawn initiator, Pawn recipient = null)
        {
            var settings = Settings.Get();
            if (!settings.IsEnabled) return false;
            if (!Bubbles.Settings.Activated || settings.GetActiveConfig() == null) return false;
            if (AIService.IsBusy()) return false;

            PawnState pawn1 = Cache.Get(initiator);
            PawnState pawn2 = Cache.Get(recipient);
            
            if (pawn1 == null || !pawn1.CanGenerateTalk()) return false;
            
            if (pawn2 == null || recipient?.Name == null || !pawn2.CanGenerateTalk())
                recipient = null;
            
            // avoid generation if pawn status did not change
            if (prompt == pawn1.LastStatus && pawn1.RejectCount < 2)
            {
                pawn1.RejectCount++;
                return false;
            }
            pawn1.RejectCount = 0;
            pawn1.LastStatus = PawnService.GetPawnStatusFull(initiator);

            List<Pawn> pawns = new List<Pawn> { initiator, recipient }.Where(p => p != null).ToList();
            
            // build generic context for pawns
            string context = PromptService.BuildContext(pawns);
            AIService.UpdateContext(context);
            
            // add current status
            prompt = PromptService.DecoratePrompt(prompt, initiator, recipient, pawn1.LastStatus);
            
            var request = new TalkRequest(prompt, initiator, recipient);
            
            Task.Run(async () =>
            {
                try
                {
                    Cache.Get(initiator).IsGeneratingTalk = true;
                    List<TalkResponse> talkResponses = await AIService.Chat(request);
                    ProcessSuccessfulResponse(pawns, talkResponses);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.StackTrace);
                }
                finally
                {
                    Cache.Get(initiator).IsGeneratingTalk = false;
                }
            });

            return true;
        }

        public static bool GenerateTalk(TalkRequest talkRequest)
        {
            if (GenerateTalk(talkRequest.Prompt, talkRequest.Initiator, talkRequest.Recipient))
            {
                Cache.Get(talkRequest.Initiator).TalkRequest = null;
                return true;
            }
            return false;
        }

        public static bool GenerateTalkFromPool(Pawn pawn)
        {
            var talkRequest = TalkRequestPool.Peek();
            talkRequest.Initiator = pawn;
            if (!GenerateTalk(talkRequest)) return false;
            TalkRequestPool.Remove(talkRequest);
            return true;
        }

        private static void ProcessSuccessfulResponse(List<Pawn> pawns, List<TalkResponse> talkResponses)
        {
            try
            {
                for (int i = 0; i < talkResponses.Count; i++)
                {
                    PawnState pawnState = Cache.GetByName(talkResponses[i].Name) ?? Cache.Get(pawns[i]);
                    pawnState.TalkQueue.Enqueue(talkResponses[i]);
                    talkResponses[i].Id = Guid.NewGuid();
                    talkResponses[i].Name = pawnState.Pawn.Name.ToStringShort;
                    if (i > 0)
                    {
                        talkResponses[i].ReplyToTalkId = talkResponses[i - 1].Id;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to process response: {ex.StackTrace}");
            }
        }

        // display generated talks
        public static void DisplayTalk()
        {
            foreach (Pawn pawn in Cache.Keys)
            {
                PawnState pawnState = Cache.Get(pawn);

                if (pawnState == null || pawnState.TalkQueue.Empty()) continue;

                var talk = pawnState.TalkQueue.Peek();
                if (talk == null)
                {
                    pawnState.TalkQueue.Dequeue();
                    continue;
                }

                // if reply, wait for ReplyInterval (3s)
                int replyToTalkTick = TalkHistory.Get(talk.ReplyToTalkId);
                if (replyToTalkTick == -1 || Find.TickManager.TicksGame - replyToTalkTick
                    < CommonUtil.GetTicksForDuration(pawnState.ReplyInterval)) continue;

                // if pawn is not able to talk, skip it
                if (!pawnState.CanDisplayTalk())
                {
                    ConsumeTalk(pawnState);
                    continue;
                }

                InteractionDef intDef = DefDatabase<InteractionDef>.GetNamed("RimTalkInteraction");

                PlayLogEntry_Interaction playLogEntryInteraction =
                    new PlayLogEntry_RimTalkInteraction(intDef, pawn, null, null);

                Find.PlayLog.Add(playLogEntryInteraction);
                break;
            }
        }

        public static string GetTalk(Pawn pawn)
        {
            PawnState pawnState = Cache.Get(pawn);
            if (pawnState == null) return null;
            
            TalkResponse talkResponse = ConsumeTalk(pawnState);
            
            pawnState.LastTalkTick = Find.TickManager.TicksGame;

            return talkResponse.Text;
        }

        private static TalkResponse ConsumeTalk(PawnState pawnState)
        {
            TalkResponse talkResponse = pawnState.TalkQueue.Dequeue();
            TalkHistory.AddTalk(talkResponse.Id);
            return talkResponse;
        }
    }
}