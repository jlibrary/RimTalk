using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class Dialog_ImageViewer : Window
{
    private readonly Texture2D _texture;

    public Dialog_ImageViewer(Texture2D texture)
    {
        _texture = texture;
        doCloseX = true;
        closeOnClickedOutside = true;
        draggable = true;
        resizeable = true;
        absorbInputAroundWindow = false;
    }

    public override Vector2 InitialSize => new(800f, 600f);

    public override void DoWindowContents(Rect inRect)
    {
        if (_texture != null)
        {
            GUI.DrawTexture(inRect, _texture, ScaleMode.ScaleToFit);
        }
        else
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(inRect, "No image to display.");
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
