using RimTalk.Data;
using RimTalk.Patch;
using RimTalk.Service;
using Verse;

namespace RimTalk
{
    public class RimTalk : GameComponent
    {
        public RimTalk(Game game) { }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Reset();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Reset();
        }

        public void Reset()
        {
            Counter.Tick = 0;
            Cache.Clear();
            TalkHistory.Clear();
            TalkService._quotaWarningShown = false;
            TickManager_DoSingleTick.NoApiKeyMessageShown = false;
            TickManager_DoSingleTick.InitialCacheRefresh = false;
            AIClientFactory.Clear();
            AIService.Clear();
        }
    }
}