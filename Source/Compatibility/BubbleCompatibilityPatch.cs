using System;
using HarmonyLib;
using RimTalk.Patch;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Compatibility;

[StaticConstructorOnStartup]
public static class BubbleCompatibilityPatch
{
    public static bool IsBubblesModLoaded { get; private set; }

    static BubbleCompatibilityPatch()
    {
        Type bubblerType = AccessTools.TypeByName("Bubbles.Core.Bubbler");
        if (bubblerType == null) return;

        IsBubblesModLoaded = true;
        try
        {
            var harmony = new Harmony("cj.rimtalk.compat.bubbles");
#pragma warning disable CS0618
            harmony.Patch(AccessTools.Method(bubblerType, "Add"),
                prefix: new HarmonyMethod(typeof(Bubbler_Add), nameof(Bubbler_Add.Prefix)));
            harmony.Patch(AccessTools.Method(bubblerType, "Draw"),
                prefix: new HarmonyMethod(typeof(Bubbler_Draw), nameof(Bubbler_Draw.Prefix)));
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to patch Interaction Bubbles: {ex.Message}");
        }
    }

    public static bool OnBubblerAddPrefix(LogEntry entry)
    {
        if (entry == null) return true;
        RimTalkSettings settings = Settings.Get();
        if (settings == null) return true;

        // 1. RimTalk interaction: suppress if using native bubbles or disabled
        if (InteractionLogPatch.IsRimTalkInteraction(entry))
        {
            return settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.InteractionBubbles;
        }

        // 2. Vanilla interaction: suppress raw bubble if RimTalk will generate an AI dialogue for it
        if (settings.IsEnabled && settings.ProcessNonRimTalkInteractions)
        {
            var def = InteractionLogPatch.GetInteractionDef(entry);
            if (def != null && (settings.IsFastTrackInteraction(def.defName) ||
                                def == InteractionDefOf.Chitchat || def == InteractionDefOf.DeepTalk))
            {
                return false;
            }
        }

        return true;
    }

    public static bool OnBubblerDrawPrefix()
    {
        return !Overlay.SuppressForScreenshot && !SpeechBubbleDrawer.SuppressForScreenshot;
    }
}
