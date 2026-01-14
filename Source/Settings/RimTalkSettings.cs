using System.Collections.Generic;
using RimTalk.Data;
using RimTalk.Prompt;
using UnityEngine;
using Verse;

namespace RimTalk;

public class RimTalkSettings : ModSettings
{
    public List<ApiConfig> CloudConfigs = [];
    public int CurrentCloudConfigIndex = 0;
    public ApiConfig LocalConfig = new() { Provider = AIProvider.Local };
    public bool UseCloudProviders = true;
    public bool UseSimpleConfig = true;
    public string SimpleApiKey = "";
    public bool IsUsingFallbackModel = false;
    public bool IsEnabled = true;
    public int TalkInterval = 7;
    public const int ReplyInterval = 4;
    public bool ProcessNonRimTalkInteractions = true;
    public bool AllowSimultaneousConversations = false;
    public string CustomInstruction = ""; // Legacy - will be migrated to PromptManager
    
    // New Prompt System
    public PromptManager PromptSystem = new();
    public bool UseAdvancedPromptMode = false;  // Default to Simple Mode
    public Dictionary<string, bool> EnabledArchivableTypes = new();
    public bool DisplayTalkWhenDrafted = true;
    public bool AllowMonologue = true;
    public bool AllowSlavesToTalk = true;
    public bool AllowPrisonersToTalk = true;
    public bool AllowOtherFactionsToTalk = false;
    public bool AllowEnemiesToTalk = false;
    public bool AllowCustomConversation = true;
    public Settings.PlayerDialogueMode PlayerDialogueMode = Settings.PlayerDialogueMode.Manual;
    public string PlayerName = "Player";
    public bool ContinueDialogueWhileSleeping = false;
    public bool AllowBabiesToTalk = true;
    public bool AllowNonHumanToTalk = true;
    public bool ApplyMoodAndSocialEffects = false;
    public int DisableAiAtSpeed = 0;
    public Settings.ButtonDisplayMode ButtonDisplay = Settings.ButtonDisplayMode.Toggle;

    public ContextSettings Context = new();

    // Debug mode settings
    public bool DebugModeEnabled = false;
    public string DebugSortColumn;
    public bool DebugSortAscending = true;

    // Overlay settings
    public bool OverlayEnabled = false;
    public float OverlayOpacity = 0.5f;
    public float OverlayFontSize = 15f;
    public bool OverlayDrawAboveUI = true;
    public Rect OverlayRectDebug = new(200f, 200f, 600f, 450f);
    public Rect OverlayRectNonDebug = new(200f, 200f, 400f, 250f);

    /// <summary>
    /// Gets the first active and valid API configuration.
    /// Checks the active provider type (Cloud or Local) and returns the first enabled config with a valid API key/URL.
    /// </summary>
    /// <returns>The active ApiConfig, or null if no valid configuration is found.</returns>
    public ApiConfig GetActiveConfig()
    {
        if (UseSimpleConfig)
        {
            if (!string.IsNullOrWhiteSpace(SimpleApiKey))
            {
                return new ApiConfig
                {
                    ApiKey = SimpleApiKey,
                    Provider = AIProvider.Google,
                    SelectedModel = IsUsingFallbackModel ? Constant.FallbackCloudModel : Constant.DefaultCloudModel,
                    IsEnabled = true
                };
            }

            return null;
        }

        if (UseCloudProviders)
        {
            if (CloudConfigs.Count == 0) return null;

            // Start searching from the current index
            for (int i = 0; i < CloudConfigs.Count; i++)
            {
                int index = (CurrentCloudConfigIndex + i) % CloudConfigs.Count;
                var config = CloudConfigs[index];
                if (config.IsValid())
                {
                    CurrentCloudConfigIndex = index; // Update the current index
                    return config;
                }
            }
            return null; // No valid config found
        }
        else
        {
            // Check local configuration
            if (LocalConfig != null && LocalConfig.IsValid())
            {
                return LocalConfig;
            }
        }

        return null;
    }

