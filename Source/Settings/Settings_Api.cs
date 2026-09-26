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
using Logger = RimTalk.Util.Logger;

namespace RimTalk;

public partial class Settings
{
    private static readonly Dictionary<string, List<string>> ModelCache = new();

    private bool DrawApiModeCard(Rect rect, string title, string desc, bool isSelected)
    {
        rect = rect.Rounded();
        // Background
        Widgets.DrawBoxSolid(rect, isSelected ? new Color(0.22f, 0.33f, 0.45f, 0.90f) : new Color(0.10f, 0.11f, 0.13f, 0.85f));

        // Border
        GUI.color = isSelected ? new Color(0.40f, 0.65f, 0.85f, 0.95f) : SectionBorderColor;
        Widgets.DrawBox(rect, 1);
        GUI.color = Color.white;

        if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

        if (!string.IsNullOrEmpty(desc))
        {
            TooltipHandler.TipRegion(rect, desc);
        }

        bool clicked = Widgets.ButtonInvisible(rect);
        if (clicked)
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
        }

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

    private void DrawQuickApiModeSelector(Listing_Standard listingStandard, RimTalkSettings settings)
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
        float cardWidth = (listingStandard.ColumnWidth - cardGap) / 2f;
        Rect rowRect = listingStandard.GetRect(cardHeight);

        Rect googleCard = new Rect(rowRect.x, rowRect.y, cardWidth, cardHeight);
        Rect player2Card = new Rect(rowRect.x + cardWidth + cardGap, rowRect.y, cardWidth, cardHeight);

        bool isGoogle = settings.UseSimpleConfig && settings.SimpleProvider == AIProvider.Google;
        bool isPlayer2 = settings.UseSimpleConfig && settings.SimpleProvider == AIProvider.Player2;

        if (DrawApiModeCard(googleCard, "RimTalk.Settings.ModeGoogleTitle".Translate(), "RimTalk.Settings.ModeGoogleDesc".Translate(), isGoogle))
        {
            settings.UseSimpleConfig = true;
            settings.SimpleProvider = AIProvider.Google;
        }

        if (DrawApiModeCard(player2Card, "RimTalk.Settings.ModePlayer2Title".Translate(), "RimTalk.Settings.ModePlayer2Desc".Translate(), isPlayer2))
        {
            settings.UseSimpleConfig = true;
            settings.SimpleProvider = AIProvider.Player2;
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
            if (UIUtil.ButtonText(buttonRect, "RimTalk.Settings.GetFreeApiKeyButton".Translate()))
            {
                Application.OpenURL("https://aistudio.google.com/app/apikey");
            }

            // Description
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listingStandard.GetRect(Text.LineHeight);
            Widgets.Label(cloudDescRect, "RimTalk.Settings.GoogleApiKeyDesc".Translate(Constant.DefaultCloudModel));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
        else
        {
            // Player2 Section: Split Cards (Symmetrical Bottom Buttons)
            const float boxHeight = 125f;
            const float cardGap = 8f;
            const float padding = 10f;
            const float btnHeight = 28f;

            Rect totalBox = listingStandard.GetRect(boxHeight);
            // Left & Right Cards: symmetrical split aligning with listingStandard.ColumnWidth
            float cardW = (listingStandard.ColumnWidth - cardGap) / 2f;

            Rect leftCard = new Rect(totalBox.x, totalBox.y, cardW, boxHeight);
            Rect rightCard = new Rect(totalBox.x + cardW + cardGap, totalBox.y, cardW, boxHeight);

            bool? status = Player2Client.GetLocalAppStatusCached();

            bool isSimpleActive = settings.UseSimpleConfig && settings.SimpleProvider == AIProvider.Player2;
            bool isLeftActive = isSimpleActive && (status == true);
            bool isRightActive = isSimpleActive && !string.IsNullOrEmpty(settings.SimplePlayer2ApiKey);
            bool isAppRunning = (status == true);

            // Color palettes (Muted Forest/Olive Green with soft transparency)
            Color greenActiveBg = new Color(0.10f, 0.20f, 0.13f, 0.75f);
            Color greenActiveBorder = new Color(0.30f, 0.58f, 0.36f, 0.90f);

            // Inactive & Dimmed
            Color inactiveBg = new Color(0.10f, 0.11f, 0.13f, 0.85f);
            Color inactiveBorder = SectionBorderColor;
            Color dimmedBg = new Color(0.08f, 0.09f, 0.11f, 0.7f);
            Color dimmedBorder = SectionBorderColor;

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
            GUI.color = isLeftActive ? new Color(0.52f, 0.78f, 0.38f) : Color.gray;
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
            if (UIUtil.ButtonText(leftBtnRect, "RimTalk.Settings.Player2DownloadApp".Translate()))
            {
                Application.OpenURL("https://player2.game");
            }

            // 2. Right Card: Option 2 - Web API Key
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

            bool isAuthenticating = Player2AuthService.IsAuthenticating;
            string btnLabel = isAuthenticating
                ? "RimTalk.Settings.Player2AuthWaiting".Translate()
                : "RimTalk.Settings.Player2GetWebKey".Translate();

            if (UIUtil.ButtonText(rightBtnRect, btnLabel))
            {
                if (isAuthenticating)
                {
                    List<FloatMenuOption> authOptions =
                    [
                        new("RimTalk.Settings.Player2AuthReopen".Translate(), () =>
                        {
                            if (!string.IsNullOrEmpty(Player2AuthService.ApprovalUrl))
                            {
                                Application.OpenURL(Player2AuthService.ApprovalUrl);
                            }
                        }),
                        new("RimTalk.Settings.Player2AuthCancel".Translate(), () =>
                        {
                            Player2AuthService.Cancel();
                        })
                    ];
                    Find.WindowStack.Add(new FloatMenu(authOptions));
                }
                else
                {
                    Player2AuthService.StartAuth();
                }
            }
        }
    }

