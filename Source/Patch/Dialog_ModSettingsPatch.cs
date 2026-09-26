using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(Dialog_ModSettings), nameof(Dialog_ModSettings.DoWindowContents))]
public static class Dialog_ModSettingsPatch
{
    public static bool Prefix(Dialog_ModSettings __instance, Rect inRect, Mod ___mod)
    {
        if (___mod is not Settings rtMod)
        {
            return true;
        }

        TextAnchor origAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.LowerLeft;

        Text.Font = GameFont.Medium;
        const string title = "RimTalk";
        Vector2 titleSize = Text.CalcSize(title);
        Rect titleRect = new Rect(inRect.x, inRect.y, titleSize.x, 35f);
        Widgets.Label(titleRect, title);

        Text.Font = GameFont.Small;
        string versionStr = $"v{Settings.Version}";
        Vector2 verSize = Text.CalcSize(versionStr);
        // Align bottom to match baseline with Medium font
        Rect verRect = new Rect(titleRect.xMax + 6f, inRect.y, verSize.x + 4f, 35f - 2f);

        Color prevColor = GUI.color;
        GUI.color = new Color(0.7f, 0.7f, 0.7f);
        Widgets.Label(verRect, versionStr);
        GUI.color = prevColor;

        Text.Anchor = origAnchor;
        Text.Font = GameFont.Small;

        inRect.yMin += 50f;
        rtMod.DoSettingsWindowContents(inRect);
        return false;
    }
}
