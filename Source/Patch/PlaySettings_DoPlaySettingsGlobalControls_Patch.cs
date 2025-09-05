using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
    public static class PlaySettings_DoPlaySettingsGlobalControls_Patch
    {        
        private static readonly Texture2D RimTalk_Toggle_Icon = ContentFinder<Texture2D>.Get("UI/ToggleIcon");

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView)
            {
                return;
            }

            Toggle_Postfix(row);
        }
        
        public static void Toggle_Postfix(WidgetRow row)
        {
            // Get the current setting value.
            var rimTalk = Current.Game.GetComponent<RimTalk>();
            if (rimTalk == null) return;

            var isEnabled = rimTalk.IsEnabled;

            row.ToggleableIcon(ref isEnabled, RimTalk_Toggle_Icon, "RimTalk.Toggle.Tooltip".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);

            if (isEnabled != rimTalk.IsEnabled && Event.current.shift && Find.WindowStack.WindowOfType<Dialog_ModSettings>() == null)
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<Settings>()));
            }
            else { rimTalk.IsEnabled = isEnabled; }
        }
    }
}