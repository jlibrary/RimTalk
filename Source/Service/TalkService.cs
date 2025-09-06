using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Error;
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
            
            if (!settings.displayTalkWhenDrafted)
            {
                if (initiator.Drafted) return false;
                if (recipient != null && recipient.Drafted) recipient = null;
            }

            PawnState pawn1 = Cache.Get(initiator);
            PawnState pawn2 = Cache.Get(recipient);

            if (prompt == pawn1.LastPrompt && pawn1.RejectCount < 4)
            {
                pawn1.RejectCount++;
                return false;
            }

            bool isMonologue = recipient?.Name == null || !Cache.Contains(recipient);

            if (!force)
            {
                if (AIService.IsBusy()) return false;
                if (!pawn1.CanGenerateTalk()) return false;
                if (!isMonologue && !pawn2.CanGenerateTalk()) return false;
            }

            pawn1.RejectCount = 0;
            pawn1.LastPrompt = prompt;

            List<Pawn> pawns = new List<Pawn> { initiator, recipient }.Where(p => p != null).ToList();
            
            PawnService.BuildContext(pawns);
            prompt = DecoratePrompt(pawns, prompt);
            
            string response = null;
            Task.Run(async () =>
            {
                try
                {
                    response = await Generate(pawns, prompt);
                }
                catch (Exception ex)
                {
                    response = await TalkErrorHandler.HandleGenerationException(ex, pawns, prompt);
                }
                finally
                {
                    Logger.Message($"{prompt}\n{response}");
                    if (response != null)
                    {
                        Settings.Get().isUsingFallbackModel = false;
                    }
                    TalkErrorHandler.ResetQuotaWarning();
                    Cache.Get(initiator).IsGeneratingTalk = false;
                }
            });
            return true;
        }

        private static async Task<string> Generate(List<Pawn> pawns, string prompt)
        {
            string response;
            if (AIService.IsFirstInstruction())
                prompt += $" in {Constant.Lang}";

            Cache.Get(pawns[0]).IsGeneratingTalk = true;

            response = await AIService.Chat(prompt);

            if (response == null)
            {
                Logger.Warning($"null response {prompt}");
                return response;
            }

            // Sanitize response
            response = JsonUtil.Sanitize(response);

            List<Talk> talks = JsonUtil.DeserializeFromJson<List<Talk>>(response);

            // if talk returned by AI is not from initiator, then ignore
            if (pawns.Count == 1 && talks[0].Name != pawns[0].Name.ToStringShort)
                return response;

            for (int i = 0; i < talks.Count; i++)
            {
                PawnState pawnState = Cache.GetByName(talks[i].Name) ?? Cache.Get(pawns[i]);
                pawnState.TalkQueue.Enqueue(talks[i]);
                talks[i].Id = Guid.NewGuid();
                talks[i].Name = pawnState.pawn.Name.ToStringShort;
                if (i > 0)
                {
                    talks[i].ReplyToTalkId = talks[i - 1].Id;
                }
            }

            AIService.AddResposne(
                JsonUtil.SerializeToJson(talks)); // add the successful response back to model
            return response;
        }

        private static string DecoratePrompt(List<Pawn> pawns, string prompt)
        {
            bool isMonologue = pawns.Count == 1 || !Cache.Contains(pawns[1]);

            var specialCondition = PawnService.SpecialConditionLabel(pawns[0]);
            
            if (specialCondition != null)
            {
                prompt = $"Goes crazy due to {specialCondition}";
            }
            else
            {
                prompt = prompt.RemoveLineBreaks();
                prompt = $"{prompt} ({CommonUtil.GetInGameHour12HString()})";
            }

            int initiatorMood = (int)((pawns[0]?.needs?.mood?.CurLevelPercentage ?? 0) * 100) + 15;
            if (isMonologue)
            {
                prompt = $"{pawns[0].Name.ToStringShort}(mood: {initiatorMood}): {prompt}";
            }
            else
            {
                int recipientMood = (int)((pawns[1]?.needs?.mood?.CurLevelPercentage ?? 0) * 100) + 15;
                prompt = $"{pawns[0].Name.ToStringShort}(mood: {initiatorMood}) and " +
                         $"{pawns[1].Name.ToStringShort}(mood: {recipientMood}): {prompt}";
            }

            prompt += $" (nearby person: {PawnService.GetNearByPawn(pawns[0])})";
            return prompt;
        }

        // check if async call is completed then display
        public static void DisplayTalk()
        {
            // TODO: fix
            foreach (Pawn pawn in Cache.Keys)
            {
                PawnState pawnState = Cache.Get(pawn);

                if (pawnState == null || pawnState.TalkQueue.Empty()) continue;

                var nextTalk = pawnState.TalkQueue.Peek();
                if (nextTalk == null)
                {
                    pawnState.TalkQueue.Dequeue();
                    continue;
                }

                int replyToTalkTick = TalkHistory.Get(nextTalk.ReplyToTalkId);
                if (replyToTalkTick == -1 || Find.TickManager.TicksGame - replyToTalkTick
                    < CommonUtil.GetTicksForDuration(pawnState.ReplyInternal)) continue;

                if (Find.TickManager.TicksGame - pawnState.LastTalkTick
                    < CommonUtil.GetTicksForDuration(pawnState.TalkInterval)) continue;

                if (!PawnService.IsAbleToTalk(pawn))
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
    
            pawnState.LastTalkTick = Find.TickManager.TicksGame; 
    
            Talk talk = ConsumeTalk(pawnState);
    
            Cache.UpdatePawnSortPosition(pawn);
    
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