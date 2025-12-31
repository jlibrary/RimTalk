using System.Linq;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Patch;
using RimTalk.Service;
using RimTalk.UI;
using Verse;

namespace RimTalk;

public class RimTalk : GameComponent
{
    public RimTalk(Game game)
    {
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

    public static void Reset(bool soft = false)
    {
        var settings = Settings.Get();
        if (settings != null)
        {
            settings.CurrentCloudConfigIndex = 0;
            CheckPlayer2Deprecation(settings);
        }

        AIErrorHandler.ResetQuotaWarning();
        TickManagerPatch.Reset();
        AIClientFactory.Clear();
        AIService.Clear();
        TalkHistory.Clear();
        PatchThoughtHandlerGetDistinctMoodThoughtGroups.Clear();
        Cache.GetAll().ToList().ForEach(pawnState => pawnState.IgnoreAllTalkResponses());
        Cache.InitializePlayerPawn();

        if (soft) return;

        Counter.Tick = 0;
        Cache.Clear();
        Stats.Reset();
        TalkRequestPool.Clear();
        ApiHistory.Clear();
    }

    private static void CheckPlayer2Deprecation(RimTalkSettings settings)
    {
        if (settings.Player2DeprecationAck) return;
        
        // Show warning if Player2 is present in the config list at all
        bool hasPlayer2Config = settings.CloudConfigs != null && settings.CloudConfigs.Any(c => c.Provider == AIProvider.Player2);
        
        if (hasPlayer2Config)
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Find.WindowStack != null && !Find.WindowStack.IsOpen(typeof(Player2DeprecationWindow)))
                {
                    Find.WindowStack.Add(new Player2DeprecationWindow());
                }
            });
        }
    }
}