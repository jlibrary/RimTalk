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

    public static List<CustomDialoguePreset> CreateDefaultPresets()
    {
        return
        [
            new CustomDialoguePreset(
                "RimTalk.PlayerSettings.Preset1.Title".Translate(),
                "RimTalk.PlayerSettings.Preset1.Prompt".Translate(),
                includeVision: true,
                isAnnouncement: false,
                isEnabled: false),

            new CustomDialoguePreset(
                "RimTalk.PlayerSettings.Preset2.Title".Translate(),
                "RimTalk.PlayerSettings.Preset2.Prompt".Translate(),
                includeVision: false,
                isAnnouncement: false,
                isEnabled: false),

            new CustomDialoguePreset(
                "RimTalk.PlayerSettings.Preset3.Title".Translate(),
                "RimTalk.PlayerSettings.Preset3.Prompt".Translate(),
                includeVision: true,
                isAnnouncement: false,
                isEnabled: false),

            new CustomDialoguePreset(
                "RimTalk.PlayerSettings.Preset4.Title".Translate(),
                "RimTalk.PlayerSettings.Preset4.Prompt".Translate(),
                includeVision: false,
                isAnnouncement: true,
                isEnabled: false)
        ];
    }
}
