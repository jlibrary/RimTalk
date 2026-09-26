using System;
using System.Collections.Generic;
using System.Reflection;
using RimTalk.Data;
using RimTalk.Service;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        private static readonly Color SoftCyan = new(0.40f, 0.80f, 0.90f);
        private ContextPreset _currentPreset = ContextPreset.Custom;
        private readonly ContextSettings _changeBuffer = new();
        private bool _presetInitialized; 

        private static readonly Dictionary<ContextPreset, ContextSettings> PresetDefinitions = new()
        {
            { ContextPreset.Essential, new ContextSettings {
                EnableContextOptimization = true,
                MaxPawnContextCount = 2,
                ConversationHistoryCount = 2,
                MaxEventsCount = 3,
                UseCompactHistory = false,
                
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
                
                IncludeTime = false,
                IncludeDate = false,
                IncludeSeason = false,
                IncludeWeather = true,
                IncludeLocationAndTemperature = false,
                IncludeTerrain = false,
                IncludeBeauty = false,
                IncludeCleanliness = false,
                IncludeSurroundings = false,
                IncludeWealth = false,
                IncludeEvents = false,
                IncludeTopicKeywords = true,
                EnableMemory = false
            }},
            { ContextPreset.Standard, new ContextSettings {
                EnableContextOptimization = false,
                MaxPawnContextCount = 3,
                ConversationHistoryCount = 3,
                MaxEventsCount = 5,
                UseCompactHistory = false,
                
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
                
                IncludeTime = true,
                IncludeDate = false,
                IncludeSeason = true,
                IncludeWeather = true,
                IncludeLocationAndTemperature = true,
                IncludeTerrain = false,
                IncludeBeauty = false,
                IncludeCleanliness = false,
                IncludeSurroundings = false,
                IncludeWealth = false,
                IncludeEvents = EventService.DefaultIncludeEvents,
                IncludeTopicKeywords = true,
                EnableMemory = false
            }},
            { ContextPreset.Comprehensive, new ContextSettings {
                EnableContextOptimization = false,
                MaxPawnContextCount = 3,
                ConversationHistoryCount = 3,
                MaxEventsCount = 7,
                UseCompactHistory = true,
                
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
                
                IncludeTime = true,
                IncludeDate = true,
                IncludeSeason = true,
                IncludeWeather = true,
                IncludeLocationAndTemperature = true,
                IncludeTerrain = true,
                IncludeBeauty = true,
                IncludeCleanliness = true,
                IncludeSurroundings = true,
                IncludeWealth = true,
                IncludeEvents = EventService.DefaultIncludeEvents,
                IncludeTopicKeywords = true,
                EnableMemory = true
            }}
        };

        private void DrawContextFilterSettings(Listing_Standard listing)
        {
            RimTalkSettings settings = Get();
            ContextSettings context = settings.Context;
            
            if (!_presetInitialized)
            {
                DetermineCurrentPreset(context);
                _maxPawnContextBuffer = context.MaxPawnContextCount.ToString();
                _conversationHistoryBuffer = context.ConversationHistoryCount.ToString();
                _maxEventsBuffer = context.MaxEventsCount.ToString();
                _presetInitialized = true;
            }

            var contextFilterDesc = "RimTalk.Settings.ContextFilterDescription".Translate();
            Widgets.Label(listing.GetRect(Text.CalcHeight(contextFilterDesc, listing.ColumnWidth)), contextFilterDesc);
            listing.Gap(6f);

            Text.Font = GameFont.Tiny;
            GUI.color = SoftCyan;
            Widgets.Label(listing.GetRect(Text.LineHeight), "RimTalk.Settings.ContextFilterTip".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(8f);

            // Preset Selectors
            DrawPresetSelector(listing, context);

            CopyFields(context, _changeBuffer);

            // General Options - 2 Columns
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 0.85f, 0.5f);
            listing.Label("RimTalk.Settings.ContextOptions".Translate());
            GUI.color = Color.white;
            listing.Gap(4f);

            const float genColumnGap = 24f;
            float genColWidth = (listing.ColumnWidth - genColumnGap) / 2f;

            bool hasExtEventMod = EventService.IsExternalEventModActive;
            string extEventModNames = hasExtEventMod ? EventService.GetActiveExternalEventModNames() : string.Empty;

            float extraHeight = hasExtEventMod ? 22f : 0f;
            Rect genSectionRect = listing.GetRect(80f + extraHeight);

            // Left General Options Column (Feature Toggles)
            Rect genLeftRect = new Rect(genSectionRect.x, genSectionRect.y, genColWidth, genSectionRect.height);
            Listing_Standard genLeftListing = new Listing_Standard();
            genLeftListing.Begin(genLeftRect);

            CheckboxLeft(genLeftListing, "RimTalk.Settings.IncludeTopicKeywords".Translate(),
                ref context.IncludeTopicKeywords,
                "RimTalk.Settings.IncludeTopicKeywords.Tooltip".Translate());
            genLeftListing.Gap(4f);

            CheckboxLeft(genLeftListing, "RimTalk.Settings.EnableMemory".Translate(),
                ref context.EnableMemory,
                "RimTalk.Settings.EnableMemory.Tooltip".Translate());
            genLeftListing.Gap(4f);

            CheckboxLeft(genLeftListing, "RimTalk.Settings.EnableContextOptimization".Translate(),
                ref context.EnableContextOptimization,
                "RimTalk.Settings.EnableContextOptimization.Tooltip".Translate());
            genLeftListing.End();

            // Right General Options Column (Limits & Filter Action)
            Rect genRightRect = new Rect(genLeftRect.xMax + genColumnGap, genSectionRect.y, genColWidth, genSectionRect.height);
            Listing_Standard genRightListing = new Listing_Standard();
            genRightListing.Begin(genRightRect);

            DrawNumericInput(genRightListing, "RimTalk.Settings.MaxPawnContextCount", ref context.MaxPawnContextCount, ref _maxPawnContextBuffer, 1, 9999);
            genRightListing.Gap(4f);

            DrawNumericInput(genRightListing, "RimTalk.Settings.ConversationHistoryCount", ref context.ConversationHistoryCount, ref _conversationHistoryBuffer, 0, 9999);
            genRightListing.Gap(4f);

            DrawCheckboxWithNumericInput(genRightListing, "RimTalk.Settings.IncludeEvents", ref context.IncludeEvents, ref context.MaxEventsCount, ref _maxEventsBuffer, 1, 9999,
                onGearClicked: OpenEventFilterDialog, gearTooltipKey: "RimTalk.Settings.EventFilterTip");
            if (hasExtEventMod)
            {
                genRightListing.Gap(2f);
                string eventModMsg = "RimTalk.Settings.ExternalEventModDetected".Translate(extEventModNames).ToString();
                GUI.color = new Color(1f, 0.85f, 0.5f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(genRightListing.GetRect(18f), eventModMsg);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            genRightListing.End();

            listing.Gap(16f);

            DrawColumns(listing, context);

            if (_currentPreset != ContextPreset.Custom && !AreSettingsEqual(_changeBuffer, context))
                _currentPreset = ContextPreset.Custom;
        }

        internal void ResetContextSettings(RimTalkSettings settings)
        {
            settings.Context = new ContextSettings();
            ApplyPreset(settings.Context, ContextPreset.Standard);
            _maxPawnContextBuffer = settings.Context.MaxPawnContextCount.ToString();
            _conversationHistoryBuffer = settings.Context.ConversationHistoryCount.ToString();
            _maxEventsBuffer = settings.Context.MaxEventsCount.ToString();
            _currentPreset = ContextPreset.Standard;
        }

        private void DrawPresetSelector(Listing_Standard listing, ContextSettings context)
        {
            GUI.color = new Color(1f, 0.85f, 0.5f);
            Widgets.Label(listing.GetRect(Text.LineHeight), "RimTalk.Settings.ContextPresets".Translate());
            GUI.color = Color.white;
            listing.Gap(4f);

            const float boxGap = 8f;
            const float boxHeight = 48f;
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
            listing.Gap(6f);
        }

        private void DrawSinglePresetBox(Rect rect, ContextPreset preset, ContextSettings context)
        {
            rect = rect.Rounded();
            bool isSelected = _currentPreset == preset;
            
            Widgets.DrawBoxSolid(rect, isSelected ? new Color(0.22f, 0.33f, 0.45f, 0.90f) : new Color(0.10f, 0.11f, 0.13f, 0.85f));
            GUI.color = isSelected ? new Color(0.40f, 0.65f, 0.85f, 0.95f) : SectionBorderColor;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            if (Widgets.ButtonInvisible(rect))
            {
                if (!isSelected)
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                else
                    SoundDefOf.Click.PlayOneShotOnCamera(null);

                _currentPreset = preset;
                if (preset != ContextPreset.Custom) ApplyPreset(context, preset);
            }

            Rect content = rect.ContractedBy(4f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            
            GUI.color = isSelected ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(new Rect(content.x, content.y + 2f, content.width, Text.LineHeight), $"RimTalk.Settings.Preset.{preset}".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = isSelected ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(content.x, content.y + Text.LineHeight + 2f, content.width, content.height - Text.LineHeight - 2f), $"RimTalk.Settings.Preset.{preset}.Desc".Translate());
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawColumns(Listing_Standard listing, ContextSettings context)
        {
            const float columnGap = 16f;
            float columnWidth = (listing.ColumnWidth - columnGap * 2f) / 3f;
            Rect positionRect = listing.GetRect(0f);

            // Col 1 (Pawn Identity & Profile - 7 items)
            Rect col1Rect = new Rect(positionRect.x, positionRect.y, columnWidth, 9999f);
            Listing_Standard col1Listing = new Listing_Standard();
            col1Listing.Begin(col1Rect);

            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            col1Listing.Label($"━━ {"RimTalk.Settings.PawnInfo".Translate()} ━━");
            GUI.color = Color.white;
            col1Listing.Gap(4f);

            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeRace".Translate(), ref context.IncludeRace);
            col1Listing.Gap(2f);
            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeNotableGenes".Translate(), ref context.IncludeNotableGenes);
            col1Listing.Gap(2f);
            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeIdeology".Translate(), ref context.IncludeIdeology);
            col1Listing.Gap(2f);
            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeBackstory".Translate(), ref context.IncludeBackstory);
            col1Listing.Gap(2f);
            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeTraits".Translate(), ref context.IncludeTraits);
            col1Listing.Gap(2f);
            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeSkills".Translate(), ref context.IncludeSkills);
            col1Listing.Gap(2f);
            CheckboxLeft(col1Listing, "RimTalk.Settings.IncludeHealth".Translate(), ref context.IncludeHealth);

            col1Listing.End();

            // Col 2 (Mind & Social - 7 items)
            Rect col2Rect = new Rect(col1Rect.xMax + columnGap, positionRect.y, columnWidth, 9999f);
            Listing_Standard col2Listing = new Listing_Standard();
            col2Listing.Begin(col2Rect);

            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            col2Listing.Label($"━━ {"RimTalk.Settings.MindAndSocial".Translate()} ━━");
            GUI.color = Color.white;
            col2Listing.Gap(4f);

            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludeMood".Translate(), ref context.IncludeMood);
            col2Listing.Gap(2f);
            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludeThoughts".Translate(), ref context.IncludeThoughts);
            col2Listing.Gap(2f);
            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludeRelations".Translate(), ref context.IncludeRelations);
            col2Listing.Gap(2f);
            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludeEquipment".Translate(), ref context.IncludeEquipment);
            col2Listing.Gap(2f);
            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludePrisonerSlaveStatus".Translate(), ref context.IncludePrisonerSlaveStatus);
            col2Listing.Gap(2f);
            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludeBeauty".Translate(), ref context.IncludeBeauty);
            col2Listing.Gap(2f);
            CheckboxLeft(col2Listing, "RimTalk.Settings.IncludeCleanliness".Translate(), ref context.IncludeCleanliness);

            col2Listing.End();

            // Col 3 (Environment & World - 8 items)
            Rect col3Rect = new Rect(col2Rect.xMax + columnGap, positionRect.y, columnWidth, 9999f);
            Listing_Standard col3Listing = new Listing_Standard();
            col3Listing.Begin(col3Rect);

            Text.Font = GameFont.Small;
            GUI.color = Color.yellow;
            col3Listing.Label($"━━ {"RimTalk.Settings.Environment".Translate()} ━━");
            GUI.color = Color.white;
            col3Listing.Gap(4f);

            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeTime".Translate(), ref context.IncludeTime);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeDate".Translate(), ref context.IncludeDate);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeSeason".Translate(), ref context.IncludeSeason);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeWeather".Translate(), ref context.IncludeWeather);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeLocationAndTemperature".Translate(), ref context.IncludeLocationAndTemperature);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeTerrain".Translate(), ref context.IncludeTerrain);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeSurroundings".Translate(), ref context.IncludeSurroundings);
            col3Listing.Gap(2f);
            CheckboxLeft(col3Listing, "RimTalk.Settings.IncludeWealth".Translate(), ref context.IncludeWealth);

            col3Listing.End();

            float tallerColumnHeight = Mathf.Max(col1Listing.CurHeight, Mathf.Max(col2Listing.CurHeight, col3Listing.CurHeight));
            listing.Gap(tallerColumnHeight + 12f);
        }

        private void DrawCheckboxWithNumericInput(Listing_Standard listing, string labelKey, ref bool checkboxVal, ref int intVal, ref string buffer, int min, int max, Action onGearClicked = null, string gearTooltipKey = null)
        {
            const float textFieldWidth = 50f;
            const float checkSize = 24f;
            const float gap = 6f;
            const float gearSize = 22f;
            const float gearGap = 4f;

            Rect rowRect = listing.GetRect(24f);
            Widgets.DrawHighlightIfMouseover(rowRect);

            Vector2 checkPos = new Vector2(rowRect.x, rowRect.y);
            Widgets.CheckboxDraw(checkPos.x, checkPos.y, checkboxVal, false, checkSize);

            float rightReservedWidth = textFieldWidth + (onGearClicked != null ? (gearSize + gearGap) : 0f);

            float labelX = rowRect.x + checkSize + gap;
            float labelWidth = rowRect.width - (checkSize + gap) - rightReservedWidth - 6f;
            Rect labelRect = new Rect(labelX, rowRect.y, labelWidth, 24f);

            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, labelKey.Translate());
            Text.Anchor = oldAnchor;

            Rect clickableArea = new Rect(rowRect.x, rowRect.y, rowRect.width - rightReservedWidth - 6f, 24f);
            if (Widgets.ButtonInvisible(clickableArea))
            {
                checkboxVal = !checkboxVal;
                if (checkboxVal)
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                else
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }

            // Keep numeric input aligned with the numeric inputs above
            Rect fieldRect = new Rect(rowRect.xMax - textFieldWidth, rowRect.y, textFieldWidth, 24f);

            if (!checkboxVal)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.45f);
                GUI.enabled = false;
                Widgets.TextFieldNumeric(fieldRect, ref intVal, ref buffer, min, max);
                GUI.enabled = true;
                GUI.color = Color.white;
            }
            else
            {
                Widgets.TextFieldNumeric(fieldRect, ref intVal, ref buffer, min, max);
            }

            if (onGearClicked != null)
            {
                // Place gear icon to the left of numeric input
                Rect gearRect = new Rect(fieldRect.x - gearGap - gearSize, rowRect.y + 1f, gearSize, gearSize);
                var gearIcon = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
                if (Widgets.ButtonImage(gearRect, gearIcon, new Color(0.85f, 0.85f, 0.85f), GenUI.MouseoverColor))
                {
                    onGearClicked();
                }
                if (!string.IsNullOrEmpty(gearTooltipKey))
                {
                    TooltipHandler.TipRegion(gearRect, gearTooltipKey.Translate());
                }
            }

            var tip = (labelKey + ".Tooltip").Translate();
            TooltipHandler.TipRegion(clickableArea, tip);
            TooltipHandler.TipRegion(fieldRect, tip);
        }

        private void ApplyPreset(ContextSettings context, ContextPreset preset)
        {
            if (PresetDefinitions.TryGetValue(preset, out var source))
            {
                bool previousIncludeEvents = context.IncludeEvents;
                bool previousUseCompact = context.UseCompactHistory;
                CopyFields(source, context);
                context.IncludeEvents = previousIncludeEvents;
                if (previousUseCompact != context.UseCompactHistory)
                {
                    TalkHistory.Clear();
                }
                _currentPreset = preset;
                _maxPawnContextBuffer = context.MaxPawnContextCount.ToString();
                _conversationHistoryBuffer = context.ConversationHistoryCount.ToString();
                _maxEventsBuffer = context.MaxEventsCount.ToString();
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
                if (field.Name == nameof(ContextSettings.IncludeEvents)) continue;
                var valA = field.GetValue(a);
                var valB = field.GetValue(b);
                if (!Equals(valA, valB)) return false;
            }
            return true;
        }

        private void DrawNumericInput(Listing_Standard listing, string labelKey, ref int value, ref string buffer, int min, int max)
        {
            const float textFieldWidth = 50f;
            Rect rowRect = listing.GetRect(24f);
            Widgets.DrawHighlightIfMouseover(rowRect);
            Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - textFieldWidth - 8f, rowRect.height);
            Rect fieldRect = new Rect(rowRect.xMax - textFieldWidth, rowRect.y, textFieldWidth, rowRect.height);

            TextAnchor originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, labelKey.Translate());
            Text.Anchor = originalAnchor;

            TooltipHandler.TipRegion(rowRect, (labelKey + ".Tooltip").Translate());

            Widgets.TextFieldNumeric(fieldRect, ref value, ref buffer, min, max);
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