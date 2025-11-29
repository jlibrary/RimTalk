using HarmonyLib;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class TogglePatch
    {
        private static readonly Texture2D RimTalkToggleIcon = ContentFinder<Texture2D>.Get("UI/RimTalkIcon");

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row is null)
                return;

            var settings = Settings.Get();

            if (settings.ButtonDisplay != Settings.ButtonDisplayMode.Toggle)
            {
                return;
            }

            var overlayEnabled = settings.OverlayEnabled;

            row.ToggleableIcon(ref overlayEnabled, RimTalkToggleIcon, "RimTalk.Toggle.Tooltip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);

            if (overlayEnabled != settings.OverlayEnabled)
            {
                bool shift = Event.current.shift;
                bool control = Event.current.control;

                if (shift && !Find.WindowStack.IsOpen<Dialog_ModSettings>())
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
                }
                else if (control && !Find.WindowStack.IsOpen<DebugWindow>())
                {
                    Find.WindowStack.Add(new DebugWindow());
                }
                else if (!shift && !control)
                {
                    settings.OverlayEnabled = overlayEnabled;
                    settings.Write();
                }
            }
        }
    }
}