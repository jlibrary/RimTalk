using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;
using RimTalk.Data;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalk;

public partial class Settings
{
    private static readonly Dictionary<string, List<string>> ModelCache = new();

    private bool DrawApiModeCard(Rect rect, string title, string desc, bool isSelected)
    {
        // Background
        Widgets.DrawBoxSolid(rect, isSelected ? new Color(0.2f, 0.4f, 0.6f, 0.85f) : new Color(0.18f, 0.18f, 0.18f, 0.6f));

        // Border
        GUI.color = isSelected ? new Color(0.4f, 0.75f, 1f, 1f) : new Color(0.35f, 0.35f, 0.35f, 0.6f);
        Widgets.DrawBox(rect, isSelected ? 2 : 1);
        GUI.color = Color.white;

        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

        bool clicked = Widgets.ButtonInvisible(rect);

        Rect content = rect.ContractedBy(5f);
        Text.Anchor = TextAnchor.UpperCenter;

        // Title
        Text.Font = GameFont.Small;
        GUI.color = isSelected ? Color.white : new Color(0.85f, 0.85f, 0.85f);
        Widgets.Label(new Rect(content.x, content.y + 2f, content.width, Text.LineHeight), title);

        // Subtitle / Desc
        Text.Font = GameFont.Tiny;
        GUI.color = isSelected ? new Color(0.8f, 0.92f, 1f) : new Color(0.6f, 0.6f, 0.6f);
        Widgets.Label(new Rect(content.x, content.y + Text.LineHeight + 2f, content.width, content.height - Text.LineHeight - 2f), desc);

        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        return clicked;
    }

    private void DrawApiModeSelector(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        // Guide Header
        Rect headerRect = listingStandard.GetRect(Text.LineHeight);
        GUI.color = Color.gray;
        Text.Font = GameFont.Tiny;
        Widgets.Label(headerRect, "RimTalk.Settings.ModeSelectorHeader".Translate());
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(2f);

        const float cardGap = 8f;
        const float cardHeight = 52f;
        float cardWidth = (listingStandard.ColumnWidth - cardGap * 2f) / 3f;
        Rect rowRect = listingStandard.GetRect(cardHeight);

        Rect googleCard = new Rect(rowRect.x, rowRect.y, cardWidth, cardHeight);
        Rect player2Card = new Rect(rowRect.x + cardWidth + cardGap, rowRect.y, cardWidth, cardHeight);
        Rect advancedCard = new Rect(rowRect.x + (cardWidth + cardGap) * 2f, rowRect.y, cardWidth, cardHeight);

        bool isGoogle = settings.UseSimpleConfig && settings.SimpleProvider == AIProvider.Google;
        bool isPlayer2 = settings.UseSimpleConfig && settings.SimpleProvider == AIProvider.Player2;
        bool isAdvanced = !settings.UseSimpleConfig;

        // 1. Google Gemini Card
        if (DrawApiModeCard(googleCard, "RimTalk.Settings.ModeGoogleTitle".Translate(), "RimTalk.Settings.ModeGoogleDesc".Translate(), isGoogle))
        {
            settings.UseSimpleConfig = true;
            settings.SimpleProvider = AIProvider.Google;
        }

        // 2. Player2 Card
        if (DrawApiModeCard(player2Card, "RimTalk.Settings.ModePlayer2Title".Translate(), "RimTalk.Settings.ModePlayer2Desc".Translate(), isPlayer2))
        {
            settings.UseSimpleConfig = true;
            settings.SimpleProvider = AIProvider.Player2;
        }

        // 3. Advanced Card
        if (DrawApiModeCard(advancedCard, "RimTalk.Settings.ModeAdvancedTitle".Translate(), "RimTalk.Settings.ModeAdvancedDesc".Translate(), isAdvanced))
        {
            settings.UseSimpleConfig = false;
        }
    }

