using HarmonyLib;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patch;

[HarmonyPriority(Priority.First)]
[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootUpdate))]
internal static class UIRootPlayPatch
{
    private static float _lastPausedDisplayTime;

    public static void Postfix()
    {
        var settings = Settings.Get();
        if (!settings.IsEnabled) return;

        if (settings.IgnorePendingHotkeyEnabled && Input.GetKeyDown(settings.IgnorePendingHotkey))
        {
            int ignored = TalkService.IgnoreAllPendingTalks();
            Messages.Message("RimTalk.Settings.IgnorePendingResult".Translate(ignored), MessageTypeDefOf.CautionInput, false);
        }

        if (!Find.TickManager.Paused || !settings.SpeakWhilePaused) return;
        if (settings.StopSpeakingInMenus && TalkService.IsSpeakingBlockedByMenu(settings.AdvancedMenuAvoidance)) return;

        float now = Time.realtimeSinceStartup;
        if (now - _lastPausedDisplayTime < settings.DisplayTalkInterval) return;
        _lastPausedDisplayTime = now;

        TalkService.DisplayTalk();
    }

    public static void Reset()
    {
        _lastPausedDisplayTime = 0f;
    }
}
