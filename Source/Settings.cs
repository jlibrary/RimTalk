using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalk;

public partial class Settings : Mod
{
    internal const string Version = "1.3.2";

    private Vector2 _mainScrollPosition = Vector2.zero;
    private Vector2 _aiInstructionScrollPos = Vector2.zero;
    private Vector2 _promptContentScrollPos = Vector2.zero;
    private string _textAreaBuffer = "";
    private bool _textAreaInitialized;
    private string _aiInstructionPresetId = "";
    private int _lastTextAreaCursorPos = -1;
    private int _lastPromptEditorCursorPos = -1;
    private int _apiSettingsHash = 0;
    private string _talkIntervalBuffer;
    private string _replyIntervalBuffer;
    private string _maxPawnContextBuffer;
    private string _conversationHistoryBuffer;
    private string _maxEventsBuffer;

    // Tab system
    private enum SettingsTab
    {
        Basic,
        PromptPreset,
        Context,
        CustomDialogue
    }
    public enum ButtonDisplayMode
    {
        Tab,
        Toggle,
        None
    }
    public enum PlayerDialogueMode
    {
        Disabled,
        Manual,
        AIDrivenPawnOnly,
        AIDriven
    }

    private SettingsTab _currentTab = SettingsTab.Basic;
    private List<TabRecord> _tabs;

    private List<TabRecord> Tabs => _tabs ??= new List<TabRecord>
    {
        new TabRecord("RimTalk.Settings.BasicSettings".Translate(), () => _currentTab = SettingsTab.Basic, () => _currentTab == SettingsTab.Basic),
        new TabRecord("RimTalk.Settings.PromptSetting".Translate(), () => _currentTab = SettingsTab.PromptPreset, () => _currentTab == SettingsTab.PromptPreset),
        new TabRecord("RimTalk.Settings.ContextFilter".Translate(), () => _currentTab = SettingsTab.Context, () => _currentTab == SettingsTab.Context),
        new TabRecord("RimTalk.Settings.CustomDialogue".Translate(), () => _currentTab = SettingsTab.CustomDialogue, () => _currentTab == SettingsTab.CustomDialogue)
    };

    private static readonly Color SectionBgColor = new(33f / 255f, 33f / 255f, 33f / 255f);
    private static readonly Color SectionBorderColor = new(70f / 255f, 70f / 255f, 70f / 255f);
    private static readonly Color TabInactiveBgColor = new(22f / 255f, 22f / 255f, 22f / 255f);

    private static RimTalkSettings _settings;

    public static RimTalkSettings Get()
    {
        return _settings ??= LoadedModManager.GetMod<Settings>()?.GetSettings<RimTalkSettings>();
    }

    public Settings(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("cj.rimtalk");
        _settings = GetSettings<RimTalkSettings>();
        harmony.PatchAll();
        _apiSettingsHash = GetApiSettingsHash(_settings);
    }

    public override string SettingsCategory() =>
        (Content?.Name ?? GetType().Assembly.GetName().Name) + $" v{Version}";

    public override void WriteSettings()
    {
        base.WriteSettings();
        ClearCache();
        RimTalkSettings settings = Get();
        int newHash = GetApiSettingsHash(settings);

        if (newHash != _apiSettingsHash)
        {
            settings.CurrentCloudConfigIndex = 0;
            _apiSettingsHash = newHash;
            RimTalk.Reset(true);
        }
    }

