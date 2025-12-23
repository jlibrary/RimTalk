using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Client.Gemini;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;
using RimTalk.Data;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk;

public partial class Settings
{
    private static readonly Dictionary<string, List<string>> ModelCache = new();

    private void DrawSimpleApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // API Key section
        listingStandard.Label("RimTalk.Settings.GoogleApiKeyLabel".Translate());

        const float buttonWidth = 150f;
        const float spacing = 5f;

        Rect rowRect = listingStandard.GetRect(30f);
        rowRect.width -= buttonWidth + spacing;

        settings.SimpleApiKey = Widgets.TextField(rowRect, settings.SimpleApiKey);

        Rect buttonRect = new Rect(rowRect.xMax + spacing, rowRect.y, buttonWidth, rowRect.height);
        if (Widgets.ButtonText(buttonRect, "RimTalk.Settings.GetFreeApiKeyButton".Translate()))
        {
            Application.OpenURL("https://aistudio.google.com/app/apikey");
        }

        // Add description for free Google providers
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(cloudDescRect, "RimTalk.Settings.GoogleApiKeyDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listingStandard.Gap();

        // Show Advanced Settings button
        Rect advancedButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(advancedButtonRect, "RimTalk.Settings.SwitchToAdvancedSettings".Translate()))
        {
            settings.UseSimpleConfig = false;
        }
    }

    private void DrawAdvancedApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // Show Simple Settings button
        Rect simpleButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(simpleButtonRect, "RimTalk.Settings.SwitchToSimpleSettings".Translate()))
        {
            if (string.IsNullOrWhiteSpace(settings.SimpleApiKey))
            {
                var firstValidCloudConfig = settings.CloudConfigs.FirstOrDefault(c => c.IsValid());
                if (firstValidCloudConfig != null)
                {
                    settings.SimpleApiKey = firstValidCloudConfig.ApiKey;
                }
            }
            settings.UseSimpleConfig = true;
        }

        listingStandard.Gap();

        // Cloud providers option with description
        Rect radioRect1 = listingStandard.GetRect(24f);
        if (Widgets.RadioButtonLabeled(radioRect1, "RimTalk.Settings.CloudProviders".Translate(), settings.UseCloudProviders))
        {
            settings.UseCloudProviders = true;
        }

        // Add description for cloud providers
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(cloudDescRect, "RimTalk.Settings.CloudProvidersDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listingStandard.Gap(3f);

        // Local provider option with description
        Rect radioRect2 = listingStandard.GetRect(24f);
        if (Widgets.RadioButtonLabeled(radioRect2, "RimTalk.Settings.LocalProvider".Translate(), !settings.UseCloudProviders))
        {
            settings.UseCloudProviders = false;
            settings.LocalConfig.Provider = AIProvider.Local;
        }

        // Add description for local provider
        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect localDescRect = listingStandard.GetRect(Text.LineHeight);
        Widgets.Label(localDescRect, "RimTalk.Settings.LocalProviderDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        listingStandard.Gap();

        // Draw appropriate section based on selection
        if (settings.UseCloudProviders)
        {
            DrawCloudProvidersSection(listingStandard, settings);
        }
        else
        {
            DrawLocalProviderSection(listingStandard, settings);
        }
    }

    private void DrawCloudProvidersSection(Listing_Standard listingStandard, RimTalkSettings settings)
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
        Widgets.Label(cloudDescRect, "RimTalk.Settings.CloudApiConfigurationsDesc".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        if (Widgets.ButtonText(addButtonRect, "+"))
        {
            settings.CloudConfigs.Add(new ApiConfig());
        }

        // Only show remove button if there are configs to remove and more than 1
        GUI.enabled = settings.CloudConfigs.Count > 1;
        if (Widgets.ButtonText(removeButtonRect, "−"))
        {
            // Remove the last configuration
            if (settings.CloudConfigs.Count > 1)
            {
                settings.CloudConfigs.RemoveAt(settings.CloudConfigs.Count - 1);
            }
        }

        GUI.enabled = true;

        listingStandard.Gap(6f);

        Rect tableHeaderRect = listingStandard.GetRect(24f);
        float x = tableHeaderRect.x;
        float y = tableHeaderRect.y;
        float height = tableHeaderRect.height;

        x += 60f;

        float providerWidth = 100f;
        float apiKeyWidth = 240f;
        float modelWidth = 200f;
        float baseUrlWidth = 355f;

        Rect providerHeaderRect = new Rect(x, y, providerWidth, height);
        Widgets.Label(providerHeaderRect, "RimTalk.Settings.ProviderHeader".Translate());
        x += providerWidth + 5f;

        Rect apiKeyHeaderRect = new Rect(x, y, apiKeyWidth, height);
        Widgets.Label(apiKeyHeaderRect, "RimTalk.Settings.ApiKeyHeader".Translate());
        x += apiKeyWidth + 5f;

        Rect modelHeaderRect = new Rect(x, y, modelWidth, height);
        Widgets.Label(modelHeaderRect, "RimTalk.Settings.ModelHeader".Translate());
        x += modelWidth + 5f;

        if (settings.CloudConfigs.Any(c => c.Provider == AIProvider.Custom))
        {
            Rect baseUrlHeaderRect = new Rect(x, y, baseUrlWidth, height);
            var labelText = "RimTalk.Settings.BaseUrlLabel".Translate() + " [?]";
            Widgets.Label(baseUrlHeaderRect, labelText);
            TooltipHandler.TipRegion(baseUrlHeaderRect, "RimTalk_Settings_Api_BaseUrlInfo".Translate());
            x += baseUrlWidth + 5f;
        }

        Rect enabledHeaderRect = new Rect(tableHeaderRect.xMax - 70f, y, 70f, height);
        Widgets.Label(enabledHeaderRect, "RimTalk.Settings.EnabledHeader".Translate());

        listingStandard.Gap(6f);

        for (int i = 0; i < settings.CloudConfigs.Count; i++)
        {
            DrawCloudConfigRow(listingStandard, settings.CloudConfigs[i], i, settings.CloudConfigs);
            listingStandard.Gap(3f);
        }
    }

    private void DrawCloudConfigRow(Listing_Standard listingStandard, ApiConfig config, int index, List<ApiConfig> configs)
    {
        Rect rowRect = listingStandard.GetRect(24f);
        float x = rowRect.x;
        float y = rowRect.y;
        float height = rowRect.height;

        DrawReorderButtons(ref x, y, height, index, configs);
        DrawProviderDropdown(ref x, y, height, config);
        DrawApiKeyInput(ref x, y, height, config);

        if (config.Provider == AIProvider.Custom)
        {
            DrawCustomProviderRow(ref x, y, height, config);
        }
        else
        {
            DrawDefaultProviderRow(ref x, y, height, config);
        }

        DrawEnableToggle(rowRect, y, height, config);
    }

    private void DrawReorderButtons(ref float x, float y, float height, int index, List<ApiConfig> configs)
    {
        Rect upButtonRect = new Rect(x, y, 24f, height);
        if (Widgets.ButtonText(upButtonRect, "▲") && index > 0)
        {
            (configs[index], configs[index - 1]) = (configs[index - 1], configs[index]);
        }
        x += 30f;

        Rect downButtonRect = new Rect(x, y, 24f, height);
        if (Widgets.ButtonText(downButtonRect, "▼") && index < configs.Count - 1)
        {
            (configs[index], configs[index + 1]) = (configs[index + 1], configs[index]);
        }
        x += 30f;
    }

    private void DrawProviderDropdown(ref float x, float y, float height, ApiConfig config)
    {
        Rect providerRect = new Rect(x, y, 100f, height);
        if (Widgets.ButtonText(providerRect, config.Provider.ToString()))
        {
            List<FloatMenuOption> providerOptions =
            [
                new(nameof(AIProvider.Google), () => {
                    config.Provider = AIProvider.Google;
                    config.SelectedModel = Constant.ChooseModel;
                }),
                new(nameof(AIProvider.OpenAI), () => {
                    config.Provider = AIProvider.OpenAI;
                    config.SelectedModel = Constant.ChooseModel;
                }),
                new(nameof(AIProvider.DeepSeek), () => {
                    config.Provider = AIProvider.DeepSeek;
                    config.SelectedModel = Constant.ChooseModel;
                }),
                new(nameof(AIProvider.Grok), () => {
                    config.Provider = AIProvider.Grok;
                    config.SelectedModel = Constant.ChooseModel;
                }),
                new(nameof(AIProvider.OpenRouter), () => {
                    config.Provider = AIProvider.OpenRouter;
                    config.SelectedModel = Constant.ChooseModel;
                }),
                new(nameof(AIProvider.Player2), () => {
                    config.Provider = AIProvider.Player2;
                    config.SelectedModel = "Default";
                    Player2Client.CheckPlayer2StatusAndNotify();
                }),
                new(nameof(AIProvider.Custom), () => {
                    config.Provider = AIProvider.Custom;
                    config.SelectedModel = "Custom";
                })
            ];
            Find.WindowStack.Add(new FloatMenu(providerOptions));
        }
        x += 105f;
    }

    private void DrawApiKeyInput(ref float x, float y, float height, ApiConfig config)
    {
        Rect apiKeyRect = new Rect(x, y, 240f, height);
        config.ApiKey = Widgets.TextField(apiKeyRect, config.ApiKey);
        x += 245f;
    }

    private void DrawCustomProviderRow(ref float x, float y, float height, ApiConfig config)
    {
        Rect customModelRect = new Rect(x, y, 200f, height);
        config.CustomModelName = Widgets.TextField(customModelRect, config.CustomModelName);
        x += 205f;

        Rect baseUrlRect = new Rect(x, y, 150f, height);
        config.BaseUrl = Widgets.TextField(baseUrlRect, config.BaseUrl);
        x += 155f;
    }

    private void DrawDefaultProviderRow(ref float x, float y, float height, ApiConfig config)
    {
        Rect modelRect = new Rect(x, y, 200f, height);
        if (config.SelectedModel == "Custom")
        {
            config.CustomModelName = Widgets.TextField(modelRect, config.CustomModelName);
            Rect backButton = new Rect(modelRect.xMax + 5, y, 24, height);
            if (Widgets.ButtonText(backButton, "X"))
            {
                config.SelectedModel = Constant.ChooseModel;
            }
        }
        else
        {
            if (Widgets.ButtonText(modelRect, config.SelectedModel))
            {
                ShowModelSelectionMenu(config);
            }
        }
        x += 205f;
    }

    private void ShowModelSelectionMenu(ApiConfig config)
    {
        // Allow Player2 to work without API key (local app detection)
        if (string.IsNullOrWhiteSpace(config.ApiKey) && config.Provider != AIProvider.Player2)
        {
            Find.WindowStack.Add(new FloatMenu([new FloatMenuOption("RimTalk.Settings.EnterApiKey".Translate(), null)]));
            return;
        }

        if (config.Provider == AIProvider.Player2)
        {
            config.SelectedModel = "Default";
            return;
        }

        string url = GetModelApiUrl(config.Provider);
        if (string.IsNullOrEmpty(url)) return;
        
        void OpenMenu(List<string> models)
        {
            var options = new List<FloatMenuOption>();

            if (models != null && models.Any())
            {
                options.AddRange(models.Select(model => new FloatMenuOption(model, () => config.SelectedModel = model)));
            }
            else
            {
                options.Add(new FloatMenuOption("(no models found - check API Key)", null));
            }

            options.Add(new FloatMenuOption("Custom", () => config.SelectedModel = "Custom"));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        if (ModelCache.ContainsKey(url))
        {
            OpenMenu(ModelCache[url]);
        }
        else
        {
            Task<List<string>> fetchTask = config.Provider == AIProvider.Google
                ? GeminiClient.FetchModelsAsync(config.ApiKey, url)
                : OpenAIClient.FetchModelsAsync(config.ApiKey, url);

            fetchTask.ContinueWith(task =>
            {
                var models = task.Result;
                if (models != null && models.Any())
                {
                    ModelCache[url] = models;
                }
                OpenMenu(models);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private string GetModelApiUrl(AIProvider provider)
    {
        switch (provider)
        {
            case AIProvider.Google: return "https://generativelanguage.googleapis.com/v1beta/models";
            case AIProvider.OpenAI: return "https://api.openai.com/v1/models";
            case AIProvider.DeepSeek: return "https://api.deepseek.com/models";
            case AIProvider.Grok: return "https://api.x.ai/v1/models";
            case AIProvider.OpenRouter: return "https://openrouter.ai/api/v1/models";
            case AIProvider.Player2:
            default: return null;
        }
    }

    private void DrawEnableToggle(Rect rowRect, float y, float height, ApiConfig config)
    {
        Rect toggleRect = new Rect(rowRect.xMax - 70f, y, 24f, height);
        Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref config.IsEnabled);
        if (Mouse.IsOver(toggleRect))
        {
            TooltipHandler.TipRegion(toggleRect, "RimTalk.Settings.EnableDisableApiConfigTooltip".Translate());
        }
    }

    private void DrawLocalProviderSection(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        listingStandard.Label("RimTalk.Settings.LocalProviderConfiguration".Translate());
        listingStandard.Gap(6f);

        if (settings.LocalConfig == null)
        {
            settings.LocalConfig = new ApiConfig { Provider = AIProvider.Local };
        }

        DrawLocalConfigRow(listingStandard, settings.LocalConfig);
    }

    private void DrawLocalConfigRow(Listing_Standard listingStandard, ApiConfig config)
    {
        Rect rowRect = listingStandard.GetRect(24f);
        float x = rowRect.x;
        float y = rowRect.y;
        float height = rowRect.height;

        Rect baseUrlLabelRect = new Rect(x, y, 80f, height);
        var labelText = "RimTalk.Settings.BaseUrlLabel".Translate() + " [?]";
        Widgets.Label(baseUrlLabelRect, labelText);
        TooltipHandler.TipRegion(baseUrlLabelRect, "RimTalk_Settings_Api_BaseUrlInfo".Translate());
        x += 85f;

        Rect urlRect = new Rect(x, y, 250f, height);
        config.BaseUrl = Widgets.TextField(urlRect, config.BaseUrl);
        x += 285f;

        Rect modelLabelRect = new Rect(x, y, 70f, height);
        Widgets.Label(modelLabelRect, "RimTalk.Settings.ModelLabel".Translate());
        x += 75f;

        Rect modelRect = new Rect(x, y, 200f, height);
        config.CustomModelName = Widgets.TextField(modelRect, config.CustomModelName);
    }
}