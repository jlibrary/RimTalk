using HarmonyLib;
using RimWorld;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.Visible), MethodType.Getter)]
public static class MainTabsPatch
{
    public static void Postfix(MainButtonWorker __instance, ref bool __result)
    {
        if (__instance.def?.defName == "RimTalkDebug")
        {
            var settings = Settings.Get();
            if (settings.ButtonDisplay != Settings.ButtonDisplayMode.Tab)
            {
                __result = false;
            }
        }
    }
}