    private void DrawSimpleApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        if (settings.SimpleProvider == AIProvider.Google)
        {
            // Google Section (Default)
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

            // Description
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(cloudDescRect, "RimTalk.Settings.GoogleApiKeyDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
        else
        {
            // Player2 Section: Split Cards (Symmetrical Bottom Buttons)
            const float boxHeight = 125f;
            const float cardGap = 12f;
            const float padding = 10f;
            const float btnHeight = 28f;

            Rect totalBox = listingStandard.GetRect(boxHeight);
            // Left & Right Cards: symmetrical split aligning with listingStandard.ColumnWidth
            float cardW = (listingStandard.ColumnWidth - cardGap) / 2f;

            Rect leftCard = new Rect(totalBox.x, totalBox.y, cardW, boxHeight);
            Rect rightCard = new Rect(totalBox.x + cardW + cardGap, totalBox.y, cardW, boxHeight);

            bool? status = Player2Client.GetLocalAppStatusCached();

            bool isLeftActive = (status == true);
            bool isRightActive = !string.IsNullOrEmpty(settings.SimplePlayer2ApiKey);
            bool isAppRunning = isLeftActive;

            // Color palettes (Muted Emerald/Green theme for both cards)
            Color greenActiveBg = new Color(0.12f, 0.24f, 0.20f, 0.5f);
            Color greenActiveBorder = new Color(0.25f, 0.68f, 0.52f, 0.85f);

            // Inactive & Dimmed
            Color inactiveBg = new Color(0.12f, 0.14f, 0.17f, 0.5f);
            Color inactiveBorder = new Color(0.3f, 0.35f, 0.42f, 0.5f);
            Color dimmedBg = new Color(0.08f, 0.09f, 0.11f, 0.4f);
            Color dimmedBorder = new Color(0.22f, 0.25f, 0.28f, 0.4f);

            // 1. Left Card: Option 1 - Desktop App
            Widgets.DrawBoxSolid(leftCard, isLeftActive ? greenActiveBg : inactiveBg);
            GUI.color = isLeftActive ? greenActiveBorder : inactiveBorder;
            Widgets.DrawBox(leftCard, 1);
            GUI.color = Color.white;

            Rect leftInner = leftCard.ContractedBy(padding);
            // Left Content: Title
            Rect leftTitleRect = new Rect(leftInner.x, leftInner.y, leftInner.width, 22f);
            Text.Font = GameFont.Small;
            Widgets.Label(leftTitleRect, "RimTalk.Settings.Player2AppTitle".Translate());

            // Left Status Row (matches inputRow height 24f and Y position for alignment)
            Rect statusRow = new Rect(leftInner.x, leftTitleRect.yMax + 1f, leftInner.width, 24f);
            GUI.color = isLeftActive ? new Color(0.4f, 0.85f, 0.65f) : Color.gray;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(statusRow, isLeftActive ? "RimTalk.Settings.Player2StatusConnected".Translate() : "RimTalk.Settings.Player2StatusDisconnected".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Left Description Row (matches rightDescRect height 18f and Y position)
            Rect leftDescRect = new Rect(leftInner.x, statusRow.yMax + 2f, leftInner.width, 18f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(leftDescRect, "RimTalk.Settings.Player2AppDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Left Bottom Button
            Rect leftBtnRect = new Rect(leftInner.x, leftCard.yMax - padding - btnHeight, leftInner.width, btnHeight);
            if (Widgets.ButtonText(leftBtnRect, "RimTalk.Settings.Player2DownloadApp".Translate()))
            {
                Application.OpenURL("https://player2.game");
            }

            // 2. Right Card: Option 2 - Web API Key (shares same green theme)
            Widgets.DrawBoxSolid(rightCard, isAppRunning ? dimmedBg : (isRightActive ? greenActiveBg : inactiveBg));
            GUI.color = isAppRunning ? dimmedBorder : (isRightActive ? greenActiveBorder : inactiveBorder);
            Widgets.DrawBox(rightCard, 1);
            GUI.color = Color.white;

            Rect rightInner = rightCard.ContractedBy(padding);
            // Right Content
            Rect rightTitleRect = new Rect(rightInner.x, rightInner.y, rightInner.width, 22f);
            Text.Font = GameFont.Small;
            if (isAppRunning) GUI.color = Color.gray;
            Widgets.Label(rightTitleRect, "RimTalk.Settings.Player2WebTitle".Translate());
            GUI.color = Color.white;

            // Right Content: inputs and buttons remain usable even if visually subordinated
            Rect inputRow = new Rect(rightInner.x, rightTitleRect.yMax + 1f, rightInner.width, 24f);
            settings.SimplePlayer2ApiKey = Widgets.TextField(inputRow, settings.SimplePlayer2ApiKey);

            Rect rightDescRect = new Rect(rightInner.x, inputRow.yMax + 2f, rightInner.width, 18f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(rightDescRect, "RimTalk.Settings.Player2WebDesc".Translate());
            GUI.color = Color.white;

            // Right Bottom Button
            Rect rightBtnRect = new Rect(rightInner.x, rightCard.yMax - padding - btnHeight, rightInner.width, btnHeight);
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(rightBtnRect, "RimTalk.Settings.Player2GetWebKey".Translate()))
            {
                Application.OpenURL("https://gerikuylerk.com/RimTalk");
            }
        }
    }

    private void DrawAdvancedApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

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
        Rect headerRect = listingStandard.GetRect(24f);

        // Header with add button
        float addBtnSize = 24f; 
        Rect addButtonRect = new Rect(headerRect.x + headerRect.width - addBtnSize, headerRect.y, addBtnSize, addBtnSize);
        headerRect.width -= (addBtnSize + 5f); 

        Widgets.Label(headerRect, "RimTalk.Settings.CloudApiConfigurations".Translate());

        Text.Font = GameFont.Tiny;
        GUI.color = Color.gray;
        Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight * 2);
        cloudDescRect.width -= 35f;
        Widgets.Label(cloudDescRect, "RimTalk.Settings.CloudApiConfigurationsDesc".Translate());
        GUI.color = Color.white;

        // Draw Add Button (+)
        Color prevColor = GUI.color;
        GUI.color = new Color(0.3f, 0.9f, 0.3f);
        if (Widgets.ButtonText(addButtonRect, "+"))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            settings.CloudConfigs.Add(new ApiConfig());
        }
        GUI.color = prevColor;
        
        listingStandard.Gap(6f);

        // --- Table Headers ---
        Rect tableHeaderRect = listingStandard.GetRect(20f);
        float x = tableHeaderRect.x;
        float y = tableHeaderRect.y;
        float height = tableHeaderRect.height;
        float totalWidth = tableHeaderRect.width;

        float providerWidth = 90f;
        float modelWidth = 190f; 
        float controlsWidth = 125f; 

        Rect providerHeaderRect = new Rect(x, y, providerWidth, height);
        Widgets.Label(providerHeaderRect, "RimTalk.Settings.ProviderHeader".Translate());
        
        float middleStartX = x + providerWidth + 5f;
        Rect apiKeyHeaderRect = new Rect(middleStartX, y, 200f, height);
        Widgets.Label(apiKeyHeaderRect, "RimTalk.Settings.ApiKeyHeader".Translate());

        Rect modelHeaderRect = new Rect(totalWidth - controlsWidth - modelWidth - 5f, y, modelWidth, height);
        Widgets.Label(modelHeaderRect, "RimTalk.Settings.ModelHeader".Translate());

        Rect enabledHeaderRect = new Rect(totalWidth - controlsWidth + 5f, y, controlsWidth, height);
        Widgets.Label(enabledHeaderRect, "RimTalk.Settings.EnabledHeader".Translate());

        listingStandard.Gap(3f);

        for (int i = 0; i < settings.CloudConfigs.Count; i++)
        {
            if (DrawCloudConfigRow(listingStandard, settings.CloudConfigs[i], i, settings.CloudConfigs))
            {
                settings.CloudConfigs.RemoveAt(i);
                i--;
            }
            listingStandard.Gap(2f);
        }

        Text.Font = GameFont.Small;
    }

    private bool DrawCloudConfigRow(Listing_Standard listingStandard, ApiConfig config, int index, List<ApiConfig> configs)
    {
        Text.Font = GameFont.Tiny;

        Rect rowRect = listingStandard.GetRect(22f);
        float x = rowRect.x;
        float y = rowRect.y;
        float height = rowRect.height;
        float totalWidth = rowRect.width;

        float providerWidth = 90f;
        float modelWidth = 190f;
        float controlsWidth = 125f;
        float gap = 5f;

        float middleZoneWidth = totalWidth - providerWidth - modelWidth - controlsWidth - (gap * 3);
        float middleStartX = x + providerWidth + gap;

        Color originalColor = GUI.color;
        if (!config.IsEnabled)
        {
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
        }

        // 1. Provider
        DrawProviderDropdown(x, y, height, providerWidth, config);
        
        // 2. Middle Zone
        if (config.Provider == AIProvider.Custom)
        {
            float keyWidth = (middleZoneWidth * 0.4f) - (gap / 2);
            float urlWidth = (middleZoneWidth * 0.6f) - (gap / 2);

            DrawApiKeyInput(middleStartX, y, height, keyWidth, config);
            DrawBaseUrlInput(middleStartX + keyWidth + gap, y, height, urlWidth, config);
        }
        else
        {
            DrawApiKeyInput(middleStartX, y, height, middleZoneWidth, config);
        }

        // 3. Model
        float modelStartX = middleStartX + middleZoneWidth + gap;
        if (config.Provider == AIProvider.Custom)
        {
            DrawCustomModelInput(modelStartX, y, height, modelWidth, config);
        }
        else
        {
            DrawDefaultModelSelector(modelStartX, y, height, modelWidth, config);
        }

        GUI.color = originalColor;

        // 4. Controls
        float btnSize = 22f;
        float btnGap = 2f;

        float deleteX = totalWidth - btnSize; 
        float downX = deleteX - btnGap - btnSize;
        float upX = downX - btnGap - btnSize;
        float customX = upX - btnGap - btnSize;

        float controlsStartX = totalWidth - controlsWidth;
        float checkboxSpaceWidth = customX - controlsStartX;
        
        float checkboxX = controlsStartX + (checkboxSpaceWidth - 24f) / 2f;
        
        Rect toggleRect = new Rect(checkboxX, y, 24f, height);
        Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref config.IsEnabled, 20f);
        if (Mouse.IsOver(toggleRect)) TooltipHandler.TipRegion(toggleRect, "Enable/Disable");

        // Customize Button (OptionsGeneral Icon)
        Rect customRect = new Rect(customX, y, btnSize, height);
        var iconTexture = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
        bool hasCustom = !string.IsNullOrWhiteSpace(config.CustomRequestJson);
        Color iconColor = hasCustom ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
        Color mouseoverColor = hasCustom ? new Color(0.6f, 1f, 0.7f) : GenUI.MouseoverColor;

        if (Widgets.ButtonImage(customRect, iconTexture, iconColor, mouseoverColor))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            Find.WindowStack.Add(new Dialog_CustomizeRequest(config));
        }
        TooltipHandler.TipRegion(customRect, "RimTalk.Settings.CustomizeRequestTooltip".Translate());

