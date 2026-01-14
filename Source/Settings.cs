using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimTalk.Prompt;
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk;

public partial class Settings : Mod
{
    private Vector2 _mainScrollPosition = Vector2.zero;
    private Vector2 _aiInstructionScrollPos = Vector2.zero;
    private Vector2 _promptContentScrollPos = Vector2.zero;
    private string _textAreaBuffer = "";
    private bool _textAreaInitialized;
    private int _lastTextAreaCursorPos = -1;
    private int _lastPromptEditorCursorPos = -1;
    private List<string> _discoveredArchivableTypes = [];
    private bool _archivableTypesScanned;
    private int _apiSettingsHash = 0;

    // Tab system
    private enum SettingsTab
    {
        Basic,
        PromptPreset,
        Context,
        EventFilter
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
        AIDriven
    }

    private SettingsTab _currentTab = SettingsTab.Basic;

    private static RimTalkSettings _settings;

    public static RimTalkSettings Get()
    {
        return _settings ??= LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
    }

    public Settings(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("cj.rimtalk");
        var settings = GetSettings<RimTalkSettings>();
        harmony.PatchAll();
        _apiSettingsHash = GetApiSettingsHash(settings);
    }

    public override string SettingsCategory() =>
        Content?.Name ?? GetType().Assembly.GetName().Name;

    private void ScanForArchivableTypes()
    {
        if (_archivableTypesScanned) return;

        var archivableTypes = new HashSet<string>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => typeof(IArchivable).IsAssignableFrom(t) &&
                                !t.IsInterface &&
                                !t.IsAbstract)
                    .Select(t => t.FullName)
                    .ToList();

