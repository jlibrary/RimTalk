using System;
using System.Collections.Generic;
using RimTalk.Data;
using Verse;

namespace RimTalk
{
    public class CurrentWorkDisplayModSettings : ModSettings
    {
        // New API configuration system
        public List<ApiConfig> CloudConfigs = new List<ApiConfig>();
        public int CurrentCloudConfigIndex = 0;
        public ApiConfig LocalConfig = new ApiConfig { Provider = AIProvider.Local };
        public bool UseCloudProviders = true;
        public bool UseSimpleConfig = true;
        public string SimpleApiKey = "";
        public readonly bool IsUsingFallbackModel = false;

        public bool IsEnabled = true;

        // Other existing settings
        public int TalkInterval = 7;
        public bool ProcessNonRimTalkInteractions;
        public string CustomInstruction = "";
        public Dictionary<string, bool> EnabledArchivableTypes = new Dictionary<string, bool>();
        public bool DisplayTalkWhenDrafted = true;
        public bool AllowSlavesToTalk = true;
        public bool AllowPrisonersToTalk = true;
        public bool AllowOtherFactionsToTalk = false;
        public bool AllowEnemiesToTalk = false;

        // Debug window settings
        public bool DebugModeEnabled = false;
        public bool DebugGroupingEnabled = false;
        public string DebugSortColumn;
        public bool DebugSortAscending = true;
        public List<string> DebugExpandedPawns = new List<string>();

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
            Write(); // Save in case the original was invalid and we couldn\'t find a new one.
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
            
            // New API configuration system
            Scribe_Collections.Look(ref CloudConfigs, "cloudConfigs", LookMode.Deep);
            Scribe_Deep.Look(ref LocalConfig, "localConfig");
            Scribe_Values.Look(ref UseCloudProviders, "useCloudProviders", true);
            Scribe_Values.Look(ref UseSimpleConfig, "useSimpleConfig", true);
            Scribe_Values.Look(ref SimpleApiKey, "simpleApiKey", "");
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
            
            // Other existing settings
            Scribe_Values.Look(ref TalkInterval, "talkInterval", 7);
            Scribe_Values.Look(ref ProcessNonRimTalkInteractions, "processNonRimTalkInteractions", true);
            Scribe_Values.Look(ref CustomInstruction, "customInstruction", "");
            Scribe_Values.Look(ref DisplayTalkWhenDrafted, "displayTalkWhenDrafted", true);
            Scribe_Values.Look(ref AllowSlavesToTalk, "allowSlavesToTalk", true);
            Scribe_Values.Look(ref AllowPrisonersToTalk, "allowPrisonersToTalk", true);
            Scribe_Values.Look(ref AllowOtherFactionsToTalk, "allowOtherFactionsToTalk", false);
            Scribe_Values.Look(ref AllowEnemiesToTalk, "allowEnemiesToTalk", false);
            Scribe_Collections.Look(ref EnabledArchivableTypes, "enabledArchivableTypes", LookMode.Value, LookMode.Value);

            // Debug window settings
            Scribe_Values.Look(ref DebugModeEnabled, "debugModeEnabled", false);
            Scribe_Values.Look(ref DebugGroupingEnabled, "debugGroupingEnabled", false);
            Scribe_Values.Look(ref DebugSortColumn, "debugSortColumn", null);
            Scribe_Values.Look(ref DebugSortAscending, "debugSortAscending", true);
            Scribe_Collections.Look(ref DebugExpandedPawns, "debugExpandedPawns", LookMode.Value);

            // Initialize collections if null
            if (CloudConfigs == null)
                CloudConfigs = new List<ApiConfig>();
            
            if (LocalConfig == null)
                LocalConfig = new ApiConfig { Provider = AIProvider.Local };
                
            if (EnabledArchivableTypes == null)
                EnabledArchivableTypes = new Dictionary<string, bool>();
            
            // Ensure we have at least one cloud config
            if (CloudConfigs.Count == 0)
            {
                CloudConfigs.Add(new ApiConfig());
            }
        }
    }
}