    /// <summary>
    /// Advances the current cloud configuration index to the next valid configuration.
    /// </summary>
    public void TryNextConfig()
    {
        if (CloudConfigs.Count <= 1) return; // No need to advance if 0 or 1 config

        int originalIndex = CurrentCloudConfigIndex;
        for (int i = 1; i < CloudConfigs.Count; i++) // Start from the next one
        {
            int nextIndex = (originalIndex + i) % CloudConfigs.Count;
            var config = CloudConfigs[nextIndex];
            if (config.IsValid())
            {
                CurrentCloudConfigIndex = nextIndex;
                Write(); // Save the updated index
                return;
            }
        }
        // If no other valid config is found, we stay at the current index or revert to original if it was valid
        // For now, we'll just stay at the current index.
        Write(); // Save in case the original was invalid and we couldn't find a new one.
    }

    /// <summary>
    /// Gets the currently active Gemini model, handling custom model names.
    /// </summary>
    /// <returns>The name of the model to use for Gemini API calls.</returns>
    public string GetCurrentModel()
    {
        var activeConfig = GetActiveConfig();
        if (activeConfig == null) return Constant.DefaultCloudModel;

        if (activeConfig.SelectedModel == "Custom")
        {
            return activeConfig.CustomModelName;
        }
        return activeConfig.SelectedModel;
    }

