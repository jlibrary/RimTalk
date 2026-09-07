using System;
using System.Reflection;
using HarmonyLib;
using RimTalk.Patch;
using RimTalk.UI;
using RimTalk.Util;
using Verse;

namespace RimTalk.Compatibility;

[StaticConstructorOnStartup]
public static class BubbleCompatibilityPatch
{
    private static readonly FieldInfo DoDraftedField = AccessTools.Field(AccessTools.TypeByName("Bubbles.Settings"), "DoDrafted");
    private static readonly FieldInfo ValueField = AccessTools.Field(DoDraftedField?.FieldType, "Value");
    private static bool _originalDraftedValue;
    private static bool _draftedOverridden;

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
                prefix: new HarmonyMethod(typeof(Bubbler_Add), nameof(Bubbler_Add.Prefix)),
                postfix: new HarmonyMethod(typeof(Bubbler_Add), nameof(Bubbler_Add.Postfix)));
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

        // 1. RimTalk interaction: display in IB if configured, temporarily allowing drafted speech
        if (InteractionLogPatch.IsRimTalkInteraction(entry))
        {
            if (settings.BubbleMode != RimTalkSettings.BubbleDisplayMode.InteractionBubbles)
                return false;

            if (settings.DisplayTalkWhenDrafted && ValueField != null)
            {
                object setting = DoDraftedField?.GetValue(null);
                if (setting != null)
                {
                    _originalDraftedValue = (bool)ValueField.GetValue(setting);
                    ValueField.SetValue(setting, true);
                    _draftedOverridden = true;
                }
            }
            return true;
        }

        // 2. Vanilla interaction: suppress raw bubble so RimTalk can override it
        if (settings.IsEnabled && settings.ProcessNonRimTalkInteractions)
        {
            if (InteractionLogPatch.GetInteractionDef(entry) != null)
                return false;
        }

        return true;
    }

    public static void OnBubblerAddPostfix()
    {
        if (_draftedOverridden)
        {
            _draftedOverridden = false;
            object setting = DoDraftedField?.GetValue(null);
            if (setting != null)
            {
                ValueField?.SetValue(setting, _originalDraftedValue);
            }
        }
    }

    public static bool OnBubblerDrawPrefix()
    {
        return !Overlay.SuppressForScreenshot && !SpeechBubbleDrawer.SuppressForScreenshot;
    }
}
