using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
internal static class TickManagerPatch
{
    private const double DisplayInterval = 0.5; // Display every half second
    private const double DebugStatUpdateInterval = 1;
    private const int UpdateCacheInterval = 5; // 5 seconds
    private static double TalkInterval => Settings.Get().TalkInterval;
    private static bool _noApiKeyMessageShown;
    private static bool _initialCacheRefresh;
    private static bool _chatHistoryCleared;

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
        
        if (IsNow(1))
        {
            // Clear LLM history daily to prevent repetitive/degraded dialogue
            int currentHour = CommonUtil.GetInGameHour(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile));
            if (currentHour == 0 && !_chatHistoryCleared)
            {
                TalkHistory.Clear();
                _chatHistoryCleared = true;
            }
            else if (currentHour != 0)
            {
                _chatHistoryCleared = false;
            }
        }

        if (!_noApiKeyMessageShown && Settings.Get().GetActiveConfig() == null)
        {
            Messages.Message("RimTalk.TickManager.ApiKeyMissing".Translate(), MessageTypeDefOf.NegativeEvent,
                false);
            _noApiKeyMessageShown = true;
        }

        if (IsNow(DisplayInterval))
        {
            CustomDialogueService.Tick();
            TalkService.DisplayTalk();
        }

        if (IsNow(TalkInterval))
        {
            // Select a pawn based on the current iteration strategy
            Pawn selectedPawn = PawnSelector.SelectNextAvailablePawn();

            if (selectedPawn != null)
            {
                // 1. ALWAYS try to get from the general pool first.
                var talkGenerated = TryGenerateTalkFromPool(selectedPawn);

                // 2. If the pawn has a specific talk request, try generating it
                if (!talkGenerated)
                {
                    var pawnState = Cache.Get(selectedPawn);
                    if (pawnState.GetNextTalkRequest() != null)
                        talkGenerated = TalkService.GenerateTalk(pawnState.GetNextTalkRequest());
                }

                // 3. Fallback: generate based on current context if nothing else worked
                if (!talkGenerated)
                {
                    TalkRequest talkRequest = new TalkRequest(null, selectedPawn);
                    TalkService.GenerateTalk(talkRequest);
                }
            }
        }
    }

    private static bool TryGenerateTalkFromPool(Pawn pawn)
    {
        // If the pawn is a free colonist not in danger and the pool has requests
        if (!pawn.IsFreeNonSlaveColonist || pawn.IsQuestLodger() || TalkRequestPool.IsEmpty || pawn.IsInDanger(true)) return false;
        var request = TalkRequestPool.GetRequestFromPool(pawn);
        return request != null && TalkService.GenerateTalk(request);
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
    }
}