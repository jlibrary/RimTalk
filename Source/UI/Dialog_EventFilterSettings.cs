using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_EventFilterSettings : Window
{
    private readonly Settings _settingsMod;
    private Vector2 _scrollPosition = Vector2.zero;

    public override Vector2 InitialSize => new(650f, 620f);

    public Dialog_EventFilterSettings(Settings settingsMod)
    {
        _settingsMod = settingsMod;
        doCloseX = true;
        closeOnAccept = true;
        closeOnCancel = true;
        draggable = true;
        absorbInputAroundWindow = true;
        preventCameraMotion = false;
    }

    public override void PostClose()
    {
        base.PostClose();
        Settings.Get()?.Write();
    }

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 32f), "RimTalk.Settings.EventFilter".Translate());
        Text.Font = GameFont.Small;

        const float closeBtnHeight = 35f;
        Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f - closeBtnHeight - 10f);

        // Pre-calculate content height offscreen
        GUI.BeginGroup(new Rect(-9999, -9999, 1, 1));
        Listing_Standard calcListing = new Listing_Standard();
        calcListing.Begin(new Rect(0, 0, contentRect.width - 16f, 9999f));
        _settingsMod.DrawEventFilterSettings(calcListing);
        float calculatedHeight = calcListing.CurHeight;
        calcListing.End();
        GUI.EndGroup();

        Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, calculatedHeight);
        _scrollPosition = GUI.BeginScrollView(contentRect, _scrollPosition, viewRect);

        Listing_Standard listing = new Listing_Standard();
        listing.Begin(viewRect);
        _settingsMod.DrawEventFilterSettings(listing);
        listing.End();

        GUI.EndScrollView();

        // Bottom Action Buttons
        float btnWidth = 160f;
        float btnGap = 16f;
        float totalBtnsWidth = btnWidth * 2f + btnGap;
        float startX = (inRect.width - totalBtnsWidth) / 2f;

        Rect resetBtnRect = new Rect(startX, inRect.height - closeBtnHeight, btnWidth, closeBtnHeight);
        if (UIUtil.ButtonText(resetBtnRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            _settingsMod.ResetEventFilterToDefault();
        }

        Rect closeBtnRect = new Rect(resetBtnRect.xMax + btnGap, inRect.height - closeBtnHeight, btnWidth, closeBtnHeight);
        if (UIUtil.ButtonText(closeBtnRect, "Close".Translate()))
        {
            Close();
        }
    }
}
