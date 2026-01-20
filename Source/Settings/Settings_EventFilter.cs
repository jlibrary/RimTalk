using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;

namespace RimTalk;

public partial class Settings
{
    private List<string> _discoveredArchivableTypes = [];
    private Dictionary<string, List<string>> _typeHierarchy = new();
    private Dictionary<string, string> _sourceMap = new();
    private readonly HashSet<string> _expandedParents = new();
    private bool _archivableTypesScanned;
    private const string Core = "Core";
    private const string VerseMessage = "Verse.Message";

    private void ScanForArchivableTypes()
    {
        if (_archivableTypesScanned) return;

        var archivableTypes = new HashSet<string>();
        _typeHierarchy = new Dictionary<string, List<string>>();
        _sourceMap = new Dictionary<string, string>();
        var likelyCoreTypes = new HashSet<string>();

        var assemblyToMod = new Dictionary<Assembly, string>();
        foreach (var mod in LoadedModManager.RunningMods)
        {
            foreach (var asm in mod.assemblies.loadedAssemblies.Where(asm => !assemblyToMod.ContainsKey(asm)))
            {
                assemblyToMod[asm] = mod.Name;
            }
        }

        // 1. Scan Assemblies for IArchivable types (The Parents/Mechanisms)
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                string modName = null;
                bool isLikelyCore = false;

                if (assemblyToMod.TryGetValue(assembly, out var mName))
                {
                    modName = mName;
                }
                else
                {
                    var asmName = assembly.GetName().Name;
                    if (asmName.StartsWith("Assembly-CSharp") ||
                        asmName.StartsWith("RimWorld") ||
                        asmName.StartsWith("Verse") ||
                        asmName.StartsWith("UnityEngine"))
                    {
                        isLikelyCore = true;
                    }
                }

                var types = assembly.GetTypes()
                    .Where(t => typeof(IArchivable).IsAssignableFrom(t) &&
                                !t.IsInterface &&
                                !t.IsAbstract)
                    .Select(t => t.FullName)
                    .ToList();

                foreach (var type in types)
                {
                    archivableTypes.Add(type);
                    if (!_typeHierarchy.ContainsKey(type))
                    {
                        _typeHierarchy[type] = new List<string>();
                    }

                    if (modName != null)
                        _sourceMap[type] = modName;

                    if (isLikelyCore)
                        likelyCoreTypes.Add(type);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error scanning assembly {assembly.FullName}: {ex.Message}");
            }
        }

        // 2. Scan specific instances currently in Archive (fallback)
        if (Current.Game != null && Find.Archive != null)
        {
            foreach (var archivable in Find.Archive.ArchivablesListForReading)
            {
                var typeName = archivable.GetType().FullName;
                archivableTypes.Add(typeName);
                if (!_typeHierarchy.ContainsKey(typeName))
                {
                    _typeHierarchy[typeName] = [];
                }

                if (!_sourceMap.ContainsKey(typeName))
                {
                    if (likelyCoreTypes.Contains(typeName))
                        _sourceMap[typeName] = Core;
                    else
                        _sourceMap[typeName] = Core; // Fallback
                }
            }
        }

        // 3. Scan Defs (The Children/Events) and map to Parents
        foreach (var def in DefDatabase<LetterDef>.AllDefs)
        {
            var parentType = def.letterClass?.FullName;
            
            if (string.IsNullOrEmpty(parentType)) continue;
            
            archivableTypes.Add(parentType);
            archivableTypes.Add(def.defName);

            if (!_typeHierarchy.ContainsKey(parentType))
                _typeHierarchy[parentType] = new List<string>();

            if (!_typeHierarchy[parentType].Contains(def.defName))
                _typeHierarchy[parentType].Add(def.defName);

            string defSource = def.modContentPack?.Name ?? Core;
            _sourceMap[def.defName] = defSource;

            if (!_sourceMap.ContainsKey(parentType))
                _sourceMap[parentType] = defSource;
        }

