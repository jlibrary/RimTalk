using RimTalk.Data;
using RimTalk.Patch;
using RimTalk.Service;
using Verse;

namespace RimTalk
{
    public class RimTalk : GameComponent
    {
        public bool IsEnabled = true;
        
        public RimTalk(Game game) { }

        public static bool IsEnabledNow()
        {
            return Current.Game.GetComponent<RimTalk>()?.IsEnabled ?? true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref IsEnabled, "IsEnabled", true);
        }

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
            var settings = Settings.Get();
            if (settings != null)
            {
                settings.currentCloudConfigIndex = 0;
            }

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