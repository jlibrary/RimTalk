using System;
using System.Collections.Generic;
using RimTalk.Data;
using Verse;

namespace RimTalk
{
    public class CurrentWorkDisplayModSettings : ModSettings
    {
        public string apiKey = "";
        
        // New API configuration system
        public List<ApiConfig> cloudConfigs = new List<ApiConfig>();
        public int currentCloudConfigIndex = 0;
        public ApiConfig localConfig = new ApiConfig { Provider = AIProvider.Local };
        public bool useCloudProviders = true;
        public bool useSimpleConfig = true;
        public string simpleApiKey = "";

        // Other existing settings
        public int talkInterval = 7;
        public bool suppressUnprocessedMessages;
        public bool processNonRimTalkInteractions;
        public string customInstruction = "";
        public Dictionary<string, bool> enabledArchivableTypes = new Dictionary<string, bool>();
        public bool displayTalkWhenDrafted = true;

        /// <summary>
        /// Gets the first active and valid API configuration.
        /// Checks the active provider type (Cloud or Local) and returns the first enabled config with a valid API key/URL.
        /// </summary>
        /// <returns>The active ApiConfig, or null if no valid configuration is found.</returns>
        public ApiConfig GetActiveConfig()
        {
            if (useSimpleConfig)
            {
                if (!string.IsNullOrWhiteSpace(simpleApiKey))
                {
                    return new ApiConfig
                    {
                        ApiKey = simpleApiKey,
                        Provider = AIProvider.Google,
                        SelectedModel = Constant.DefaultCloudModel,
                        IsEnabled = true
                    };
                }

                return null;
            }

            if (useCloudProviders)
            {
                if (cloudConfigs.Count == 0) return null;

                // Start searching from the current index
                for (int i = 0; i < cloudConfigs.Count; i++)
                {
                    int index = (currentCloudConfigIndex + i) % cloudConfigs.Count;
                    var config = cloudConfigs[index];
                    if (config.IsValid())
                    {
                        currentCloudConfigIndex = index; // Update the current index
                        return config;
                    }
                }
                return null; // No valid config found
            }
            else
            {
                // Check local configuration
                if (localConfig != null && localConfig.IsValid())
                {
                    return localConfig;
                }
            }

            return null;
        }

        /// <summary>
        /// Advances the current cloud configuration index to the next valid configuration.
        /// </summary>
        public void TryNextConfig()
        {
            if (cloudConfigs.Count <= 1) return; // No need to advance if 0 or 1 config

            int originalIndex = currentCloudConfigIndex;
            for (int i = 1; i < cloudConfigs.Count; i++) // Start from the next one
            {
                int nextIndex = (originalIndex + i) % cloudConfigs.Count;
                var config = cloudConfigs[nextIndex];
                if (config.IsValid())
                {
                    currentCloudConfigIndex = nextIndex;
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
            
            // Legacy API key handling for backward compatibility
            Scribe_Values.Look(ref apiKey, "geminiApiKey", "");
            
            // New API configuration system
            Scribe_Collections.Look(ref cloudConfigs, "cloudConfigs", LookMode.Deep);
            Scribe_Deep.Look(ref localConfig, "localConfig");
            Scribe_Values.Look(ref useCloudProviders, "useCloudProviders", true);
            Scribe_Values.Look(ref useSimpleConfig, "useSimpleConfig", true);
            Scribe_Values.Look(ref simpleApiKey, "simpleApiKey", "");
            
            // Other existing settings
            Scribe_Values.Look(ref talkInterval, "talkInterval", 7);
            Scribe_Values.Look(ref suppressUnprocessedMessages, "suppressUnprocessedMessages", false);
            Scribe_Values.Look(ref processNonRimTalkInteractions, "processNonRimTalkInteractions", false);
            Scribe_Values.Look(ref customInstruction, "customInstruction", "");
            Scribe_Values.Look(ref displayTalkWhenDrafted, "displayTalkWhenDrafted", true);
            Scribe_Collections.Look(ref enabledArchivableTypes, "enabledArchivableTypes", LookMode.Value, LookMode.Value);

            // Initialize collections if null
            if (cloudConfigs == null)
                cloudConfigs = new List<ApiConfig>();
            
            if (localConfig == null)
                localConfig = new ApiConfig { Provider = AIProvider.Local };
                
            if (enabledArchivableTypes == null)
                enabledArchivableTypes = new Dictionary<string, bool>();

            // Migration logic: if we have a legacy API key but no cloud configs, migrate it
            if (!string.IsNullOrWhiteSpace(apiKey) && cloudConfigs.Count == 0)
            {
                cloudConfigs.Add(new ApiConfig 
                { 
                    ApiKey = apiKey,
                    IsEnabled = true,
                    Provider = AIProvider.Google,
                    SelectedModel = Constant.DefaultCloudModel
                });
            }

            // Ensure we have at least one cloud config
            if (cloudConfigs.Count == 0)
            {
                cloudConfigs.Add(new ApiConfig());
            }
        }
    }
}
