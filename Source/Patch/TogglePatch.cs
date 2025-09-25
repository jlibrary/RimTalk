using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class TogglePatch
    {        
        private static readonly Texture2D RimTalkToggleIcon = ContentFinder<Texture2D>.Get("UI/ToggleIcon");
        
        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row is null) 
                return;
         
            var settings = Settings.Get();

            if (settings.ButtonDisplay != ButtonDisplayMode.Toggle)
            {
                return;
            }
            
            // Get the current setting value.
            var isEnabled = settings.IsEnabled;

            row.ToggleableIcon(ref isEnabled, RimTalkToggleIcon, "RimTalk.Toggle.Tooltip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);

            if (isEnabled == settings.IsEnabled) return;
            
            if (Event.current.control)
            {
                settings.OverlayEnabled = !settings.OverlayEnabled;
                settings.Write();
            }
            else if (Event.current.shift && Find.WindowStack.WindowOfType<Dialog_ModSettings>() == null)
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
            }
            else
            {
                settings.IsEnabled = isEnabled;
                settings.Write();
            }
        }
    }
}