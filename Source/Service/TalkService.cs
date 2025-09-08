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
        public static bool GenerateTalk(string prompt, Pawn initiator, Pawn recipient = null, bool force = false)
        {
            var settings = Settings.Get();
            if (!RimTalk.IsEnabled) return false;
            if (!Bubbles.Settings.Activated || settings.GetActiveConfig() == null) return false;
            if (!Cache.Contains(initiator)) return false;
            if (AIService.IsContextUpdating()) return false;

            PawnState pawn1 = Cache.Get(initiator);
            PawnState pawn2 = Cache.Get(recipient);
            
            if (!force)
            {
                if (AIService.IsBusy()) return false;
                if (!pawn1.CanGenerateTalk()) return false;
            }

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
            
            Task.Run(async () =>
            {
                try
                {
                    Cache.Get(initiator).IsGeneratingTalk = true;
                    string response = await AIService.Chat(prompt);
                    Logger.Message($"Pawn1: {pawns[0]?.Name?.ToStringShort}" + (pawns.Count > 1 ? $" Pawn2: {pawns[1]?.Name?.ToStringShort}" : "") +
                                   $"\n{prompt}\n{response}");
                    ProcessSuccessfulResponse(pawns, response);
                    Cache.Get(initiator).IsGeneratingTalk = false;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex.StackTrace);
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

        private static void ProcessSuccessfulResponse(List<Pawn> pawns, string response)
        {
            try
            {
                var sanitizedResponse = JsonUtil.Sanitize(response);
                List<Talk> talks = JsonUtil.DeserializeFromJson<List<Talk>>(sanitizedResponse);
                
                for (int i = 0; i < talks.Count; i++)
                {
                    PawnState pawnState = Cache.GetByName(talks[i].Name) ?? Cache.Get(pawns[i]);
                    pawnState.TalkQueue.Enqueue(talks[i]);
                    talks[i].Id = Guid.NewGuid();
                    talks[i].Name = pawnState.Pawn.Name.ToStringShort;
                    if (i > 0)
                    {
                        talks[i].ReplyToTalkId = talks[i - 1].Id;
                    }
                }

                AIService.CleanupLastRequest();
                AIService.AddResposne(response);
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
            
            Talk talk = ConsumeTalk(pawnState);
            
            pawnState.LastTalkTick = Find.TickManager.TicksGame;

            return talk.Text;
        }

        private static Talk ConsumeTalk(PawnState pawnState)
        {
            Talk talk = pawnState.TalkQueue.Dequeue();
            TalkHistory.AddTalk(talk.Id);
            return talk;
        }
    }
}