    public override void ExposeData()
    {
        base.ExposeData();
            
        Scribe_Collections.Look(ref CloudConfigs, "cloudConfigs", LookMode.Deep);
        Scribe_Deep.Look(ref LocalConfig, "localConfig");
        Scribe_Values.Look(ref UseCloudProviders, "useCloudProviders", true);
        Scribe_Values.Look(ref UseSimpleConfig, "useSimpleConfig", true);
        Scribe_Values.Look(ref SimpleApiKey, "simpleApiKey", "");
        Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        Scribe_Values.Look(ref TalkInterval, "talkInterval", 7);
        Scribe_Values.Look(ref ProcessNonRimTalkInteractions, "processNonRimTalkInteractions", true);
        Scribe_Values.Look(ref AllowSimultaneousConversations, "allowSimultaneousConversations", false);
        Scribe_Values.Look(ref CustomInstruction, "customInstruction", "");
        Scribe_Values.Look(ref DisplayTalkWhenDrafted, "displayTalkWhenDrafted", true);
        Scribe_Values.Look(ref AllowMonologue, "allowMonologue", true);
        Scribe_Values.Look(ref AllowSlavesToTalk, "allowSlavesToTalk", true);
        Scribe_Values.Look(ref AllowPrisonersToTalk, "allowPrisonersToTalk", true);
        Scribe_Values.Look(ref AllowOtherFactionsToTalk, "allowOtherFactionsToTalk", false);
        Scribe_Values.Look(ref AllowEnemiesToTalk, "allowEnemiesToTalk", false);
        Scribe_Values.Look(ref AllowCustomConversation, "allowCustomConversation", true);
        Scribe_Values.Look(ref PlayerDialogueMode, "playerDialogueMode", Settings.PlayerDialogueMode.Manual);
        Scribe_Values.Look(ref PlayerName, "playerName", "Player");
        
        Scribe_Values.Look(ref ContinueDialogueWhileSleeping, "continueDialogueWhileSleeping", false);
        Scribe_Values.Look(ref DisableAiAtSpeed, "DisableAiAtSpeed", 0);
        Scribe_Collections.Look(ref EnabledArchivableTypes, "enabledArchivableTypes", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref AllowBabiesToTalk, "allowBabiesToTalk", true);
        Scribe_Values.Look(ref AllowNonHumanToTalk, "allowNonHumanToTalk", true);
        Scribe_Values.Look(ref ApplyMoodAndSocialEffects, "applyMoodAndSocialEffects", false);
        
        Scribe_Deep.Look(ref Context, "context");
        
        // New Prompt System
        Scribe_Deep.Look(ref PromptSystem, "promptSystem");
        Scribe_Values.Look(ref UseAdvancedPromptMode, "useAdvancedPromptMode", false);

        // Debug window settings
        Scribe_Values.Look(ref ButtonDisplay, "buttonDisplay", Settings.ButtonDisplayMode.Toggle, true);
        Scribe_Values.Look(ref DebugModeEnabled, "debugModeEnabled", false);
        Scribe_Values.Look(ref DebugSortColumn, "debugSortColumn", null);
        Scribe_Values.Look(ref DebugSortAscending, "debugSortAscending", true);
        
        // Overlay settings
        Scribe_Values.Look(ref OverlayEnabled, "overlayEnabled", false);
        Scribe_Values.Look(ref OverlayOpacity, "overlayOpacity", 0.5f);
        Scribe_Values.Look(ref OverlayFontSize, "overlayFontSize", 15f);
        Scribe_Values.Look(ref OverlayDrawAboveUI, "overlayDrawAboveUI", true);

        // Scribe Debug Overlay Rect
        Rect defaultDebugRect = new Rect(200f, 200f, 600f, 450f);
        float overlayDebugX = OverlayRectDebug.x;
        float overlayDebugY = OverlayRectDebug.y;
        float overlayDebugWidth = OverlayRectDebug.width;
        float overlayDebugHeight = OverlayRectDebug.height;
        Scribe_Values.Look(ref overlayDebugX, "overlayRectDebug_x", defaultDebugRect.x);
        Scribe_Values.Look(ref overlayDebugY, "overlayRectDebug_y", defaultDebugRect.y);
        Scribe_Values.Look(ref overlayDebugWidth, "overlayRectDebug_width", defaultDebugRect.width);
        Scribe_Values.Look(ref overlayDebugHeight, "overlayRectDebug_height", defaultDebugRect.height);

        // Scribe Non-Debug Overlay Rect
        Rect defaultNonDebugRect = new Rect(200f, 200f, 400f, 250f);
        float overlayNonDebugX = OverlayRectNonDebug.x;
        float overlayNonDebugY = OverlayRectNonDebug.y;
        float overlayNonDebugWidth = OverlayRectNonDebug.width;
        float overlayNonDebugHeight = OverlayRectNonDebug.height;
        Scribe_Values.Look(ref overlayNonDebugX, "overlayRectNonDebug_x", defaultNonDebugRect.x);
        Scribe_Values.Look(ref overlayNonDebugY, "overlayRectNonDebug_y", defaultNonDebugRect.y);
        Scribe_Values.Look(ref overlayNonDebugWidth, "overlayRectNonDebug_width", defaultNonDebugRect.width);
        Scribe_Values.Look(ref overlayNonDebugHeight, "overlayRectNonDebug_height", defaultNonDebugRect.height);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            OverlayRectDebug = new Rect(overlayDebugX, overlayDebugY, overlayDebugWidth, overlayDebugHeight);
            OverlayRectNonDebug = new Rect(overlayNonDebugX, overlayNonDebugY, overlayNonDebugWidth, overlayNonDebugHeight);
        }

        // Initialize collections if null
        if (CloudConfigs == null)
            CloudConfigs = new List<ApiConfig>();
            
        if (LocalConfig == null)
            LocalConfig = new ApiConfig { Provider = AIProvider.Local };
                
        if (EnabledArchivableTypes == null)
            EnabledArchivableTypes = new Dictionary<string, bool>();

        if (Context == null)
            Context = new ContextSettings();
        
        // Initialize PromptSystem if null
        if (PromptSystem == null)
            PromptSystem = new PromptManager();
        
        // Set the singleton instance
        PromptManager.SetInstance(PromptSystem);
        
        // Migrate legacy CustomInstruction to new system
        if (Scribe.mode == LoadSaveMode.PostLoadInit && !string.IsNullOrWhiteSpace(CustomInstruction))
        {
            PromptSystem.MigrateLegacyInstruction(CustomInstruction);
            CustomInstruction = ""; // Clear after migration
        }
            
        // Ensure we have at least one cloud config
        if (CloudConfigs.Count == 0)
        {
            CloudConfigs.Add(new ApiConfig());
        }
    }
}