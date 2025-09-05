using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.AI.OpenAI;
using RimTalk.Util;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk
{
    public partial class Settings
    {
        private static readonly string[] modelOptions = new string[]
        {
            "gemini-2.5-pro",
            "gemini-2.5-flash", 
            "gemini-2.5-flash-lite",
            "gemini-2.0-flash",
            "gemini-2.0-flash-lite",
            "gemma-3-27b-it",
            "gemma-3-12b-it",
            "Custom"
        };
        private static Dictionary<string, List<string>> _modelCache = new Dictionary<string, List<string>>();

        private async Task<List<string>> FetchModels(string apiKey, string url)
        {
            if (_modelCache.ContainsKey(url))
            {
                return _modelCache[url];
            }

            var models = new List<string>();
            using (var webRequest = UnityWebRequest.Get(url))
            {
                webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
                var asyncOperation = webRequest.SendWebRequest();

                while (!asyncOperation.isDone)
                {
                    await Task.Delay(100);
                }

                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    Logger.Error($"Failed to fetch models: {webRequest.error}");
                }
                else
                {
                    var response = JsonUtil.DeserializeFromJson<ModelsResponse>(webRequest.downloadHandler.text);
                    if (response != null)
                    {
                        models = response.Data.Select(m => m.Id).ToList();
                        _modelCache[url] = models;
                    }
                }
            }

            return models;
        }

        private void DrawSimpleApiSettings(Listing_Standard listingStandard)
        {
            CurrentWorkDisplayModSettings settings = Get();

            // API Key section
            listingStandard.Label("RimTalk.Settings.GoogleApiKeyLabel".Translate());
            settings.simpleApiKey = Widgets.TextField(listingStandard.GetRect(24), settings.simpleApiKey);
            
            // Add description for free Google providers
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(cloudDescRect,
                "RimTalk.Settings.GoogleApiKeyDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

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
            
            x += 60f; // Adjust x to account for the add/remove buttons

            // Provider Header
            Rect providerHeaderRect = new Rect(x, y, 100f, height);
            Widgets.Label(providerHeaderRect, "RimTalk.Settings.ProviderHeader".Translate());
            x += 105f;

            // API Key Header
            Rect apiKeyHeaderRect = new Rect(x, y, 240f, height);
            Widgets.Label(apiKeyHeaderRect, "RimTalk.Settings.ApiKeyHeader".Translate());
            x += 245f;

            // Model Header
            Rect modelHeaderRect = new Rect(x, y, 200f, height);
            Widgets.Label(modelHeaderRect, "RimTalk.Settings.ModelHeader".Translate());
            x += 205f;
            
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
                DrawCloudConfigRow(listingStandard, settings.cloudConfigs[i], i, settings.cloudConfigs);
                listingStandard.Gap(3f);
            }
        }

        private void DrawCloudConfigRow(Listing_Standard listingStandard, ApiConfig config, int index, List<ApiConfig> configs)
        {
            Rect rowRect = listingStandard.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;
            
            // Reorder buttons
            // Up button
            Rect upButtonRect = new Rect(x, y, 24f, height);
            if (Widgets.ButtonText(upButtonRect, "▲") && index > 0)
            {
                var temp = configs[index];
                configs[index] = configs[index - 1];
                configs[index - 1] = temp;
            }
            x += 30f;

            // Down button
            Rect downButtonRect = new Rect(x, y, 24f, height);
            if (Widgets.ButtonText(downButtonRect, "▼") && index < configs.Count - 1)
            {
                var temp = configs[index];
                configs[index] = configs[index + 1];
                configs[index + 1] = temp;
            }
            
            x += 30f;

            // Provider dropdown (100px)
            Rect providerRect = new Rect(x, y, 100f, height);
            if (Widgets.ButtonText(providerRect, config.Provider.ToString()))
            {
                List<FloatMenuOption> providerOptions = new List<FloatMenuOption>
                {
                    new FloatMenuOption(AIProvider.Google.ToString(), () =>
                    {
                        config.Provider = AIProvider.Google;
                        config.SelectedModel = Data.Constant.ChooseModel;
                    }),
                    new FloatMenuOption(AIProvider.OpenAI.ToString(), () =>
                    {
                        config.Provider = AIProvider.OpenAI;
                        config.SelectedModel = Data.Constant.ChooseModel;
                    }),
                    new FloatMenuOption(AIProvider.DeepSeek.ToString(), () =>
                    {
                        config.Provider = AIProvider.DeepSeek;
                        config.SelectedModel = Data.Constant.ChooseModel;
                    }),
                    new FloatMenuOption(AIProvider.OpenRouter.ToString(), () =>
                    {
                        config.Provider = AIProvider.OpenRouter;
                        config.SelectedModel = Data.Constant.ChooseModel;
                    })
                };
                Find.WindowStack.Add(new FloatMenu(providerOptions));
            }

            x += 105f;

            // API Key field (240px)
            Rect apiKeyRect = new Rect(x, y, 240f, height);
            config.ApiKey = Widgets.TextField(apiKeyRect, config.ApiKey);
            x += 245f;

            // Model dropdown (200px)
            Rect modelRect = new Rect(x, y, 200f, height);
            if (Widgets.ButtonText(modelRect, config.SelectedModel))
            {
                if (string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        { new FloatMenuOption("RimTalk.Settings.EnterApiKey".Translate(), null) }));
                }
                else if (config.Provider == AIProvider.Google)
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (string model in modelOptions)
                    {
                        string modelCopy = model;
                        options.Add(new FloatMenuOption(model, () => config.SelectedModel = modelCopy));
                    }

                    Find.WindowStack.Add(new FloatMenu(options));
                }
                else
                {
                    string url;
                    switch (config.Provider)
                    {
                        case AIProvider.OpenAI:
                            url = "https://api.openai.com/v1/models";
                            break;
                        case AIProvider.DeepSeek:
                            url = "https://api.deepseek.com/models";
                            break;
                        case AIProvider.OpenRouter:
                            url = "https://openrouter.ai/api/v1/models";
                            break;
                        default:
                            return;
                    }

                    FetchModels(config.ApiKey, url).ContinueWith(task =>
                    {
                        var models = task.Result;
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        if (models != null && models.Any())
                        {
                            foreach (string model in models)
                            {
                                string modelCopy = model;
                                options.Add(new FloatMenuOption(model, () => config.SelectedModel = modelCopy));
                            }
                        }
                        else
                        {
                            options.Add(new FloatMenuOption("(no models found - check API Key)", null));
                        }

                        options.Add(new FloatMenuOption("Custom", () => config.SelectedModel = "Custom"));
                        Find.WindowStack.Add(new FloatMenu(options));
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
            }

            x += 205f;

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