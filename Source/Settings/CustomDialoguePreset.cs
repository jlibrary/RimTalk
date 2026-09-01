using System;
using System.Collections.Generic;
using Verse;

namespace RimTalk;

public class CustomDialoguePreset : IExposable
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Title = "";
    public string Prompt = "";
    public bool IncludeVision = false;
    public bool IsAnnouncement = false;
    public bool IsEnabled = false;
    public float CustomHeight = 65f;
    [Verse.Unsaved] public UnityEngine.Vector2 ScrollPosition = UnityEngine.Vector2.zero;

    public CustomDialoguePreset()
    {
    }

    public CustomDialoguePreset(string title, string prompt, bool includeVision = false, bool isAnnouncement = false, bool isEnabled = false)
    {
        Id = Guid.NewGuid().ToString("N");
        Title = title;
        Prompt = prompt;
        IncludeVision = includeVision;
        IsAnnouncement = isAnnouncement;
        IsEnabled = isEnabled;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id", Guid.NewGuid().ToString("N"));
        Scribe_Values.Look(ref Title, "title", "");
        Scribe_Values.Look(ref Prompt, "prompt", "");
        Scribe_Values.Look(ref IncludeVision, "includeVision", false);
        Scribe_Values.Look(ref IsAnnouncement, "isAnnouncement", false);
        Scribe_Values.Look(ref IsEnabled, "isEnabled", false);
        Scribe_Values.Look(ref CustomHeight, "customHeight", 65f);
    }

    public CustomDialoguePreset Clone()
    {
        return new CustomDialoguePreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = Title,
            Prompt = Prompt,
            IncludeVision = IncludeVision,
            IsAnnouncement = IsAnnouncement,
            IsEnabled = IsEnabled
        };
    }

    private static string SafeTranslate(string key, string fallback)
    {
        if (LanguageDatabase.activeLanguage != null && key.CanTranslate())
        {
            return key.Translate().ToString();
        }
        return fallback;
    }

    public static List<CustomDialoguePreset> CreateDefaultPresets()
    {
        return
        [
            new CustomDialoguePreset(
                SafeTranslate("RimTalk.PlayerSettings.Preset1.Title", "Base & Life"),
                SafeTranslate("RimTalk.PlayerSettings.Preset1.Prompt", "Looking around our base, how do you feel about our setup and daily life here?"),
                includeVision: true,
                isAnnouncement: false,
                isEnabled: false),

            new CustomDialoguePreset(
                SafeTranslate("RimTalk.PlayerSettings.Preset2.Title", "Mood & Well-being"),
                SafeTranslate("RimTalk.PlayerSettings.Preset2.Prompt", "How are you holding up lately? Anything on your mind or bothering you?"),
                includeVision: false,
                isAnnouncement: false,
                isEnabled: false),

            new CustomDialoguePreset(
                SafeTranslate("RimTalk.PlayerSettings.Preset3.Title", "Defenses & Tactics"),
                SafeTranslate("RimTalk.PlayerSettings.Preset3.Prompt", "Take a look at our defenses and gear. Spot any tactical weak points we should reinforce?"),
                includeVision: true,
                isAnnouncement: false,
                isEnabled: false),

            new CustomDialoguePreset(
                SafeTranslate("RimTalk.PlayerSettings.Preset4.Title", "Work & Safety Notice"),
                SafeTranslate("RimTalk.PlayerSettings.Preset4.Prompt", "Attention everyone! Stay alert, mind your safety, and let's focus on our duties!"),
                includeVision: false,
                isAnnouncement: true,
                isEnabled: false)
        ];
    }
}
