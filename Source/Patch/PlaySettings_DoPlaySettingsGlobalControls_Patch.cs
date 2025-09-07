using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch
{
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
    public static class PlaySettings_DoPlaySettingsGlobalControls_Patch
    {        
        private static readonly Texture2D RimTalk_Toggle_Icon = ContentFinder<Texture2D>.Get("UI/ToggleIcon");

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row is null) 
                return;
            
            // Get the current setting value.
            var isEnabled = RimTalk.IsEnabled;

            row.ToggleableIcon(ref isEnabled, RimTalk_Toggle_Icon, "RimTalk.Toggle.Tooltip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);

            if (isEnabled != RimTalk.IsEnabled && Event.current.shift && Find.WindowStack.WindowOfType<Dialog_ModSettings>() == null)
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
            }
            else { RimTalk.IsEnabled = isEnabled; }
        }
    }
}