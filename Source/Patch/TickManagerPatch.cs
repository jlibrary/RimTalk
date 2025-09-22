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
        private const int UpdateCacheInterval = 5; // 5 seconds
        private static double TalkInterval => Settings.Get().TalkInterval;
        private static bool _noApiKeyMessageShown;
        private static bool _initialCacheRefresh;
        private static bool _isNormalTalkIteration = true; // Track which iteration we're on

        public static void Postfix()
        {
            Counter.Tick++;

            if (IsNow(DebugStatUpdateInterval))
            {
                Stats.Update();
            }

            if (!Settings.Get().IsEnabled || Find.CurrentMap == null)
            {
                return;
            }

            if (!_initialCacheRefresh || IsNow(UpdateCacheInterval))
            {
                Cache.Refresh();
                _initialCacheRefresh = true;
            }

            if (!_noApiKeyMessageShown && Settings.Get().GetActiveConfig() == null)
            {
                Messages.Message("RimTalk.TickManager.ApiKeyMissing".Translate(), MessageTypeDefOf.NegativeEvent,
                    false);
                _noApiKeyMessageShown = true;
            }

            if (IsNow(DisplayInterval))
            {
                TalkService.DisplayTalk();
            }

            if (IsNow(TalkInterval))
            {
                // Select a pawn based on the current iteration strategy
                Pawn selectedPawn = PawnSelector.SelectAvailablePawnByWeight();

                if (selectedPawn != null)
                {
                    // 1. ALWAYS try to get from the general pool first.
                    var talkGenerated = TryGenerateTalkFromPool(selectedPawn);

                    // 2. If no talk was generated from the pool, proceed with the original logic.
                    if (!talkGenerated)
                    {
                        if (_isNormalTalkIteration)
                        {
                            // Iteration 1: Try normal talk, then thought talk as fallback
                            talkGenerated = TryGenerateNormalTalk(selectedPawn);
                            if (!talkGenerated)
                            {
                                talkGenerated = TryGenerateThoughtTalk(selectedPawn);
                            }
                        }
                        else
                        {
                            // Iteration 2: Try thought talk, then normal talk as fallback
                            talkGenerated = TryGenerateThoughtTalk(selectedPawn);
                            if (!talkGenerated)
                            {
                                talkGenerated = TryGenerateNormalTalk(selectedPawn);
                            }
                        }
                    }

                    // 3. Final fallback: generate based on current context if nothing else worked
                    if (!talkGenerated)
                    {
                        TalkService.GenerateTalk(null, selectedPawn);
                    }
                }

                // Toggle for the next iteration
                _isNormalTalkIteration = !_isNormalTalkIteration;
            }
        }

        private static bool TryGenerateTalkFromPool(Pawn pawn)
        {
            // If the pawn is a free colonist not in danger and the pool has requests
            if (pawn.IsFreeNonSlaveColonist && !TalkRequestPool.IsEmpty && !PawnService.IsPawnInDanger(pawn))
            {
                var request = TalkRequestPool.GetRequestFromPool(pawn);
                if (request != null)
                {
                    return TalkService.GenerateTalk(request);
                }
            }

            return false;
        }

        private static bool TryGenerateNormalTalk(Pawn pawn)
        {
            var pawnState = Cache.Get(pawn);

            // If the pawn has a specific talk request, try generating it
            if (pawnState.TalkRequest != null && !pawnState.TalkRequest.IsExpired())
            {
                return TalkService.GenerateTalk(pawnState.TalkRequest);
            }

            return false;
        }

        private static bool TryGenerateThoughtTalk(Pawn pawn)
        {
            var thought = PawnService.GetNewThought(pawn);
            var thoughtLabel = PawnService.GetNewThoughtLabel(thought.Key);
            bool result = false;

            if (thoughtLabel != null)
            {
                result = TalkService.GenerateTalk(thoughtLabel, pawn);
            }

            var pawnCache = Cache.Get(pawn);
            if (IsNow(TalkInterval * 50)) // reset thoughts occasionally
            {
                pawnCache?.UpdateThoughts();
            }
            else if (result)
            {
                pawnCache?.UpdateThoughts(thought);
            }

            return result;
        }

        private static bool IsNow(double interval)
        {
            int ticksForDuration = CommonUtil.GetTicksForDuration(interval);
            if (ticksForDuration == 0) return false;
            return Counter.Tick % ticksForDuration == 0;
        }

        public static void Reset()
        {
            _noApiKeyMessageShown = false;
            _initialCacheRefresh = false;
            _isNormalTalkIteration = true;
        }
    }
}