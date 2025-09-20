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
            if (!settings.IsEnabled || !CommonUtil.ShouldAiBeActiveOnSpeed()) return false;
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
            List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(initiator);
            pawn1.LastStatus = PawnService.GetPawnStatusFull(initiator, nearbyPawns);

            List<Pawn> pawns = new List<Pawn> { initiator, recipient }.Where(p => p != null).ToList();
            
            // build generic context for pawns
            string context = PromptService.BuildContext(pawns);
            AIService.UpdateContext(context);
            
            
            // add current status
            prompt = PromptService.DecoratePrompt(prompt, initiator, recipient, pawn1.LastStatus);
            
            var talkRequest = new TalkRequest(prompt, initiator, recipient);
            
            Task.Run(async () =>
            {
                try
                {
                    Cache.Get(initiator).IsGeneratingTalk = true;
                    var talkResponses = await AIService.Chat(talkRequest, TalkHistory.GetMessageHistory(initiator));
                    ProcessSuccessfulResponse(pawns.Union(nearbyPawns).Distinct().ToList(), talkResponses, prompt);
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

        public static bool GenerateTalkFromPool(TalkRequest talkRequest)
        {
            if (!GenerateTalk(talkRequest)) return false;
            TalkRequestPool.Remove(talkRequest);
            return true;
        }

        private static void ProcessSuccessfulResponse(List<Pawn> allInvolvedPawns, List<TalkResponse> talkResponses, string request)
        {
            try
            {
                for (int i = 0; i < talkResponses.Count; i++)
                {
                    PawnState pawnState = Cache.GetByName(talkResponses[i].Name) ?? Cache.Get(allInvolvedPawns[i]);
                    pawnState.TalkQueue.Enqueue(talkResponses[i]);
                    talkResponses[i].Id = Guid.NewGuid();
                    talkResponses[i].Name = pawnState.Pawn.Name.ToStringShort;
                    if (i > 0)
                    {
                        talkResponses[i].ParentTalkId = talkResponses[i - 1].Id;
                    }
                }
                
                // Add the responses to the history of all pawns
                string cleanedPrompt = request.Replace(Constant.Prompt, "");
                foreach (var pawn in allInvolvedPawns)
                {
                    TalkHistory.AddMessageHistory(pawn, cleanedPrompt, JsonUtil.SerializeToJson(talkResponses));;
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
                int parentTalkTick = TalkHistory.GetSpokenTick(talk.ParentTalkId);
                if (parentTalkTick == -1 || GenTicks.TicksGame - parentTalkTick
                    < CommonUtil.GetTicksForDuration(pawnState.ReplyInterval)) continue;

                // if pawn is not able to talk, skip it
                if (!pawnState.CanDisplayTalk())
                {
                    ConsumeTalk(pawnState);
                    continue;
                }

                InteractionDef intDef = DefDatabase<InteractionDef>.GetNamed("RimTalkInteraction");
                var playLogEntryInteraction =
                    new PlayLogEntry_RimTalkInteraction(intDef, pawn, pawn, null);

                Find.PlayLog.Add(playLogEntryInteraction);
                break;
            }
        }

        public static string GetTalk(Pawn pawn)
        {
            PawnState pawnState = Cache.Get(pawn);
            if (pawnState == null) return null;
            
            TalkResponse talkResponse = ConsumeTalk(pawnState);
            
            if (!talkResponse.IsReply())
                pawnState.LastTalkTick = GenTicks.TicksGame;

            return talkResponse.Text;
        }

        private static TalkResponse ConsumeTalk(PawnState pawnState)
        {
            TalkResponse talkResponse = pawnState.TalkQueue.Dequeue();
            TalkHistory.AddSpoken(talkResponse.Id);
            return talkResponse;
        }
    }
}