using System;
using System.Collections.Generic;
using System.Reflection;
using RimTalk.Data;
using UnityEngine;
using Verse;

namespace RimTalk
{
    public enum ContextPreset
    {
        Essential,
        Standard,
        Comprehensive,
        Custom
    }

    public partial class Settings
    {
        private ContextPreset _currentPreset = ContextPreset.Custom;
        private readonly ContextSettings _changeBuffer = new();
        private bool _presetInitialized; 

        private static readonly Dictionary<ContextPreset, ContextSettings> PresetDefinitions = new()
        {
            { ContextPreset.Essential, new ContextSettings {
                EnableContextOptimization = true,
                MaxPawnContextCount = 2,
                ConversationHistoryCount = 1,
                
                IncludeRace = true,
                IncludeNotableGenes = false,
                IncludeIdeology = false,
                IncludeBackstory = true,
                IncludeTraits = true,
                IncludeSkills = false,
                IncludeHealth = true,
                IncludeMood = true,
                IncludeThoughts = true,
                IncludeRelations = true,
                IncludeEquipment = false,
                IncludePrisonerSlaveStatus = false,
                
                IncludeTimeAndDate = false,
                IncludeSeason = false,
                IncludeWeather = true,
                IncludeLocationAndTemperature = false,
                IncludeTerrain = false,
                IncludeBeauty = false,
                IncludeCleanliness = false,
                IncludeSurroundings = false,
                IncludeWealth = false
            }},
            { ContextPreset.Standard, new ContextSettings {
                EnableContextOptimization = false,
                MaxPawnContextCount = 3,
                ConversationHistoryCount = 1,
                
                IncludeRace = true,
                IncludeNotableGenes = true,
                IncludeIdeology = true,
                IncludeBackstory = true,
                IncludeTraits = true,
                IncludeSkills = true,
                IncludeHealth = true,
                IncludeMood = true,
                IncludeThoughts = true,
                IncludeRelations = true,
                IncludeEquipment = true,
                IncludePrisonerSlaveStatus = false,
                
                IncludeTimeAndDate = true,
                IncludeSeason = true,
                IncludeWeather = true,
                IncludeLocationAndTemperature = true,
                IncludeTerrain = false,
                IncludeBeauty = false,
                IncludeCleanliness = false,
                IncludeSurroundings = false,
                IncludeWealth = false
            }},
            { ContextPreset.Comprehensive, new ContextSettings {
                EnableContextOptimization = false,
                MaxPawnContextCount = 3,
                ConversationHistoryCount = 3,
                
                IncludeRace = true,
                IncludeNotableGenes = true,
                IncludeIdeology = true,
                IncludeBackstory = true,
                IncludeTraits = true,
                IncludeSkills = true,
                IncludeHealth = true,
                IncludeMood = true,
                IncludeThoughts = true,
                IncludeRelations = true,
                IncludeEquipment = true,
                IncludePrisonerSlaveStatus = true,
                
                IncludeTimeAndDate = true,
                IncludeSeason = true,
                IncludeWeather = true,
                IncludeLocationAndTemperature = true,
                IncludeTerrain = true,
                IncludeBeauty = true,
                IncludeCleanliness = true,
                IncludeSurroundings = true,
                IncludeWealth = true
            }}
        };

