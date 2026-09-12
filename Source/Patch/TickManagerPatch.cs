using HarmonyLib;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Service;
using RimTalk.Source.Data;
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
    private static int _lastTalkEndTick;

    public static void Postfix()
    {
        Counter.Tick++;

        AIErrorHandler.DrainPendingMessages();

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

        if (IsNow(1))
        {
            ProcessFastTrackRequests();
        }

        ProcessRegularTalkRequests();
    }

    private static void ProcessRegularTalkRequests()
    {
        if (AIService.IsBusy())
        {
            if (AIService.CurrentRequest == null || !AIService.CurrentRequest.TalkType.IsFastTrack())
            {
                _lastTalkEndTick = GenTicks.TicksGame;
            }
            return;
        }

        int intervalTicks = CommonUtil.GetTicksForDuration(TalkInterval);
        if (intervalTicks <= 0 || GenTicks.TicksGame - _lastTalkEndTick < intervalTicks)
            return;

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
                if (pawnState?.GetNextTalkRequest() != null)
                    talkGenerated = TalkService.GenerateTalk(pawnState.GetNextTalkRequest());
            }

            // 3. Fallback: generate based on current context if nothing else worked
            if (!talkGenerated)
            {
                TalkRequest talkRequest = new TalkRequest(null, selectedPawn, talkType: TalkType.Chitchat);
                TalkService.GenerateTalk(talkRequest);
            }
        }

        _lastTalkEndTick = GenTicks.TicksGame;
    }

    private static void ProcessFastTrackRequests()
    {
        while (UserRequestPool.GetNextUserRequest() is { } pawn)
        {
            var pawnState = Cache.Get(pawn);
            if (pawnState == null)
            {
                UserRequestPool.Remove(pawn);
                continue;
            }
            var request = pawnState.GetNextTalkRequest();

            if (request == null)
            {
                UserRequestPool.Remove(pawn);
                continue;
            }

            if (AIService.IsBusy())
            {
                if (AIService.CanCancelFor(request))
                {
                    AIService.CancelCurrent();
                }
                else if (request.TalkType == TalkType.Interaction)
                {
                    DropQueuedRequest(pawn, pawnState, request);
                    continue;
                }
                return;
            }

            TalkService.GenerateTalk(request);
            UserRequestPool.Remove(pawn);
            return;
        }
    }

    private static void DropQueuedRequest(Pawn pawn, PawnState state, TalkRequest request)
    {
        UserRequestPool.Remove(pawn);
        state.TalkRequests.Remove(request);
        TalkRequestPool.AddToHistory(request, RequestStatus.Expired);
    }

    private static bool TryGenerateTalkFromPool(Pawn pawn)
    {
        // If the pawn is a free colonist not in danger and the pool has requests
        if (!pawn.IsFreeNonSlaveColonist || pawn.IsQuestLodger() || TalkRequestPool.IsEmpty || pawn.IsInDanger(true)) return false;
        var request = TalkRequestPool.GetRequestFromPool(pawn);
        if (request != null && TalkService.GenerateTalk(request))
        {
            TalkRequestPool.Remove(request);
            return true;
        }
        return false;
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
        _lastTalkEndTick = GenTicks.TicksGame;
        TopicService.Reset();
        SleepDialogueTracker.Reset();
    }
}