        foreach (var def in DefDatabase<MessageTypeDef>.AllDefs)
        {
            string parentType = VerseMessage;

            if (string.IsNullOrEmpty(parentType)) continue;

            archivableTypes.Add(parentType);
            archivableTypes.Add(def.defName);

            if (!_typeHierarchy.ContainsKey(parentType))
                _typeHierarchy[parentType] = new List<string>();

            if (!_typeHierarchy[parentType].Contains(def.defName))
                _typeHierarchy[parentType].Add(def.defName);

            // Store Source
            string defSource = def.modContentPack?.Name ?? Core;
            _sourceMap[def.defName] = defSource;

            // Ensure parent has a source
            if (!_sourceMap.ContainsKey(parentType))
                _sourceMap[parentType] = defSource;
        }

        // 4. Finalize
        // Fill in any still-unknown parents that were flagged as Likely Core
        foreach (var type in archivableTypes.Where(type =>
                     !_sourceMap.ContainsKey(type) && likelyCoreTypes.Contains(type)))
        {
            _sourceMap[type] = Core;
        }

        // Deduplicate: If a type appears in Verse.Message, remove it from other parents (e.g. StandardLetter)
        // This prevents double entries for things like "NegativeEvent" which exist as both LetterDef and MessageTypeDef
        if (_typeHierarchy.TryGetValue(VerseMessage, out var msgChildren))
        {
            var messageKeys = new HashSet<string>(msgChildren);
            foreach (List<string> children in from parent in _typeHierarchy.Keys.ToList()
                     where parent != VerseMessage
                     select _typeHierarchy[parent])
            {
                children.RemoveAll(child => messageKeys.Contains(child));
            }
        }

        _discoveredArchivableTypes = archivableTypes.OrderBy(x => x).ToList();

        // Sort children for UI consistency
        foreach (var key in _typeHierarchy.Keys.ToList())
        {
            _typeHierarchy[key].Sort();
        }

        _archivableTypesScanned = true;

        RimTalkSettings settings = Get();

        // Identify all Message-related types (Parent + Children) to disable them by default
        var messageTypes = new HashSet<string> { VerseMessage };
        if (_typeHierarchy.TryGetValue(VerseMessage, out var messageChildren))
        {
            foreach (var child in messageChildren) messageTypes.Add(child);
        }

        foreach (var typeName in _discoveredArchivableTypes)
        {
            if (!settings.EnabledArchivableTypes.ContainsKey(typeName))
            {
                // Disable messages by default, enable everything else
                bool defaultEnabled = !messageTypes.Contains(typeName);
                settings.EnabledArchivableTypes[typeName] = defaultEnabled;
            }
        }

