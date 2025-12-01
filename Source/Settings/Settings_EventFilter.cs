using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimTalk;

public partial class Settings
{
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

        // Group types by category for better organization
        var groupedTypes = _discoveredArchivableTypes
            .GroupBy(typeName =>
            {
                string simpleName = typeName.Contains(".")
                    ? typeName.Substring(typeName.LastIndexOf('.') + 1)
                    : typeName;

                if (simpleName.Contains("Letter")) return "Letters";
                if (simpleName.Contains("Message")) return "Messages";
                return "Other";
            })
            .OrderBy(g => g.Key == "Letters" ? 0 : g.Key == "Messages" ? 1 : 2)
            .ToList();

        // Draw archivable types with checkboxes, grouped by category
        if (_discoveredArchivableTypes.Any())
        {
            foreach (var group in groupedTypes)
            {
                // Category header
                Text.Font = GameFont.Small;
                GUI.color = Color.yellow;
                var categoryHeader = $"━━ {group.Key} ({group.Count()}) ━━";
                var categoryHeaderRect = listingStandard.GetRect(Text.CalcHeight(categoryHeader, listingStandard.ColumnWidth));
                Widgets.Label(categoryHeaderRect, categoryHeader);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                listingStandard.Gap(6f);

                // Items in this category
                foreach (var typeName in group.OrderBy(x => x))
                {
                    bool isEnabled = settings.EnabledArchivableTypes.ContainsKey(typeName)
                        ? settings.EnabledArchivableTypes[typeName]
                        : false;

                    bool newEnabled = isEnabled;

                    // Highlight Verse.Message in red if it's enabled (since it should normally be off)
                    if (typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase) && isEnabled)
                    {
                        GUI.color = Color.red;
                    }

                    listingStandard.CheckboxLabeled(typeName, ref newEnabled, typeName);
                    GUI.color = Color.white;

                    if (newEnabled != isEnabled)
                    {
                        settings.EnabledArchivableTypes[typeName] = newEnabled;
                    }
                }

                listingStandard.Gap();
            }
        }
        else
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.yellow;
            var noArchivableTypes = "RimTalk.Settings.NoArchivableTypes".Translate();
            var noArchivableTypesRect = listingStandard.GetRect(Text.CalcHeight(noArchivableTypes, listingStandard.ColumnWidth));
            Widgets.Label(noArchivableTypesRect, noArchivableTypes);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        listingStandard.Gap(6f);

        // Reset to defaults button at the bottom
        Rect resetButtonRect = listingStandard.GetRect(30f);
        if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
        {
            // Reset all archivable types to default values
            foreach (var typeName in _discoveredArchivableTypes)
            {
                // Enable by default for most types, but disable Verse.Message specifically
                bool defaultEnabled = !typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase);
                settings.EnabledArchivableTypes[typeName] = defaultEnabled;
            }
        }
    }
}