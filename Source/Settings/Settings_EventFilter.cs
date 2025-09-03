using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimTalk
{
    public partial class Settings
    {
        private void DrawEventFilterSettings(Rect rect)
        {
            CurrentWorkDisplayModSettings settings = Get();
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(rect);

            // Instructions
            Text.Font = GameFont.Tiny;
            GUI.color = Color.cyan;
            listingStandard.Label("RimTalk.Settings.EventFilterTip".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listingStandard.Gap(6f);

            // Group types by category for better organization
            var groupedTypes = discoveredArchivableTypes
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

            // Scrollable list of archivable types with checkboxes, grouped by category
            if (discoveredArchivableTypes.Any())
            {
                float totalHeight = groupedTypes.Sum(group => 30f + group.Count() * 25f); // Category headers + items
                float availableHeight = rect.height - 80f; // Reserve space for instructions and reset button
                Rect archivableListRect = listingStandard.GetRect(availableHeight);
                Rect archivableViewRect = new Rect(0f, 0f, archivableListRect.width - 16f, totalHeight);

                archivableScrollPosition =
                    GUI.BeginScrollView(archivableListRect, archivableScrollPosition, archivableViewRect);

                float currentY = 0f;
                foreach (var group in groupedTypes)
                {
                    // Category header
                    Rect categoryRect = new Rect(0f, currentY, archivableViewRect.width, 25f);
                    Text.Font = GameFont.Small;
                    GUI.color = Color.yellow;
                    Widgets.Label(categoryRect, $"━━ {group.Key} ({group.Count()}) ━━");
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    currentY += 30f;

                    // Items in this category
                    foreach (var typeName in group.OrderBy(x => x))
                    {
                        Rect checkboxRect = new Rect(10f, currentY, archivableViewRect.width - 10f, 22f);

                        bool isEnabled = settings.enabledArchivableTypes.ContainsKey(typeName)
                            ? settings.enabledArchivableTypes[typeName]
                            : false;

                        bool newEnabled = isEnabled;

                        // Highlight Verse.Message in red if it's enabled (since it should normally be off)
                        if (typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase) && isEnabled)
                        {
                            GUI.color = Color.red;
                        }

                        Widgets.CheckboxLabeled(checkboxRect, typeName, ref newEnabled);
                        GUI.color = Color.white;

                        if (newEnabled != isEnabled)
                        {
                            settings.enabledArchivableTypes[typeName] = newEnabled;
                        }

                        currentY += 25f;
                    }
                }

                GUI.EndScrollView();
            }
            else
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.yellow;
                listingStandard.Label("RimTalk.Settings.NoArchivableTypes".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            listingStandard.Gap(6f);

            // Reset to defaults button at the bottom
            Rect resetButtonRect = listingStandard.GetRect(30f);
            if (Widgets.ButtonText(resetButtonRect, "RimTalk.Settings.ResetToDefault".Translate()))
            {
                // Reset all archivable types to default values
                foreach (var typeName in discoveredArchivableTypes)
                {
                    // Enable by default for most types, but disable Verse.Message specifically
                    bool defaultEnabled = !typeName.Equals("Verse.Message", StringComparison.OrdinalIgnoreCase);
                    settings.enabledArchivableTypes[typeName] = defaultEnabled;
                }
            }

            listingStandard.End();
        }
    }
}
