using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class TickManager_DoSingleTick
    {
        private const double DisplayInterval = 0.5; // Display every half second
        private const int UpdateCacheInterval = 5;    // 5 seconds
        private static double TalkInterval => Settings.Get().talkInterval;
        public static bool NoApiKeyMessageShown;
        public static bool InitialCacheRefresh;

        public static void Postfix()
        {
            Counter.Tick++;

            if (!RimTalk.IsEnabled || Find.CurrentMap == null) return;

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

            if (IsNow(TalkInterval))
            {
                var pawn = PawnSelector.SelectAvailablePawnByWeight();
                if (pawn != null)
                {
                    var pawnState = Cache.Get(pawn);
                    // if pawn has talk request, try generating
                    if (pawnState.TalkRequest != null)
                    {
                        TalkService.GenerateTalk(pawnState.TalkRequest);
                    }
                    // if pawn does not have any request and in good condition, try to take one from pool
                    else if (!TalkRequestPool.IsEmpty && !PawnService.IsPawnInDanger(pawn))
                    {
                        TalkService.GenerateTalkFromPool(pawn);
                    }
                    // otherwise generate based on current context
                    else
                    {
                        TalkService.GenerateTalk(null, pawn);
                    }
                }
            }
            
            if (IsNow(TalkInterval, TalkInterval / 2))
            {
                var pawn = PawnSelector.SelectAvailablePawnByWeight(true);
                var thought = PawnService.GetNewThought(pawn);
                var thoughtLabel = PawnService.GetNewThoughtLabel(thought.Key);
                bool result = false;

                if (thoughtLabel != null)
                {
                    result = TalkService.GenerateTalk(thoughtLabel, pawn);
                }

                var pawnCache = Cache.Get(pawn);
                if (IsNow(TalkInterval * 50))  // reset thoughts occasionally
                {
                    pawnCache?.UpdateThoughts();
                }
                else if (result)
                {
                    pawnCache?.UpdateThoughts(thought);
                }
            }
        }
        
        private static bool IsNow(double interval, double offset = 0)
        {
            int ticksForDuration = CommonUtil.GetTicksForDuration(interval);
            int offsetTicks = CommonUtil.GetTicksForDuration(offset);
            if (ticksForDuration == 0) return false;
            return Counter.Tick % ticksForDuration == offsetTicks;
        }
    }
}