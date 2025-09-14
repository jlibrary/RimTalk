using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class TickManagerPatch
    {
        private const double DisplayInterval = 0.5; // Display every half second
        private const double DebugStatUpdateInterval = 1;
        private const int UpdateCacheInterval = 5;    // 5 seconds
        private static double TalkInterval => Settings.Get().TalkInterval;
        private static bool _noApiKeyMessageShown;
        private static bool _initialCacheRefresh;

        public static void Postfix()
        {
            Counter.Tick++;
            
            if (IsNow(DebugStatUpdateInterval))
            {
                Stats.Update();
            }

            if (!Settings.Get().IsEnabled || Find.CurrentMap == null) return;
            
            if (!_initialCacheRefresh || IsNow(UpdateCacheInterval))
            {
                Cache.Refresh();
                _initialCacheRefresh = true;
            }
            
            if (!_noApiKeyMessageShown && Settings.Get().GetActiveConfig() == null)
            {
                Messages.Message("RimTalk.TickManager.ApiKeyMissing".Translate(), MessageTypeDefOf.NegativeEvent, false);
                _noApiKeyMessageShown = true;
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
                    // if pawn not in danger, try to take one from general pool
                    if (!TalkRequestPool.IsEmpty && !PawnService.IsPawnInDanger(pawn))
                    {
                        TalkService.GenerateTalkFromPool(pawn);
                    }
                    // if pawn has talk request, try generating
                    else if (pawnState.TalkRequest != null)
                    {
                        TalkService.GenerateTalk(pawnState.TalkRequest);
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
        
        public static void Reset()
        {
            _noApiKeyMessageShown = false;
            _initialCacheRefresh = false;
        }
    }
}