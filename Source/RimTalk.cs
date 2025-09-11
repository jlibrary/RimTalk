using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
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

        public void Reset(bool soft = false)
        {
            var settings = Settings.Get();
            if (settings != null)
            {
                settings.CurrentCloudConfigIndex = 0;
            }
            AIErrorHandler.ResetQuotaWarning();
            TickManager_DoSingleTick.Reset();
            AIClientFactory.Clear();
            AIService.Clear();

            if (soft) return;
            
            Counter.Tick = 0;
            Cache.Clear();
            Stats.Reset();
            TalkLogHistory.Clear();
        }
    }
}