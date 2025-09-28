using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk;

public partial class Settings : Mod
{
    private Vector2 _mainScrollPosition = Vector2.zero;
    private string _textAreaBuffer = "";
    private bool _textAreaInitialized;
    private List<string> _discoveredArchivableTypes = [];
    private bool _archivableTypesScanned;
    private int _apiSettingsHash = 0;

    // Tab system
    private enum SettingsTab
    {
        Basic,
        AIInstruction,
        EventFilter
    }

    private SettingsTab _currentTab = SettingsTab.Basic;

    private static RimTalkSettings _settings;

    public static RimTalkSettings Get()
    {
        if (_settings == null)
        {
            _settings = LoadedModManager.GetMod<Settings>().GetSettings<RimTalkSettings>();
        }
        return _settings;
    }

    public static void ClearCache()
    {
        _settings = null;
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

        // Scan all assemblies for IArchivable implementations (includes mods)
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

        // Also add types from current archive if game is loaded (to catch any missed runtime types)
        if (Current.Game != null && Find.Archive != null)
        {
            foreach (var archivable in Find.Archive.ArchivablesListForReading)
            {
                archivableTypes.Add(archivable.GetType().FullName);
            }
        }

        _discoveredArchivableTypes = archivableTypes.OrderBy(x => x).ToList();
        _archivableTypesScanned = true;

        // Initialize settings for new types
        RimTalkSettings settings = Get();
        foreach (var typeName in _discoveredArchivableTypes)
        {
            if (!settings.EnabledArchivableTypes.ContainsKey(typeName))
            {
                // Enable by default for most types, but disable Verse.Message specifically
                bool defaultEnabled = !typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase);
                settings.EnabledArchivableTypes[typeName] = defaultEnabled;
            }
        }

        Log.Message($"[RimTalk] Discovered {_discoveredArchivableTypes.Count} archivable types");
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        ClearCache(); // Invalidate the cache
        RimTalkSettings settings = Get();
        int newHash = GetApiSettingsHash(settings);

        // If hash changes, reset the cloud config index and trigger a full reset of RimTalk.
        if (newHash != _apiSettingsHash)
        {
            settings.CurrentCloudConfigIndex = 0;
            _apiSettingsHash = newHash;
            RimTalk.Reset(true);
        }
    }

    private int GetApiSettingsHash(RimTalkSettings settings)
    {
        // Create a string representation of the API settings and get its hash code
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

        return sb.ToString().GetHashCode();
    }

    private void DrawTabButtons(Rect rect)
    {
        float tabWidth = rect.width / 3f;

        Rect basicTabRect = new Rect(rect.x, rect.y, tabWidth, 30f);
        Rect instructionTabRect = new Rect(rect.x + tabWidth, rect.y, tabWidth, 30f);
        Rect filterTabRect = new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, 30f);

        // Basic Settings Tab
        GUI.color = _currentTab == SettingsTab.Basic ? Color.white : Color.gray;
        if (Widgets.ButtonText(basicTabRect, "RimTalk.Settings.BasicSettings".Translate()))
        {
            _currentTab = SettingsTab.Basic;
        }

        // AI Instruction Tab
        GUI.color = _currentTab == SettingsTab.AIInstruction ? Color.white : Color.gray;
        if (Widgets.ButtonText(instructionTabRect, "RimTalk.Settings.AIInstruction".Translate()))
        {
            _currentTab = SettingsTab.AIInstruction;
        }

        // Event Filter Tab
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
        // Draw tab buttons at the top
        Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
        DrawTabButtons(tabRect);

        // Draw content area below tabs
        Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);

        // --- Dynamic height calculation (off-screen) ---
        GUI.BeginGroup(new Rect(-9999, -9999, 1, 1)); // Draw off-screen
        Listing_Standard listing = new Listing_Standard();
        Rect calculationRect = new Rect(0, 0, contentRect.width - 16f, 9999f);
        listing.Begin(calculationRect);

        switch (_currentTab)
        {
            case SettingsTab.Basic:
                DrawBasicSettings(listing);
                break;
            case SettingsTab.AIInstruction:
                DrawAIInstructionSettings(listing);
                break;
            case SettingsTab.EventFilter:
                DrawEventFilterSettings(listing);
                break;
        }

        float contentHeight = listing.CurHeight;
        listing.End();
        GUI.EndGroup();
        // --- End of height calculation ---

        // Now draw for real with the correct scroll view height
        Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
        _mainScrollPosition = GUI.BeginScrollView(contentRect, _mainScrollPosition, viewRect);

        listing.Begin(viewRect);

        switch (_currentTab)
        {
            case SettingsTab.Basic:
                DrawBasicSettings(listing);
                break;
            case SettingsTab.AIInstruction:
                DrawAIInstructionSettings(listing);
                break;
            case SettingsTab.EventFilter:
                DrawEventFilterSettings(listing);
                break;
        }

        listing.End();
        GUI.EndScrollView();
    }
}