                foreach (var type in types)
                    archivableTypes.Add(type);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error scanning assembly {assembly.FullName}: {ex.Message}");
            }
        }

        if (Current.Game != null && Find.Archive != null)
        {
            foreach (var archivable in Find.Archive.ArchivablesListForReading)
            {
                archivableTypes.Add(archivable.GetType().FullName);
            }
        }

        // Add Defs from XML
        var letterDefNames = DefDatabase<LetterDef>.AllDefs.Select(def => def.defName);
        var messageTypeDefNames = DefDatabase<MessageTypeDef>.AllDefs.Select(def => def.defName);
        foreach (var def in letterDefNames) archivableTypes.Add(def);
        foreach (var def in messageTypeDefNames) archivableTypes.Add(def);

        _discoveredArchivableTypes = archivableTypes.OrderBy(x => x).ToList();
        _archivableTypesScanned = true;

        RimTalkSettings settings = Get();
        foreach (var typeName in _discoveredArchivableTypes)
        {
            if (!settings.EnabledArchivableTypes.ContainsKey(typeName))
            {
                bool defaultEnabled = !typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase);
                settings.EnabledArchivableTypes[typeName] = defaultEnabled;
            }
        }

        Log.Message($"[RimTalk] Discovered {_discoveredArchivableTypes.Count} archivable types");
    }

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
            
        if (settings.CloudConfigs != null)
        {
            foreach (var config in settings.CloudConfigs)
            {
                sb.AppendLine(config.Provider.ToString());
                sb.AppendLine(config.ApiKey);
                sb.AppendLine(config.SelectedModel);
                sb.AppendLine(config.CustomModelName);
                sb.AppendLine(config.IsEnabled.ToString());
                sb.AppendLine(config.BaseUrl);
            }
        }
        if (settings.LocalConfig != null)
        {
            sb.AppendLine(settings.LocalConfig.Provider.ToString());
            sb.AppendLine(settings.LocalConfig.BaseUrl);
            sb.AppendLine(settings.LocalConfig.CustomModelName);
        }

        sb.AppendLine(settings.CustomInstruction);
        sb.AppendLine(settings.AllowSimultaneousConversations.ToString());
        sb.AppendLine(settings.AllowSlavesToTalk.ToString());
        sb.AppendLine(settings.AllowPrisonersToTalk.ToString());
        sb.AppendLine(settings.AllowOtherFactionsToTalk.ToString());
        sb.AppendLine(settings.AllowEnemiesToTalk.ToString());
        sb.AppendLine(settings.AllowBabiesToTalk.ToString());
        sb.AppendLine(settings.AllowNonHumanToTalk.ToString());
        sb.AppendLine(settings.ApplyMoodAndSocialEffects.ToString());
        sb.AppendLine(settings.PlayerDialogueMode.ToString());
        sb.AppendLine(settings.PlayerName);
        
        return sb.ToString().GetHashCode();
    }

    private void DrawTabButtons(Rect rect)
    {
        float tabWidth = rect.width / 4f;

        Rect basicTabRect = new Rect(rect.x, rect.y, tabWidth, 30f);
        Rect promptTabRect = new Rect(rect.x + tabWidth, rect.y, tabWidth, 30f);
        Rect contextTabRect = new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, 30f);
        Rect filterTabRect = new Rect(rect.x + tabWidth * 3, rect.y, tabWidth, 30f);

        GUI.color = _currentTab == SettingsTab.Basic ? Color.white : Color.gray;
        if (Widgets.ButtonText(basicTabRect, "RimTalk.Settings.BasicSettings".Translate()))
        {
            _currentTab = SettingsTab.Basic;
        }

        GUI.color = _currentTab == SettingsTab.PromptPreset ? Color.white : Color.gray;
        if (Widgets.ButtonText(promptTabRect, "RimTalk.Settings.PromptSetting".Translate()))
        {
            _currentTab = SettingsTab.PromptPreset;
        }

        GUI.color = _currentTab == SettingsTab.Context ? Color.white : Color.gray;
        if (Widgets.ButtonText(contextTabRect, "RimTalk.Settings.ContextFilter".Translate()))
        {
            _currentTab = SettingsTab.Context;
        }

        GUI.color = _currentTab == SettingsTab.EventFilter ? Color.white : Color.gray;
        if (Widgets.ButtonText(filterTabRect, "RimTalk.Settings.EventFilter".Translate()))
        {
            _currentTab = SettingsTab.EventFilter;
            if (!_archivableTypesScanned)
            {
                ScanForArchivableTypes();
            }
        }

        GUI.color = Color.white;
    }
        
    public override void DoSettingsWindowContents(Rect inRect)
    {
        RimTalkSettings rtSettings = Get();
        
        // Settings window hacks
        var settingsWindow = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
        if (settingsWindow != null)
        {
            settingsWindow.doCloseX = true;
            settingsWindow.draggable = true;
            settingsWindow.closeOnAccept = false;
            settingsWindow.absorbInputAroundWindow = false;
            settingsWindow.preventCameraMotion = false;
            settingsWindow.closeOnClickedOutside = false;

            // Dynamically resize if in Advanced Prompt mode, otherwise reset to standard size
            float targetWidth;
            float targetHeight;

            if (_currentTab == SettingsTab.PromptPreset && rtSettings.UseAdvancedPromptMode)
            {
                targetWidth = Mathf.Min(Verse.UI.screenWidth * 0.9f, 1200f);
                targetHeight = Mathf.Min(Verse.UI.screenHeight * 0.9f, 800f);
            }
            else
            {
                targetWidth = 900f;
                targetHeight = 700f;
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
        
        // 1. Draw Tabs
        Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
        DrawTabButtons(tabRect);

        // 2. Define Content Area
        Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);

        // 3. Special Case: Prompt Preset Tab (Advanced Mode only)
        // Why this is different: Advanced Mode contains a complex, full-height editor 
        // that handles its own internal scrolling. Wrapping it in a main ScrollView causes 
        // nested scroll issues.
        if (_currentTab == SettingsTab.PromptPreset && rtSettings.UseAdvancedPromptMode)
        {
            Listing_Standard promptListing = new Listing_Standard();
            promptListing.Begin(contentRect);
            DrawPromptPresetSettings(promptListing, contentRect);
            promptListing.End();
            return;
        }

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
            case SettingsTab.PromptPreset:
                DrawPromptPresetSettings(listing, contentRect);
                break;
            case SettingsTab.Context:
                DrawContextFilterSettings(listing);
                break;
            case SettingsTab.EventFilter:
                DrawEventFilterSettings(listing);
                break;
        }

        float contentHeight = listing.CurHeight;
        listing.End();
        GUI.EndGroup();

        // --- Real Draw ---
        Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
        _mainScrollPosition = GUI.BeginScrollView(contentRect, _mainScrollPosition, viewRect);

        listing.Begin(viewRect);

        switch (_currentTab)
        {
            case SettingsTab.Basic:
                DrawBasicSettings(listing);
                break;
            case SettingsTab.PromptPreset:
                DrawPromptPresetSettings(listing, contentRect);
                break;
            case SettingsTab.Context:
                DrawContextFilterSettings(listing);
                break;
            case SettingsTab.EventFilter:
                DrawEventFilterSettings(listing);
                break;
        }

        listing.End();
        GUI.EndScrollView();
    }
    
    private static void ClearCache()
    {
        _settings = null;
    }
}
