using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class OverlayTabLauncher : MainTabWindow
{
    // This remains empty, as it's just a launcher.
    public override void DoWindowContents(Rect inRect)
    {
    }

    public override void PostOpen()
    {
        base.PostOpen();

        var settings = Settings.Get();

        settings.OverlayEnabled = !settings.OverlayEnabled;

        if (settings.OverlayEnabled)
        {
            settings.OverlayEnabled = true;
        }
            
        settings.Write();
        Close();
    }
}