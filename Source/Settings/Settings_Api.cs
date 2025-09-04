using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawSimpleApiSettings(Listing_Standard listingStandard)
        {
            CurrentWorkDisplayModSettings settings = Get();

            // API Key section
            listingStandard.Label("RimTalk.Settings.GoogleApiKeyLabel".Translate(AIProvider.Google.ToString()));
            settings.simpleApiKey = Widgets.TextField(listingStandard.GetRect(24), settings.simpleApiKey);

            // Add a button that opens the API key page
            listingStandard.Gap(6f);
            Rect getKeyButtonRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(getKeyButtonRect, "RimTalk.Settings.GetFreeApiKeyButton".Translate()))
            {
                Application.OpenURL("https://aistudio.google.com/app/apikey");
            }

            listingStandard.Gap(12f);

            // Show Advanced Settings button
            Rect advancedButtonRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(advancedButtonRect, "RimTalk.Settings.SwitchToAdvancedSettings".Translate()))
            {
                settings.useSimpleConfig = false;
            }
        }

        private void DrawAdvancedApiSettings(Listing_Standard listingStandard)
        {
            CurrentWorkDisplayModSettings settings = Get();

            // Show Simple Settings button
            Rect simpleButtonRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(simpleButtonRect, "RimTalk.Settings.SwitchToSimpleSettings".Translate()))
            {
                if (string.IsNullOrWhiteSpace(settings.simpleApiKey))
                {
                    var firstValidCloudConfig = settings.cloudConfigs.FirstOrDefault(c => c.IsValid());
                    if (firstValidCloudConfig != null)
                    {
                        settings.simpleApiKey = firstValidCloudConfig.ApiKey;
                    }
                }
                settings.useSimpleConfig = true;
            }

            listingStandard.Gap(12f);

            // Cloud providers option with description
            Rect radioRect1 = listingStandard.GetRect(24f);
            if (Widgets.RadioButtonLabeled(radioRect1, "RimTalk.Settings.CloudProviders".Translate(), settings.useCloudProviders))
            {
                settings.useCloudProviders = true;
            }

            // Add description for cloud providers
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(cloudDescRect,
                "RimTalk.Settings.CloudProvidersDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listingStandard.Gap(3f);

            // Local provider option with description
            Rect radioRect2 = listingStandard.GetRect(24f);
            if (Widgets.RadioButtonLabeled(radioRect2, "RimTalk.Settings.LocalProvider".Translate(), !settings.useCloudProviders))
            {
                settings.useCloudProviders = false;
                settings.localConfig.Provider = AIProvider.Local;
            }

            // Add description for local provider
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect localDescRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(localDescRect,
                "RimTalk.Settings.LocalProviderDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listingStandard.Gap(12f);

            // Draw appropriate section based on selection
            if (settings.useCloudProviders)
            {
                DrawCloudProvidersSection(listingStandard, settings);
            }
            else
            {
                DrawLocalProviderSection(listingStandard, settings);
            }
        }

        private void DrawCloudProvidersSection(Listing_Standard listingStandard, CurrentWorkDisplayModSettings settings)
        {
            // Header with add/remove buttons
            Rect headerRect = listingStandard.GetRect(24f);
            Rect addButtonRect = new Rect(headerRect.x + headerRect.width - 65f, headerRect.y, 30f, 24f);
            Rect removeButtonRect = new Rect(headerRect.x + headerRect.width - 30f, headerRect.y, 30f, 24f);
            headerRect.width -= 70f;

            Widgets.Label(headerRect, "RimTalk.Settings.CloudApiConfigurations".Translate());

            // Add description for cloud providers
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight * 2);
            cloudDescRect.width -= 70f;
            Widgets.Label(cloudDescRect,
                "RimTalk.Settings.CloudApiConfigurationsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            
            if (Widgets.ButtonText(addButtonRect, "+"))
            {
                settings.cloudConfigs.Add(new ApiConfig());
            }

            // Only show remove button if there are configs to remove and more than 1
            GUI.enabled = settings.cloudConfigs.Count > 1;
            if (Widgets.ButtonText(removeButtonRect, "−"))
            {
                // Remove the last configuration
                if (settings.cloudConfigs.Count > 1)
                {
                    settings.cloudConfigs.RemoveAt(settings.cloudConfigs.Count - 1);
                }
            }

            GUI.enabled = true;

            listingStandard.Gap(6f);

            // Draw table headers
            Rect tableHeaderRect = listingStandard.GetRect(24f);
            float x = tableHeaderRect.x;
            float y = tableHeaderRect.y;
            float height = tableHeaderRect.height;

            // Provider Header
            Rect providerHeaderRect = new Rect(x, y, 100f, height);
            Widgets.Label(providerHeaderRect, "RimTalk.Settings.ProviderHeader".Translate());
            x += 105f;

            // API Key Header
            Rect apiKeyHeaderRect = new Rect(x, y, 300f, height);
            Widgets.Label(apiKeyHeaderRect, "RimTalk.Settings.ApiKeyHeader".Translate());
            x += 305f;

            // Model Header
            Rect modelHeaderRect = new Rect(x, y, 150f, height);
            Widgets.Label(modelHeaderRect, "RimTalk.Settings.ModelHeader".Translate());
            x += 155f;
            
            // Custom Model Header
            Rect customModelHeaderRect = new Rect(x, y, 150f, height);
            Widgets.Label(customModelHeaderRect, "RimTalk.Settings.CustomModelHeader".Translate());
            x += 155f;

            // Enabled Header
            Rect enabledHeaderRect = new Rect(tableHeaderRect.xMax - 70f, y, 70f, height);
            Widgets.Label(enabledHeaderRect, "RimTalk.Settings.EnabledHeader".Translate());
            
            listingStandard.Gap(6f);

            // Draw each cloud config
            for (int i = 0; i < settings.cloudConfigs.Count; i++)
            {
                DrawCloudConfigRow(listingStandard, settings.cloudConfigs[i], i);
                listingStandard.Gap(3f);
            }
        }

        private void DrawCloudConfigRow(Listing_Standard listingStandard, ApiConfig config, int index)
        {
            Rect rowRect = listingStandard.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;

            // Provider dropdown (100px)
            Rect providerRect = new Rect(x, y, 100f, height);
            if (Widgets.ButtonText(providerRect, config.Provider.ToString()))
            {
                List<FloatMenuOption> providerOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption(AIProvider.Google.ToString(), () => config.Provider = AIProvider.Google)
                };
                Find.WindowStack.Add(new FloatMenu(providerOptions));
            }

            x += 105f;

            // API Key field (300px)
            Rect apiKeyRect = new Rect(x, y, 300f, height);
            config.ApiKey = Widgets.TextField(apiKeyRect, config.ApiKey);
            x += 305f;

            // Model dropdown
            Rect modelRect = new Rect(x, y, 150f, height);
            if (Widgets.ButtonText(modelRect, config.SelectedModel))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (string model in modelOptions)
                {
                    string modelCopy = model;
                    options.Add(new FloatMenuOption(model, () => config.SelectedModel = modelCopy));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            x += 155f;

            // Custom model name field (if Custom is selected)
            if (config.SelectedModel == "Custom")
            {
                Rect customModelRect = new Rect(x, y, 150f, height);
                config.CustomModelName = Widgets.TextField(customModelRect, config.CustomModelName);
            }
            x += 155f;

            // Enable/Disable checkbox aligned to the right (70px)
            Rect toggleRect = new Rect(rowRect.xMax - 70f, y, 24f, height);
            Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref config.IsEnabled);

            // Add tooltip for the checkbox
            if (Mouse.IsOver(toggleRect))
            {
                TooltipHandler.TipRegion(toggleRect, "RimTalk.Settings.EnableDisableApiConfigTooltip".Translate());
            }
        }


        private void DrawLocalProviderSection(Listing_Standard listingStandard, CurrentWorkDisplayModSettings settings)
        {
            listingStandard.Label("RimTalk.Settings.LocalProviderConfiguration".Translate());
            listingStandard.Gap(6f);

            // Ensure local config exists
            if (settings.localConfig == null)
            {
                settings.localConfig = new ApiConfig { Provider = AIProvider.Local };
            }

            DrawLocalConfigRow(listingStandard, settings.localConfig);
        }

        private void DrawLocalConfigRow(Listing_Standard listingStandard, ApiConfig config)
        {
            Rect rowRect = listingStandard.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;

            // Label for Base Url
            Rect baseUrlLabelRect = new Rect(x, y, 70f, height);
            Widgets.Label(baseUrlLabelRect, "RimTalk.Settings.BaseUrlLabel".Translate());
            x += 75f; // Adjust x to account for the label's width

            // Endpoint URL field
            Rect urlRect = new Rect(x, y, 250f, height);
            config.BaseUrl = Widgets.TextField(urlRect, config.BaseUrl);
            x += 285f;

            // Label for Model
            Rect modelLabelRect = new Rect(x, y, 70f, height);
            Widgets.Label(modelLabelRect, "RimTalk.Settings.ModelLabel".Translate());
            x += 75f; // Adjust x to account for the label's width

            // Model text field (200px)
            Rect modelRect = new Rect(x, y, 200f, height);
            config.CustomModelName = Widgets.TextField(modelRect, config.CustomModelName);
            x += 205f;
        }
    }
}