    private int GetApiSettingsHash(RimTalkSettings settings)
    {
        var sb = new StringBuilder();

        var activeConfig = settings.GetActiveConfig();
        if (activeConfig != null)
        {
            sb.AppendLine(activeConfig.Provider.ToString());
            sb.AppendLine(activeConfig.GetEffectiveModelName());
            sb.AppendLine(activeConfig.BaseUrl);
        }
        else
        {
            sb.AppendLine("None");
        }

        sb.AppendLine(settings.AllowSimultaneousConversations.ToString());
        sb.AppendLine(settings.AllowSlavesToTalk.ToString());
        sb.AppendLine(settings.AllowPrisonersToTalk.ToString());
        sb.AppendLine(settings.AllowOtherFactionsToTalk.ToString());
        sb.AppendLine(settings.AllowEnemiesToTalk.ToString());
        sb.AppendLine(settings.AllowBabiesToTalk.ToString());
        sb.AppendLine(settings.AllowNonHumanToTalk.ToString());
        sb.AppendLine(settings.AllowAnnouncement.ToString());
        sb.AppendLine(settings.ApplyMoodAndSocialEffects.ToString());
        sb.AppendLine(settings.PlayerDialogueMode.ToString());
        sb.AppendLine(settings.PlayerName);
        sb.AppendLine(settings.PlayerPersona);
        
        return sb.ToString().GetHashCode();
    }

    private void DrawTabButtons(Rect tabBaseRect, Rect inRect, RimTalkSettings settings)
    {
        const float maxTabWidth = 175f;
        float tabWidth = Mathf.Floor(Mathf.Min(maxTabWidth, (inRect.width - 135f) / Tabs.Count));
        float tabHeight = 32f;
        float curX = tabBaseRect.x;

        Text.Anchor = TextAnchor.MiddleCenter;
        Text.Font = GameFont.Small;

        for (int i = 0; i < Tabs.Count; i++)
        {
            TabRecord tab = Tabs[i];
            bool isSelected = tab.Selected;
            float h = isSelected ? tabHeight : tabHeight - 3f;
            Rect tabRect = new Rect(curX, tabBaseRect.y - h, tabWidth, isSelected ? h + 3f : h);

            Widgets.DrawBoxSolid(tabRect, isSelected ? SectionBgColor : TabInactiveBgColor);
            GUI.color = SectionBorderColor;
            Widgets.DrawLineHorizontal(tabRect.x, tabRect.y, tabWidth);
            Widgets.DrawLineVertical(tabRect.x, tabRect.y, (i == 0 && isSelected) ? h + 3f : h);
            Widgets.DrawLineVertical(tabRect.xMax - 1f, tabRect.y, h);

            if (!isSelected)
            {
                Widgets.DrawLineHorizontal(tabRect.x, tabBaseRect.y, tabWidth);
                Widgets.DrawHighlightIfMouseover(tabRect);
                if (Widgets.ButtonInvisible(tabRect))
                {
                    tab.clickedAction?.Invoke();
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                }
            }

            GUI.color = isSelected ? Color.white : new Color(0.70f, 0.70f, 0.70f);
            Widgets.Label(new Rect(tabRect.x, tabBaseRect.y - h, tabWidth, h), tab.label);
            curX += tabWidth;
        }

        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = SectionBorderColor;
        if (curX < tabBaseRect.xMax) Widgets.DrawLineHorizontal(curX, tabBaseRect.y, tabBaseRect.xMax - curX);

        // Quick Setup button
        GUI.color = Color.white;
        Rect toggleRect = new Rect(inRect.xMax - 123f, tabBaseRect.y - 31f, 125f, 30f);
        if (UIUtil.ButtonText(toggleRect, "RimTalk.Settings.QuickSetupButton".Translate())) settings.ShowQuickSettings = true;
        TooltipHandler.TipRegion(toggleRect, "RimTalk.Settings.QuickSetupTooltip".Translate());
    }