        private void DrawContextFilterSettings(Listing_Standard listing)
        {
            RimTalkSettings settings = Get();
            ContextSettings context = settings.Context;
            
            if (!_presetInitialized)
            {
                DetermineCurrentPreset(context);
                _presetInitialized = true;
            }

            var contextFilterDesc = "RimTalk.Settings.ContextFilterDescription".Translate();
            Widgets.Label(listing.GetRect(Text.CalcHeight(contextFilterDesc, listing.ColumnWidth)), contextFilterDesc);
            listing.Gap(6f);

            Text.Font = GameFont.Tiny;
            GUI.color = Color.cyan;
            Widgets.Label(listing.GetRect(Text.LineHeight), "RimTalk.Settings.ContextFilterTip".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(12f);

            // Preset Selectors
            DrawPresetSelector(listing, context);

            CopyFields(context, _changeBuffer);

            // General Options
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 0.85f, 0.5f);
            listing.Label("RimTalk.Settings.ContextOptions".Translate());
            GUI.color = Color.white;
            listing.Gap(6f);

            listing.CheckboxLabeled("RimTalk.Settings.EnableContextOptimization".Translate(),
                ref context.EnableContextOptimization,
                "RimTalk.Settings.EnableContextOptimization.Tooltip".Translate());
            listing.Gap(6f);

            DrawDropdown(listing, "RimTalk.Settings.MaxPawnContextCount", context.MaxPawnContextCount, 
                val => { context.MaxPawnContextCount = val; _currentPreset = ContextPreset.Custom; }, 2, 7);
            listing.Gap(6f);

            DrawDropdown(listing, "RimTalk.Settings.ConversationHistoryCount", context.ConversationHistoryCount, 
                val => { context.ConversationHistoryCount = val; _currentPreset = ContextPreset.Custom; }, 0, 7);
            listing.Gap();

            DrawColumns(listing, context);

            if (_currentPreset != ContextPreset.Custom && !AreSettingsEqual(_changeBuffer, context))
                _currentPreset = ContextPreset.Custom;

            listing.Gap(24f);

            // Reset
            if (listing.ButtonText("RimTalk.Settings.ResetToDefault".Translate()))
            {
                settings.Context = new ContextSettings();
                ApplyPreset(settings.Context, ContextPreset.Standard);
            }
        }

        private void DrawPresetSelector(Listing_Standard listing, ContextSettings context)
        {
            GUI.color = new Color(1f, 0.85f, 0.5f);
            Widgets.Label(listing.GetRect(Text.LineHeight), "RimTalk.Settings.ContextPresets".Translate());
            GUI.color = Color.white;
            listing.Gap(8f);

            const float boxGap = 12f;
            const float boxHeight = 70f;
            float totalWidth = listing.ColumnWidth;
            float boxWidth = (totalWidth - boxGap * 3f) / 4f;
            Rect rowRect = listing.GetRect(boxHeight);

            int i = 0;
            foreach (ContextPreset preset in Enum.GetValues(typeof(ContextPreset)))
            {
                Rect boxRect = new Rect(rowRect.x + (boxWidth + boxGap) * i, rowRect.y, boxWidth, boxHeight);
                DrawSinglePresetBox(boxRect, preset, context);
                i++;
            }
            listing.Gap();
        }