        DrawReorderButtons(upX, y, height, index, configs);

        Rect deleteRect = new Rect(deleteX, y, btnSize, height);
        bool deleteClicked = false;
        bool canDelete = configs.Count > 1;

        Color prevColor = GUI.color;
        if (canDelete)
        {
            GUI.color = new Color(1f, 0.4f, 0.4f);
        }
        else
        {
            GUI.color = Color.gray;
        }

        if (Widgets.ButtonText(deleteRect, "×", active: canDelete))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            deleteClicked = true;
        }
        GUI.color = prevColor;

        Text.Font = GameFont.Tiny;
        return deleteClicked;
    }

    private void DrawReorderButtons(float x, float y, float height, int index, List<ApiConfig> configs)
    {
        float btnSize = 22f;
        Rect upButtonRect = new Rect(x, y, btnSize, height);
        
        if (Widgets.ButtonText(upButtonRect, "▲") && index > 0)
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            (configs[index], configs[index - 1]) = (configs[index - 1], configs[index]);
        }

        Rect downButtonRect = new Rect(x + btnSize + 2f, y, btnSize, height);

        if (Widgets.ButtonText(downButtonRect, "▼") && index < configs.Count - 1)
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            (configs[index], configs[index + 1]) = (configs[index + 1], configs[index]);
        }
    }

    private void DrawDefaultModelSelector(float x, float y, float height, float width, ApiConfig config)
    {
        Rect modelRect = new Rect(x, y, width, height);
        if (config.SelectedModel == "Custom")
        {
            float xButtonWidth = 22f;
            float textFieldWidth = width - xButtonWidth - 2f;

            Rect textFieldRect = new Rect(x, y, textFieldWidth, height);
            Rect backButtonRect = new Rect(x + textFieldWidth + 2f, y, xButtonWidth, height);

            config.CustomModelName = DrawTextFieldWithPlaceholder(textFieldRect, config.CustomModelName, "Model ID");
            
            if (Widgets.ButtonText(backButtonRect, "×"))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                config.SelectedModel = Constant.ChooseModel;
            }
        }
        else
        {
            string label = config.SelectedModel;
            if (config.Provider == AIProvider.Player2)
            {
                bool? status = Player2Client.GetLocalAppStatusCached();
                if (status == true)
                    label = "Desktop App";
                else if (!string.IsNullOrEmpty(config.ApiKey))
                    label = "Web API";
                else
                    label = "Default";
            }

            if (Widgets.ButtonText(modelRect, label))
            {
                ShowModelSelectionMenu(config);
            }
        }
    }

    private string DrawTextFieldWithPlaceholder(Rect rect, string text, string placeholder)
    {
        string result = Widgets.TextField(rect, text);
        
        if (string.IsNullOrEmpty(result))
        {
            TextAnchor originalAnchor = Text.Anchor;
            Color originalColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f); 
            
            Rect labelRect = new Rect(rect.x + 5f, rect.y, rect.width - 5f, rect.height);
            Widgets.Label(labelRect, placeholder);

            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
        }

        return result;
    }

    private static readonly AIProvider[] DropdownProviders =
    [
        AIProvider.Google,
        AIProvider.Player2,
        AIProvider.OpenAI,
        AIProvider.DeepSeek,
        AIProvider.Grok,
        AIProvider.GLM,
        AIProvider.GLMCoding,
        AIProvider.OpenRouter,
        AIProvider.AlibabaIntl,
        AIProvider.AlibabaCN,
        AIProvider.Custom
    ];

    private void DrawProviderDropdown(float x, float y, float height, float width, ApiConfig config)
    {
        Rect providerRect = new Rect(x, y, width, height);
        if (Widgets.ButtonText(providerRect, config.Provider.GetLabel()))
        {
            List<FloatMenuOption> providerOptions = [];
            foreach (AIProvider provider in DropdownProviders)
            {
                providerOptions.Add(new FloatMenuOption(provider.GetLabel(), () =>
                {
                    config.Provider = provider;
                    switch (provider)
                    {
                        case AIProvider.Player2:
                            config.SelectedModel = "Default";
                            break;
                        case AIProvider.Custom:
                            config.SelectedModel = "Custom";
                            break;
                        default:
                            config.SelectedModel = Constant.ChooseModel;
                            break;
                    }
                }));
            }
            Find.WindowStack.Add(new FloatMenu(providerOptions));
        }
    }

    private void DrawApiKeyInput(float x, float y, float height, float width, ApiConfig config)
    {
        Rect apiKeyRect = new Rect(x, y, width, height);
        config.ApiKey = DrawTextFieldWithPlaceholder(apiKeyRect, config.ApiKey, "Paste API Key...");
    }

    private void DrawBaseUrlInput(float x, float y, float height, float width, ApiConfig config)
    {
        Rect baseUrlRect = new Rect(x, y, width, height);
        config.BaseUrl = DrawTextFieldWithPlaceholder(baseUrlRect, config.BaseUrl, "https://...");
        if (Mouse.IsOver(baseUrlRect)) TooltipHandler.TipRegion(baseUrlRect, "RimTalk_Settings_Api_BaseUrlInfo".Translate());
    }

    private void DrawCustomModelInput(float x, float y, float height, float width, ApiConfig config)
    {
        Rect customModelRect = new Rect(x, y, width, height);
        config.CustomModelName = DrawTextFieldWithPlaceholder(customModelRect, config.CustomModelName, "Model ID");
        config.SelectedModel = string.IsNullOrWhiteSpace(config.CustomModelName)
            ? Constant.ChooseModel
            : config.CustomModelName;
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

        string url = config.Provider.GetListModelsUrl();
        if (string.IsNullOrEmpty(url)) return;
        
        void OpenMenu(List<string> models)
        {
            var options = new List<FloatMenuOption>();

            if (models != null && models.Any())
            {
                // Sorted here (not in FetchModelsAsync) so the cached path benefits too.
                // OpenRouter alone returns 400+ models in whatever order its API feels like.
                var sorted = models
                    .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(model => model, StringComparer.Ordinal);

                options.AddRange(sorted.Select(model => new FloatMenuOption(model, () => config.SelectedModel = model)));
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
            Task<List<string>> fetchTask = OpenAIClient.FetchModelsAsync(config.ApiKey, url);

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
        x += 205f;

        // Customize Button (OptionsGeneral Icon)
        Rect customRect = new Rect(x, y + 1f, 22f, 22f);
        var iconTexture = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
        bool hasCustom = !string.IsNullOrWhiteSpace(config.CustomRequestJson);
        Color iconColor = hasCustom ? new Color(0.4f, 0.9f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
        Color mouseoverColor = hasCustom ? new Color(0.6f, 1f, 0.7f) : GenUI.MouseoverColor;

        if (Widgets.ButtonImage(customRect, iconTexture, iconColor, mouseoverColor))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            Find.WindowStack.Add(new Dialog_CustomizeRequest(config));
        }
        TooltipHandler.TipRegion(customRect, "RimTalk.Settings.CustomizeRequestTooltip".Translate());
    }
}