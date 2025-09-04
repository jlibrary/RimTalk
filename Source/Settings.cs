using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk
{
    public partial class Settings : Mod
    {
        private Vector2 mainScrollPosition = Vector2.zero;
        private string textAreaBuffer = "";
        private bool textAreaInitialized = false;
        private string lastSavedInstruction = "";
        private List<string> discoveredArchivableTypes = new List<string>();
        private bool archivableTypesScanned = false;
        
        // Tab system
        private enum SettingsTab
        {
            Basic,
            AIInstruction,
            EventFilter
        }

        private SettingsTab currentTab = SettingsTab.Basic;

        // Available model options
        private readonly string[] modelOptions = new string[]
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

        public static CurrentWorkDisplayModSettings Get() =>
            LoadedModManager.GetMod<Settings>().GetSettings<CurrentWorkDisplayModSettings>();

        public Settings(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("cj.rimtalk");
            GetSettings<CurrentWorkDisplayModSettings>();
            harmony.PatchAll();
        }

        public override string SettingsCategory() =>
            Content?.Name ?? GetType().Assembly.GetName().Name;

        private void ScanForArchivableTypes()
        {
            if (archivableTypesScanned) return;

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
                catch (ReflectionTypeLoadException ex)
                {
                    // Some assemblies might fail to load, that's ok
                    Logger.Warning($"[RimTalk] Could not load types from assembly: {assembly.FullName}");
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

            discoveredArchivableTypes = archivableTypes.OrderBy(x => x).ToList();
            archivableTypesScanned = true;

            // Initialize settings for new types
            CurrentWorkDisplayModSettings settings = Get();
            foreach (var typeName in discoveredArchivableTypes)
            {
                if (!settings.enabledArchivableTypes.ContainsKey(typeName))
                {
                    // Enable by default for most types, but disable Verse.Message specifically
                    bool defaultEnabled = !typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase);
                    settings.enabledArchivableTypes[typeName] = defaultEnabled;
                }
            }

            Log.Message($"[RimTalk] Discovered {discoveredArchivableTypes.Count} archivable types");
        }

        public override void WriteSettings()
        {
            base.WriteSettings();

            // Check if instruction changed when settings are saved (window closed)
            CurrentWorkDisplayModSettings settings = Get();
            if (settings.customInstruction != lastSavedInstruction)
            {
                lastSavedInstruction = settings.customInstruction;
                // Find the RimTalk GameComponent and call Reset
                var rimTalkComponent = Current.Game?.GetComponent<RimTalk>();
                if (rimTalkComponent != null)
                {
                    rimTalkComponent.Reset();
                }
            }
        }

        private void DrawTabButtons(Rect rect)
        {
            float tabWidth = rect.width / 3f;

            Rect basicTabRect = new Rect(rect.x, rect.y, tabWidth, 30f);
            Rect instructionTabRect = new Rect(rect.x + tabWidth, rect.y, tabWidth, 30f);
            Rect filterTabRect = new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, 30f);

            // Basic Settings Tab
            GUI.color = currentTab == SettingsTab.Basic ? Color.white : Color.gray;
            if (Widgets.ButtonText(basicTabRect, "RimTalk.Settings.BasicSettings".Translate()))
            {
                currentTab = SettingsTab.Basic;
            }

            // AI Instruction Tab
            GUI.color = currentTab == SettingsTab.AIInstruction ? Color.white : Color.gray;
            if (Widgets.ButtonText(instructionTabRect, "RimTalk.Settings.AIInstruction".Translate()))
            {
                currentTab = SettingsTab.AIInstruction;
            }

            // Event Filter Tab
            GUI.color = currentTab == SettingsTab.EventFilter ? Color.white : Color.gray;
            if (Widgets.ButtonText(filterTabRect, "RimTalk.Settings.EventFilter".Translate()))
            {
                currentTab = SettingsTab.EventFilter;
                if (!archivableTypesScanned)
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

            // Create scrollable area for the current tab content
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentRect.height);

            viewRect = new Rect(0f, 0f, contentRect.width - 16f, 800f);

            mainScrollPosition = GUI.BeginScrollView(contentRect, mainScrollPosition, viewRect);
            switch (currentTab)
            {
                case SettingsTab.Basic:
                    DrawBasicSettings(viewRect);
                    break;
                case SettingsTab.AIInstruction:
                    DrawAIInstructionSettings(viewRect);
                    break;
                case SettingsTab.EventFilter:
                    DrawEventFilterSettings(viewRect);
                    break;
            }

            GUI.EndScrollView();
        }
    }
}