        private void DrawSinglePresetBox(Rect rect, ContextPreset preset, ContextSettings context)
        {
            bool isSelected = _currentPreset == preset;
            
            Widgets.DrawBoxSolid(rect, isSelected ? new Color(0.2f, 0.4f, 0.6f, 0.8f) : new Color(0.2f, 0.2f, 0.2f, 0.5f));
            GUI.color = isSelected ? new Color(0.4f, 0.7f, 1f, 1f) : new Color(0.3f, 0.3f, 0.3f, 0.5f);
            Widgets.DrawBox(rect, 2);
            GUI.color = Color.white;

            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            if (Widgets.ButtonInvisible(rect))
            {
                _currentPreset = preset;
                if (preset != ContextPreset.Custom) ApplyPreset(context, preset);
            }

            Rect content = rect.ContractedBy(8f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            
            GUI.color = isSelected ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(new Rect(content.x, content.y, content.width, Text.LineHeight), $"RimTalk.Settings.Preset.{preset}".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = isSelected ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(content.x, content.y + Text.LineHeight + 4f, content.width, content.height - Text.LineHeight - 4f), $"RimTalk.Settings.Preset.{preset}.Desc".Translate());
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawColumns(Listing_Standard listing, ContextSettings context)
        {
            const float columnGap = 200f;
            float columnWidth = (listing.ColumnWidth - columnGap) / 2;
            Rect positionRect = listing.GetRect(0f); 

            // Left Column
            Rect leftRect = new Rect(positionRect.x, positionRect.y, columnWidth, 9999f);
            Listing_Standard leftListing = new Listing_Standard();
            leftListing.Begin(leftRect);

            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            leftListing.Label($"━━ {"RimTalk.Settings.PawnInfo".Translate()} ━━");
            GUI.color = Color.white;
            leftListing.Gap(6f);

            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeRace".Translate(), ref context.IncludeRace);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeNotableGenes".Translate(), ref context.IncludeNotableGenes);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeIdeology".Translate(), ref context.IncludeIdeology);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeBackstory".Translate(), ref context.IncludeBackstory);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeTraits".Translate(), ref context.IncludeTraits);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeSkills".Translate(), ref context.IncludeSkills);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeHealth".Translate(), ref context.IncludeHealth);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeMood".Translate(), ref context.IncludeMood);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeThoughts".Translate(), ref context.IncludeThoughts);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeRelations".Translate(), ref context.IncludeRelations);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludeEquipment".Translate(), ref context.IncludeEquipment);
            leftListing.CheckboxLabeled("RimTalk.Settings.IncludePrisonerSlaveStatus".Translate(), ref context.IncludePrisonerSlaveStatus);

            leftListing.End();

            // Right Column
            Rect rightRect = new Rect(leftRect.xMax + columnGap, positionRect.y, columnWidth, 9999f);
            Listing_Standard rightListing = new Listing_Standard();
            rightListing.Begin(rightRect);

            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            rightListing.Label($"━━ {"RimTalk.Settings.Environment".Translate()} ━━");
            GUI.color = Color.white;
            rightListing.Gap(6f);

            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeTimeAndDate".Translate(), ref context.IncludeTimeAndDate);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeSeason".Translate(), ref context.IncludeSeason);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeWeather".Translate(), ref context.IncludeWeather);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeLocationAndTemperature".Translate(), ref context.IncludeLocationAndTemperature);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeTerrain".Translate(), ref context.IncludeTerrain);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeBeauty".Translate(), ref context.IncludeBeauty);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeCleanliness".Translate(), ref context.IncludeCleanliness);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeSurroundings".Translate(), ref context.IncludeSurroundings);
            rightListing.CheckboxLabeled("RimTalk.Settings.IncludeWealth".Translate(), ref context.IncludeWealth);

            rightListing.End();

            listing.Gap(Mathf.Max(leftListing.CurHeight, rightListing.CurHeight));
        }

        private void ApplyPreset(ContextSettings context, ContextPreset preset)
        {
            if (PresetDefinitions.TryGetValue(preset, out var source))
            {
                CopyFields(source, context);
                _currentPreset = preset;
            }
        }

        private void CopyFields<T>(T source, T target)
        {
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                field.SetValue(target, field.GetValue(source));
            }
        }

        private bool AreSettingsEqual(ContextSettings a, ContextSettings b)
        {
            foreach (var field in typeof(ContextSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var valA = field.GetValue(a);
                var valB = field.GetValue(b);
                if (!object.Equals(valA, valB)) return false;
            }
            return true;
        }

        private void DrawDropdown(Listing_Standard listing, string labelKey, int currentValue, Action<int> onSelect, int min, int max)
        {
            const float dropdownWidth = 120f;
            Rect rowRect = listing.GetRect(24f);
            Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - dropdownWidth - 10f, rowRect.height);
            Rect dropdownRect = new Rect(rowRect.xMax - dropdownWidth, rowRect.y, dropdownWidth, rowRect.height);

            Widgets.Label(labelRect, labelKey.Translate());
            TooltipHandler.TipRegion(rowRect, (labelKey + ".Tooltip").Translate());

            if (Widgets.ButtonText(dropdownRect, currentValue.ToString()))
            {
                List<FloatMenuOption> options = [];
                for (int i = min; i <= max; i++)
                {
                    int count = i;
                    options.Add(new FloatMenuOption(count.ToString(), () => onSelect(count)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        
        private void DetermineCurrentPreset(ContextSettings current)
        {
            _currentPreset = ContextPreset.Custom;
            foreach (var entry in PresetDefinitions)
            {
                if (AreSettingsEqual(current, entry.Value))
                {
                    _currentPreset = entry.Key;
                    break;
                }
            }
        }
    }
}