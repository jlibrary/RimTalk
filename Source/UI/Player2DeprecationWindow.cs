using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Player2DeprecationWindow : Window
{
    private bool _dontShowAgain;
    
    public Player2DeprecationWindow()
    {
        doCloseX = false;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    public override Vector2 InitialSize => new Vector2(500f, 350f);

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "RimTalk.Player2Deprecation.Title".Translate());
        
        Text.Font = GameFont.Small;
        float y = 45f;
        
        string message = "RimTalk.Player2Deprecation.Message".Translate();
        Widgets.Label(new Rect(0f, y, inRect.width, 180f), message);
        
        y += 190f;
        
        Widgets.CheckboxLabeled(
            new Rect(0f, y, inRect.width, 24f), 
            "RimTalk.Player2Deprecation.DontShowAgain".Translate(), 
            ref _dontShowAgain
        );
        
        y += 40f;

        if (Widgets.ButtonText(new Rect(inRect.width / 2f - 60f, inRect.height - 40f, 120f, 35f), "OK".Translate()))
        {
            if (_dontShowAgain)
            {
                var settings = Settings.Get();
                settings.Player2DeprecationAck = true;
                settings.Write();
            }
            Close();
        }
    }
}
