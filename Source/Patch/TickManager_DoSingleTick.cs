using HarmonyLib;
using RimTalk.Service;
using Verse;
using RimTalk.Data;
using RimWorld;
using System.Collections.Generic;
using RimTalk.Util;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class TickManager_DoSingleTick
    {
        private const double DisplayInterval = 1.5; // Display every seconds
        private const int UpdateCacheInterval = 5;    // 5 seconds
        private static int JobTalkInterval => Settings.Get().talkInterval * 2;
        private static int ThoughtsTalkInterval => Settings.Get().talkInterval * 2 + 2;
        public static bool NoApiKeyMessageShown;
        public static bool InitialCacheRefresh;

        public static void Postfix()
        {
            Counter.Tick++;

            if (Find.CurrentMap == null) return;

            if (!InitialCacheRefresh || IsNow(UpdateCacheInterval))
            {
                Cache.Refresh();
                InitialCacheRefresh = true;
            }
            
            if (!NoApiKeyMessageShown && Settings.Get().GetActiveConfig() == null)
            {
                Messages.Message("RimTalk.TickManager.ApiKeyMissing".Translate(), MessageTypeDefOf.NegativeEvent, false);
                NoApiKeyMessageShown = true;
            }

            if (IsNow(DisplayInterval))
            {
                TalkService.DisplayTalk();
            }

            if (IsNow(ThoughtsTalkInterval))
            {
                foreach (Pawn pawn in PawnService.GetPawnsAbleToTalk())
                {
                    KeyValuePair<Thought, float> thought = PawnService.GetNewThought(pawn);
                    string thoughtLabel = PawnService.GetNewThoughtLabel(thought.Key);
                    if (thoughtLabel != null)
                    {
                        TalkService.GenerateTalk($"{thoughtLabel} {PawnService.GetTalkSubject(pawn)}", pawn);
                    }
                    var pawnCache = Cache.Get(pawn);
                    if (IsNow(ThoughtsTalkInterval * 50))  // reset thoughts occasionally
                        pawnCache?.UpdateThoughts();
                    else
                        pawnCache?.UpdateThoughts(thought);
                    break;
                }
            }

            if (IsNow(JobTalkInterval))
            {
                foreach (Pawn pawn in PawnService.GetPawnsAbleToTalk())
                {
                    if (TalkService.GenerateTalk(PawnService.GetTalkSubject(pawn), pawn)) break;
                }
            }
        }
        
        private static bool IsNow(double interval)
        {
            int ticksForDuration = CommonUtil.GetTicksForDuration(interval);
            if (ticksForDuration == 0) return false;
            return Counter.Tick % ticksForDuration == 0;
        }
    }
}