    private void DrawQuickSetup(Rect inRect, RimTalkSettings settings)
    {
        // 1. Top Bar
        float toggleWidth = 125f;
        const float headerHeight = 30f;
        Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
        Rect subtitleRect = new Rect(headerRect.x, headerRect.y + 4f, headerRect.width - toggleWidth - 15f, 24f);
        Rect toggleRect = new Rect(headerRect.xMax - toggleWidth, headerRect.y, toggleWidth, 30f);

        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(subtitleRect, "RimTalk.Settings.QuickSetupSubtitle".Translate());
        GUI.color = Color.white;

        if (UIUtil.ButtonText(toggleRect, "RimTalk.Settings.AllSettingsButton".Translate()))
        {
            settings.ShowQuickSettings = false;
        }
        TooltipHandler.TipRegion(toggleRect, "RimTalk.Settings.AllSettingsTooltip".Translate());

        // 2. Content Area
        var quickWindow = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
        float closeWidth = 120f;
        float quickMaxUsableY = quickWindow != null ? (quickWindow.windowRect.height - 36f) : (inRect.yMax + 40f);
        float quickBtnY = quickMaxUsableY - 35f - 6f;

        float boxTop = inRect.y + headerHeight + 8f;
        Rect contentBoxRect = new Rect(inRect.x, boxTop, inRect.width, (quickBtnY - 10f) - boxTop);
        Widgets.DrawBoxSolid(contentBoxRect, SectionBgColor);
        Color prevColor = GUI.color;
        GUI.color = SectionBorderColor;
        Widgets.DrawLineVertical(contentBoxRect.x, contentBoxRect.y, contentBoxRect.height);
        Widgets.DrawLineVertical(contentBoxRect.xMax - 1f, contentBoxRect.y, contentBoxRect.height);
        Widgets.DrawLineHorizontal(contentBoxRect.x, contentBoxRect.y, contentBoxRect.width);
        Widgets.DrawLineHorizontal(contentBoxRect.x, contentBoxRect.yMax - 1f, contentBoxRect.width);
        GUI.color = prevColor;

        Rect contentRect = contentBoxRect.ContractedBy(10f);
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(contentRect);

        // 2-card mode selector (Google Gemini vs Player2)
        DrawQuickApiModeSelector(listing, settings);
        listing.Gap(8f);

        // API Key or Player2 connection settings
        DrawSimpleApiSettings(listing);

        if (!settings.UseSimpleConfig)
        {
            listing.Gap(10f);
            GUI.color = new Color(1f, 0.85f, 0.4f);
            Text.Font = GameFont.Tiny;
            listing.Label("RimTalk.Settings.CustomConfigActiveNotice".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        listing.End();

        // 3. Bottom Close Button
        Rect closeBtnRect = new Rect(inRect.x + (inRect.width - closeWidth) / 2f, quickBtnY, closeWidth, 35f);
        if (UIUtil.ButtonText(closeBtnRect, "CloseButton".Translate()))
        {
            quickWindow?.Close();
        }

        DrawRollbackLink(inRect.x, quickBtnY, 35f);
    }

    private static void DrawRollbackLink(float x, float y, float height)
    {
        bool hasLastDll = VersionSwitcher.IsPreviousDllAvailable();
        var availVersions = VersionSwitcher.GetAvailableVersions();
        string rollbackText = availVersions.Count == 1
            ? $"{"RimTalk.VersionSwitcher.RollbackLabel".Translate()} ({availVersions[0]})"
            : "RimTalk.VersionSwitcher.RollbackLabel".Translate().ToString();

        GameFont origFont = Text.Font;
        Color origColor = GUI.color;
        TextAnchor origAnchor = Text.Anchor;
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.MiddleLeft;

        Vector2 textSize = Text.CalcSize(rollbackText);
        float linkY = y + (height - textSize.y) / 2f;
        Rect linkRect = new Rect(x, linkY, textSize.x + 4f, textSize.y);
        bool isHovered = Mouse.IsOver(linkRect);

        if (hasLastDll)
        {
            GUI.color = isHovered ? Color.white : new Color(0.72f, 0.72f, 0.72f, 0.9f);
            Widgets.Label(linkRect, rollbackText);

            if (Widgets.ButtonInvisible(linkRect))
            {
                VersionSwitcher.OpenRollbackMenu();
            }
        }
        else
        {
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            Widgets.Label(linkRect, rollbackText);
        }

        Text.Font = origFont;
        Text.Anchor = origAnchor;
        GUI.color = origColor;
    }
        
    public override void DoSettingsWindowContents(Rect inRect)
    {
        RimTalkSettings rtSettings = Get();
        
        // Settings window hacks
        var settingsWindow = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
        if (settingsWindow != null)
        {
            settingsWindow.doCloseX = true;
            settingsWindow.doCloseButton = false;
            settingsWindow.draggable = true;
            settingsWindow.closeOnAccept = false;
            settingsWindow.absorbInputAroundWindow = false;
            settingsWindow.preventCameraMotion = false;
            settingsWindow.closeOnClickedOutside = false;
            settingsWindow.forcePause = false;

            float targetWidth;
            float targetHeight;

            if (rtSettings.ShowQuickSettings == true)
            {
                targetWidth = 650f;
                targetHeight = 600f;
            }
            else if (_currentTab == SettingsTab.PromptPreset && rtSettings.UseAdvancedPromptMode)
            {
                targetWidth = Mathf.Min(Verse.UI.screenWidth * 0.9f, 1200f);
                targetHeight = Mathf.Min(Verse.UI.screenHeight * 0.9f, 800f);
            }
            else
            {
                targetWidth = Mathf.Min(Verse.UI.screenWidth * 0.85f, 960f);
                targetHeight = Mathf.Min(Verse.UI.screenHeight * 0.85f, 740f);
            }

            if (Mathf.Abs(settingsWindow.windowRect.width - targetWidth) > 1f || 
                Mathf.Abs(settingsWindow.windowRect.height - targetHeight) > 1f)
            {
                settingsWindow.windowRect.width = targetWidth;
                settingsWindow.windowRect.height = targetHeight;
                settingsWindow.windowRect.x = (Verse.UI.screenWidth - targetWidth) / 2f;
                settingsWindow.windowRect.y = (Verse.UI.screenHeight - targetHeight) / 2f;
            }
        }
        
        // 0. Quick Setup Mode (No tabs)
        if (rtSettings.ShowQuickSettings == true)
        {
            DrawQuickSetup(inRect, rtSettings);
            return;
        }

        // 1. Draw Tabs & Menu Section Frame
        float btnHeight = 35f;
        float maxUsableY = settingsWindow != null ? (settingsWindow.windowRect.height - 36f) : (inRect.yMax + 40f);
        float btnY = maxUsableY - btnHeight - 6f;

        float tabLineY = inRect.y + 40f;
        Rect tabBaseRect = new Rect(inRect.x, tabLineY, inRect.width, (btnY - 10f) - tabLineY);
        Widgets.DrawBoxSolid(tabBaseRect, SectionBgColor);
        DrawTabButtons(tabBaseRect, inRect, rtSettings);

        Color prevColor = GUI.color;
        GUI.color = SectionBorderColor;
        Widgets.DrawLineVertical(tabBaseRect.x, tabBaseRect.y, tabBaseRect.height);
        Widgets.DrawLineVertical(tabBaseRect.xMax - 1f, tabBaseRect.y, tabBaseRect.height);
        Widgets.DrawLineHorizontal(tabBaseRect.x, tabBaseRect.yMax - 1f, tabBaseRect.width);
        GUI.color = prevColor;

        // 2. Define Content Area (inset by 10px inside the menu section)
        Rect contentRect = tabBaseRect.ContractedBy(10f);

        // 3. Special Case: Prompt Preset Tab (Handles its own scrolling in both Simple and Advanced modes)
        if (_currentTab == SettingsTab.PromptPreset)
        {
            Listing_Standard promptListing = new Listing_Standard();
            promptListing.Begin(contentRect);
            DrawPromptPresetSettings(promptListing, contentRect);
            promptListing.End();
        }
        else
        {
            // 4. Standard Logic for other tabs (Scrollable Lists)
            // --- Off-screen height calculation ---
            GUI.BeginGroup(new Rect(-9999, -9999, 1, 1)); 
            Listing_Standard listing = new Listing_Standard();
            Rect calculationRect = new Rect(0, 0, contentRect.width - 16f, 9999f);
            listing.Begin(calculationRect);

            switch (_currentTab)
            {
                case SettingsTab.Basic:
                    DrawBasicSettings(listing);
                    break;
                case SettingsTab.Context:
                    DrawContextFilterSettings(listing);
                    break;
                case SettingsTab.CustomDialogue:
                    DrawCustomDialogueSettings(listing);
                    break;
            }

            float contentHeight = listing.CurHeight;
            listing.End();
            GUI.EndGroup();

            // Reset scroll position if content fits without scrolling
            if (contentHeight <= contentRect.height)
            {
                _mainScrollPosition = Vector2.zero;
            }

            // --- Real Draw ---
            _mainScrollPosition.y = Mathf.Round(_mainScrollPosition.y);
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, Mathf.Max(contentHeight, contentRect.height));
            Widgets.BeginScrollView(contentRect, ref _mainScrollPosition, viewRect);

            listing.Begin(viewRect);

            switch (_currentTab)
            {
                case SettingsTab.Basic:
                    DrawBasicSettings(listing);
                    break;
                case SettingsTab.Context:
                    DrawContextFilterSettings(listing);
                    break;
                case SettingsTab.CustomDialogue:
                    DrawCustomDialogueSettings(listing);
                    break;
            }

            listing.End();
            Widgets.EndScrollView();
        }

        // 5. Bottom Bar: Reset to Default and Close buttons at the VERY BOTTOM of the window
        float closeBtnWidth = 120f;
        float resetBtnWidth = 160f;
        float btnGap = 12f;
        float totalWidth = resetBtnWidth + closeBtnWidth + btnGap;
        float startX = inRect.x + (inRect.width - totalWidth) / 2f;

        // Rollback text link on the bottom-left across all tabs
        DrawRollbackLink(inRect.x, btnY, btnHeight);

        Rect resetBtnRect = new Rect(startX, btnY, resetBtnWidth, btnHeight);
        Rect closeBtnRect = new Rect(startX + resetBtnWidth + btnGap, btnY, closeBtnWidth, btnHeight);

        if (UIUtil.ButtonText(resetBtnRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            switch (_currentTab)
            {
                case SettingsTab.Basic:
                    ResetBasicSettings(rtSettings);
                    break;
                case SettingsTab.PromptPreset:
                    if (rtSettings.UseAdvancedPromptMode)
                    {
                        var promptMgr = PromptManager.Instance;
                        if (promptMgr.HasAddonInjectedDefaults())
                        {
                            var dialog = new Dialog_MessageBox(
                                "RimTalk.Settings.PromptPreset.ResetConfirmWithMods".Translate(),
                                "RimTalk.Settings.PromptPreset.ResetWithMods".Translate(),
                                () =>
                                {
                                    promptMgr.ResetToModDefaults();
                                    _selectedPresetId = null;
                                    _selectedEntryId = null;
                                },
                                "GoBack".Translate(),
                                null,
                                "RimTalk.Settings.ResetToDefault".Translate(),
                                buttonADestructive: false
                            );
                            dialog.buttonCText = "RimTalk.Settings.PromptPreset.ResetVanillaDefault".Translate();
                            dialog.buttonCAction = () =>
                            {
                                promptMgr.ResetToVanillaDefaults();
                                _selectedPresetId = null;
                                _selectedEntryId = null;
                            };
                            Find.WindowStack.Add(dialog);
                        }
                        else
                        {
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RimTalk.Settings.ResetConfirm".Translate(), () =>
                            {
                                promptMgr.ResetToVanillaDefaults();
                                _selectedPresetId = null;
                                _selectedEntryId = null;
                            }));
                        }
                    }
                    else
                    {
                        rtSettings.SimpleModeInstruction = Constant.DefaultInstruction;
                        _textAreaBuffer = Constant.DefaultInstruction;
                        _textAreaInitialized = true;
                    }
                    break;
                case SettingsTab.Context:
                    ResetContextSettings(rtSettings);
                    break;
                case SettingsTab.CustomDialogue:
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RimTalk.PlayerSettings.ResetPresetsConfirm".Translate(), () =>
                    {
                        rtSettings.DialoguePresets = CustomDialoguePreset.CreateDefaultPresets();
                    }));
                    break;
            }
        }

        if (UIUtil.ButtonText(closeBtnRect, "CloseButton".Translate()))
        {
            settingsWindow?.Close();
        }
    }
    
    private static void ClearCache()
    {
        _settings = null;
    }
}
