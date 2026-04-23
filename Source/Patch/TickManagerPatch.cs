using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
internal static class TickManagerPatch
{
    private const double DebugStatUpdateInterval = 1;
    private const int UpdateCacheInterval = 5; // 5 seconds
    private static bool _noApiKeyMessageShown;
    private static bool _initialCacheRefresh;
    private static bool _chatHistoryCleared;
    private static int _lastTalkEndTick;
    private static int _lastDisplayTick;
    private static int _lastAutoTalkCreateTick;

    public static void Postfix()
    {
        Counter.Tick++;

        if (IsNow(DebugStatUpdateInterval))
        {
            Stats.Update();
        }

        var settings = Settings.Get();
        if (!settings.IsEnabled || Find.CurrentMap == null)
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

        if (!_noApiKeyMessageShown && settings.GetActiveConfig() == null)
        {
            Messages.Message("RimTalk.TickManager.ApiKeyMissing".Translate(), MessageTypeDefOf.NegativeEvent,
                false);
            _noApiKeyMessageShown = true;
        }

        if (ShouldRunDisplayTick())
        {
            CustomDialogueService.Tick();
            TalkService.DisplayTalk();
        }

        bool shouldProcessUserRequestNow = settings.ProcessUserTalkRequestImmediately || IsNow(1);
        if (shouldProcessUserRequestNow)
        {
            // User-initiated talks: default is 1s cadence; optional immediate mode checks every tick.
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

                if (!request.TalkType.IsFromUser())
                {
                    UserRequestPool.Remove(pawn);
                    continue;
                }

                if (TalkService.GenerateTalk(request))
                    UserRequestPool.Remove(pawn);
                return;
            }
        }

        if (AIService.IsBusy())
        {
            _lastTalkEndTick = GenTicks.TicksGame;
            return;
        }

        int intervalTicks = CommonUtil.GetTicksForDuration(settings.TalkInterval, settings.AlignTimingToNormalSpeed);
        if (intervalTicks > 0 && GenTicks.TicksGame - _lastTalkEndTick >= intervalTicks)
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
                if (!talkGenerated && CanCreateAutoTalkRequestNow())
                {
                    TalkRequest talkRequest = new TalkRequest(null, selectedPawn);
                    talkGenerated = TalkService.GenerateTalk(talkRequest);
                    if (talkGenerated)
                        _lastAutoTalkCreateTick = GenTicks.TicksGame;
                }
            }
            
            _lastTalkEndTick = GenTicks.TicksGame;
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

    private static bool ShouldRunDisplayTick()
    {
        var settings = Settings.Get();
        int intervalTicks = CommonUtil.GetTicksForDuration(settings.DisplayTalkInterval, settings.AlignTimingToNormalSpeed);
        if (intervalTicks <= 0) return false;
        if (GenTicks.TicksGame - _lastDisplayTick < intervalTicks) return false;
        _lastDisplayTick = GenTicks.TicksGame;
        return true;
    }

    private static bool CanCreateAutoTalkRequestNow()
    {
        var settings = Settings.Get();
        int intervalTicks = CommonUtil.GetTicksForDuration(settings.AutoTalkRequestInterval, settings.AlignTimingToNormalSpeed);
        if (intervalTicks <= 0) return true;
        return GenTicks.TicksGame - _lastAutoTalkCreateTick >= intervalTicks;
    }

    public static void Reset()
    {
        _noApiKeyMessageShown = false;
        _initialCacheRefresh = false;
        _lastTalkEndTick = GenTicks.TicksGame;
        _lastDisplayTick = GenTicks.TicksGame;
        _lastAutoTalkCreateTick = GenTicks.TicksGame;
    }
}