    private void DrawAdvancedApiSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // 1-Row Segmented Switch (Cloud vs Local) + Add Button on the right
        Rect modeRow = listingStandard.GetRect(28f);
        const float segWidth = 180f;
        Rect cloudSegRect = new Rect(modeRow.x, modeRow.y, segWidth, 28f);
        Rect localSegRect = new Rect(cloudSegRect.xMax + 4f, modeRow.y, segWidth, 28f);

        bool isCloud = settings.UseCloudProviders;
        Widgets.DrawBoxSolid(cloudSegRect, isCloud ? new Color(0.22f, 0.33f, 0.45f, 0.90f) : new Color(0.10f, 0.11f, 0.13f, 0.85f));
        GUI.color = isCloud ? new Color(0.40f, 0.65f, 0.85f, 0.95f) : SectionBorderColor;
        Widgets.DrawBox(cloudSegRect, 1);
        GUI.color = Color.white;
        if (Mouse.IsOver(cloudSegRect)) Widgets.DrawHighlight(cloudSegRect);
        if (Widgets.ButtonInvisible(cloudSegRect))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            settings.UseCloudProviders = true;
        }
        TextAnchor oldAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(cloudSegRect, "RimTalk.Settings.CloudProviders".Translate());

        bool isLocal = !settings.UseCloudProviders;
        Widgets.DrawBoxSolid(localSegRect, isLocal ? new Color(0.22f, 0.33f, 0.45f, 0.90f) : new Color(0.10f, 0.11f, 0.13f, 0.85f));
        GUI.color = isLocal ? new Color(0.40f, 0.65f, 0.85f, 0.95f) : SectionBorderColor;
        Widgets.DrawBox(localSegRect, 1);
        GUI.color = Color.white;
        if (Mouse.IsOver(localSegRect)) Widgets.DrawHighlight(localSegRect);
        if (Widgets.ButtonInvisible(localSegRect))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            settings.UseCloudProviders = false;
            settings.LocalConfig.Provider = AIProvider.Local;
        }
        Widgets.Label(localSegRect, "RimTalk.Settings.LocalProvider".Translate());
        Text.Anchor = oldAnchor;

        TooltipHandler.TipRegion(cloudSegRect, "RimTalk.Settings.CloudProvidersDesc".Translate());
        TooltipHandler.TipRegion(localSegRect, "RimTalk.Settings.LocalProviderDesc".Translate());

        // Add button on the right if cloud mode
        if (settings.UseCloudProviders)
        {
            float addBtnW = 110f;
            Rect addBtnRect = new Rect(modeRow.xMax - addBtnW, modeRow.y, addBtnW, 28f);
            Color prevColor = GUI.color;
            GUI.color = new Color(0.3f, 0.9f, 0.3f);
            if (UIUtil.ButtonText(addBtnRect, "+ " + "Add".Translate()))
            {
                settings.CloudConfigs.Add(new ApiConfig());
            }
            GUI.color = prevColor;
        }

        listingStandard.Gap(8f);

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

        if (UIUtil.ButtonText(deleteRect, "×", active: canDelete))
        {
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
        
        if (UIUtil.ButtonText(upButtonRect, "▲") && index > 0)
        {
            (configs[index], configs[index - 1]) = (configs[index - 1], configs[index]);
        }

        Rect downButtonRect = new Rect(x + btnSize + 2f, y, btnSize, height);

        if (UIUtil.ButtonText(downButtonRect, "▼") && index < configs.Count - 1)
        {
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
            
            if (UIUtil.ButtonText(backButtonRect, "×"))
            {
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
            Widgets.Label(rect, $" {placeholder}");
            
            GUI.color = originalColor;
            Text.Anchor = originalAnchor;
        }

        return result;
    }

    private static readonly AIProvider[] DropdownProviders =
    [
        AIProvider.Claude,
        AIProvider.OpenAI,
        AIProvider.DeepSeek,
        AIProvider.Grok,
        AIProvider.GLM,
        AIProvider.GLMCoding,
        AIProvider.OpenRouter,
        AIProvider.AlibabaIntl,
        AIProvider.AlibabaCN,
        AIProvider.Moonshot,
        AIProvider.Google,
        AIProvider.Player2,
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

    private struct DetectedServerInfo
    {
        public string Url;
        public string Name;
        public List<string> Models;
    }

    private static string GetLocalServerDisplayName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            string hostPort = $"{uri.Host}:{uri.Port}";
            string appName = uri.Port switch
            {
                11434 => "Ollama",
                1234 => "LM Studio",
                8080 => "llama.cpp",
                8000 => "vLLM",
                5000 => "TextGen",
                5001 => "KoboldCpp",
                1337 => "Jan",
                _ => null
            };

            return appName != null ? $"{appName} ({hostPort})" : hostPort;
        }

        return url;
    }

    private static bool isScanningLocal;
    private static List<DetectedServerInfo> pendingLocalServers;
    private static ApiConfig pendingLocalConfig;

    private void DrawLocalProviderSection(Listing_Standard listingStandard, RimTalkSettings settings)
    {
        if (settings.LocalConfig == null)
        {
            settings.LocalConfig = new ApiConfig { Provider = AIProvider.Local };
        }

        listingStandard.Gap(20f);
        DrawLocalConfigRow(listingStandard, settings.LocalConfig);
    }

    private static void ApplyDetectedServer(DetectedServerInfo server, ApiConfig config)
    {
        config.BaseUrl = server.Url;
        if (server.Models.Count == 1)
        {
            config.CustomModelName = server.Models[0];
        }
        else if (server.Models.Count > 1)
        {
            var modelOptions = server.Models.Select(m => new FloatMenuOption(m, () =>
            {
                config.CustomModelName = m;
            })).ToList();
            Find.WindowStack.Add(new FloatMenu(modelOptions) { vanishIfMouseDistant = false });
        }
    }

    private void DrawLocalConfigRow(Listing_Standard listingStandard, ApiConfig config)
    {
        if (pendingLocalServers != null && pendingLocalConfig == config)
        {
            var servers = pendingLocalServers;
            pendingLocalServers = null;
            pendingLocalConfig = null;

            if (servers.Count == 1)
            {
                ApplyDetectedServer(servers[0], config);
            }
            else
            {
                var serverOptions = servers.Select(server => new FloatMenuOption(server.Name, () =>
                {
                    ApplyDetectedServer(server, config);
                })).ToList();

                Find.WindowStack.Add(new FloatMenu(serverOptions) { vanishIfMouseDistant = false });
            }
        }

        const float totalWidth = 708f;
        const float height = 26f;

        Rect rowRect = listingStandard.GetRect(height);
        float x = rowRect.x + Mathf.Max(0f, (listingStandard.ColumnWidth - totalWidth) / 2f);
        float y = rowRect.y;

        TextAnchor prevAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;

        // Base URL label & info icon
        Widgets.Label(new Rect(x, y, 38f, height), "RimTalk.Settings.BaseUrlLabel".Translate());
        Rect infoRect = new Rect(x + 40f, y + 4f, 18f, 18f);
        GUI.DrawTexture(infoRect, TexButton.Info);
        TooltipHandler.TipRegion(infoRect, "RimTalk_Settings_Api_BaseUrlInfo".Translate());
        x += 66f;

        Text.Anchor = prevAnchor;

        // URL TextField
        Rect urlRect = new Rect(x, y, 230f, height);
        config.BaseUrl = Widgets.TextField(urlRect, config.BaseUrl);
        x += 262f;

        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(x, y, 60f, height), "RimTalk.Settings.ModelLabel".Translate());
        Text.Anchor = prevAnchor;
        x += 64f;

        // Model TextField
        Rect modelRect = new Rect(x, y, 200f, height);
        config.CustomModelName = Widgets.TextField(modelRect, config.CustomModelName);
        x += 218f;

        // Customize Button (gear icon)
        Rect customRect = new Rect(x, y + 2f, 22f, 22f);
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
        x += 40f;

        // Auto Detect Button
        Rect scanBtnRect = new Rect(x, y, 118f, height);
        string scanLabel = isScanningLocal
            ? "RimTalk.Settings.LocalScanning".Translate()
            : "RimTalk.Settings.LocalAutoDetect".Translate();

        if (UIUtil.ButtonText(scanBtnRect, scanLabel, true, true, !isScanningLocal))
        {
            StartScanLocalEndpoints(config);
        }
    }

    private static void StartScanLocalEndpoints(ApiConfig config)
    {
        if (isScanningLocal) return;
        isScanningLocal = true;

        Task.Run(async () =>
        {
            try
            {
                string[] candidates =
                [
                    "http://localhost:11434",
                    "http://localhost:1234",
                    "http://localhost:8080",
                    "http://localhost:8000",
                    "http://localhost:5000",
                    "http://localhost:5001",
                    "http://localhost:1337"
                ];

                var tasks = candidates.Select(async url =>
                {
                    try
                    {
                        string modelsUrl = url.TrimEnd('/') + "/v1/models";
                        var models = await OpenAIClient.FetchModelsAsync(null, modelsUrl);
                        if (models != null && models.Count > 0)
                        {
                            return (url, models, alive: true);
                        }
                    }
                    catch
                    {
                        // Ignore connection failures / timeouts
                    }
                    return (url, models: new List<string>(), alive: false);
                }).ToList();

                var results = await Task.WhenAll(tasks);
                var activeServers = results
                    .Where(r => r.alive)
                    .Select(r => new DetectedServerInfo
                    {
                        Url = r.url,
                        Name = GetLocalServerDisplayName(r.url),
                        Models = r.models
                    })
                    .ToList();

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    isScanningLocal = false;
                    if (activeServers.Count == 0)
                    {
                        Messages.Message("RimTalk.Settings.LocalNotFound".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        Messages.Message("RimTalk.Settings.LocalDetected".Translate(), MessageTypeDefOf.PositiveEvent, false);
                        pendingLocalServers = activeServers;
                        pendingLocalConfig = config;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error scanning local endpoints: {ex.Message}");
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    isScanningLocal = false;
                    Messages.Message("RimTalk.Settings.LocalNotFound".Translate(), MessageTypeDefOf.RejectInput, false);
                });
            }
        });
    }
}