        Logger.Message(
            $"Discovered {_discoveredArchivableTypes.Count} archivable types across {_typeHierarchy.Count} parent categories.");
    }

    private void DrawEventFilterSettings(Listing_Standard listingStandard)
    {
        RimTalkSettings settings = Get();

        // Instructions
        Text.Font = GameFont.Tiny;
        GUI.color = Color.cyan;
        var eventFilterTip = "RimTalk.Settings.EventFilterTip".Translate();
        var eventFilterTipRect = listingStandard.GetRect(Text.CalcHeight(eventFilterTip, listingStandard.ColumnWidth));
        Widgets.Label(eventFilterTipRect, eventFilterTip);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        listingStandard.Gap(6f);

        if (_typeHierarchy.Any())
        {
            var sortedParents = _typeHierarchy.Keys
                .OrderBy(k => k.Equals(VerseMessage, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(k => _typeHierarchy[k].Count)
                .ThenBy(k => k)
                .ToList();

            foreach (var parentKey in sortedParents)
            {
                var children = _typeHierarchy[parentKey];
                bool hasChildren = children.Any();
                bool showExpander = hasChildren && children.Count > 1;
                bool isExpanded = _expandedParents.Contains(parentKey);

                // --- Parent Row ---
                Rect parentRect = listingStandard.GetRect(24f);
                float xOffset = 0f;

                // 1. Expander Button
                if (showExpander)
                {
                    Rect expanderRect = new Rect(parentRect.x, parentRect.y, 24f, 24f);
                    string label = isExpanded ? "[-]" : "[+]";
                    if (Widgets.ButtonText(expanderRect, label, drawBackground: false))
                    {
                        if (isExpanded) _expandedParents.Remove(parentKey);
                        else _expandedParents.Add(parentKey);
                    }
                }

                xOffset += 28f;

                // 2. Parent Checkbox & Label
                bool isParentEnabled = settings.EnabledArchivableTypes.TryGetValue(parentKey, out var pVal) && pVal;
                bool newParentEnabled = isParentEnabled;

                Rect checkboxRect = new Rect(parentRect.x + xOffset, parentRect.y, parentRect.width - xOffset, 24f);
                Widgets.CheckboxLabeled(checkboxRect, parentKey, ref newParentEnabled);

                // Draw Source (Mod Name)
                if (_sourceMap.TryGetValue(parentKey, out var pSource) &&
                    !string.IsNullOrEmpty(pSource) &&
                    pSource != Core)
                {
                    float nameWidth = Text.CalcSize(parentKey).x;
                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    Rect sourceRect = new Rect(checkboxRect.x + nameWidth + 10f, checkboxRect.y + 2f, 300f, 24f);
                    Widgets.Label(sourceRect, $"({pSource})");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }

                if (newParentEnabled != isParentEnabled)
                {
                    settings.EnabledArchivableTypes[parentKey] = newParentEnabled;
                    // Auto-toggle children
                    if (hasChildren)
                    {
                        foreach (var child in children)
                            settings.EnabledArchivableTypes[child] = newParentEnabled;
                    }
                }

                // --- Children Rows ---
                if (!showExpander || !isExpanded) continue;

                foreach (var childKey in children)
                {
                    Rect childRect = listingStandard.GetRect(24f);
                    childRect.xMin += 40f; // Indent

                    bool isChildEnabled = settings.EnabledArchivableTypes.TryGetValue(childKey, out var cVal) && cVal;
                    bool newChildEnabled = isChildEnabled;

                    Widgets.CheckboxLabeled(childRect, childKey, ref newChildEnabled);

                    // Draw Source (Mod Name)
                    if (_sourceMap.TryGetValue(childKey, out var cSource) &&
                        !string.IsNullOrEmpty(cSource) &&
                        cSource != Core)
                    {
                        float nameWidth = Text.CalcSize(childKey).x;
                        Text.Font = GameFont.Tiny;
                        GUI.color = Color.gray;
                        Rect sourceRect = new Rect(childRect.x + nameWidth + 10f, childRect.y + 2f, 300f, 24f);
                        Widgets.Label(sourceRect, $"({cSource})");
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;
                    }

                    if (newChildEnabled != isChildEnabled)
                    {
                        settings.EnabledArchivableTypes[childKey] = newChildEnabled;
                        // If child enabled -> Force Parent Enabled
                        if (newChildEnabled && !settings.EnabledArchivableTypes[parentKey])
                        {
                            settings.EnabledArchivableTypes[parentKey] = true;
                        }
                    }
                }
            }
        }
        else
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.yellow;
            var noArchivableTypes = "RimTalk.Settings.NoArchivableTypes".Translate();
            var noArchivableTypesRect =
                listingStandard.GetRect(Text.CalcHeight(noArchivableTypes, listingStandard.ColumnWidth));
            Widgets.Label(noArchivableTypesRect, noArchivableTypes);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        listingStandard.Gap(6f);

        // Reset to defaults button
        Rect resetButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            // Identify all Message-related types (Parent + Children) to disable them by default
            var messageTypes = new HashSet<string> { VerseMessage };
            if (_typeHierarchy.TryGetValue(VerseMessage, out var messageChildren))
            {
                foreach (var child in messageChildren) messageTypes.Add(child);
            }

            foreach (var typeName in _discoveredArchivableTypes)
            {
                bool defaultEnabled = !messageTypes.Contains(typeName);
                settings.EnabledArchivableTypes[typeName] = defaultEnabled;
            }
        }
    }
}