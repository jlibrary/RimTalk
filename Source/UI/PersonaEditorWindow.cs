using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Memory;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.UI;

public class PersonaEditorWindow : Window
{
    private enum EditorTab { Personality, Memories }
    private EditorTab _currentTab = EditorTab.Personality;

    public bool IsPersonalityTabSelected => _currentTab == EditorTab.Personality;

    private enum MemorySortColumn { None, Target, Time, Weight, Note }
    private MemorySortColumn _sortColumn = MemorySortColumn.Time;
    private bool _sortAscending = false;
    private bool _sortDirty = true;
    private readonly List<MemoryEntry> _displayedMemories = new();

    private const int MaxLength = 500; // Reasonable limit
    private Pawn _pawn;
    private string _editingPersonality;
    private float _talkInitiationWeight;
    private bool _isGenerating = false;
    private bool _pawnChanged = false;
    private Vector2 _scrollPos = Vector2.zero;
    private Vector2 _memoryScrollPos = Vector2.zero;
    private readonly string _textControlName = "RimTalk_Persona_TextArea";
    private List<TabRecord> _tabs;

    private static readonly Color SectionBorderColor = new(70f / 255f, 70f / 255f, 70f / 255f);
    private static readonly Color TabActiveBgColor = new(33f / 255f, 33f / 255f, 33f / 255f);
    private static readonly Color TabInactiveBgColor = new(22f / 255f, 22f / 255f, 22f / 255f);

    private static readonly Color CardBgColor = new(26f / 255f, 28f / 255f, 32f / 255f, 0.85f);
    private static readonly Color CardBorderColor = new(65f / 255f, 70f / 255f, 78f / 255f, 0.8f);
    private static readonly Color SoftGreen = new(0.6f, 0.9f, 0.6f);
    private static readonly Color DirectiveBorderColor = new(45f / 255f, 95f / 255f, 50f / 255f, 0.8f);
    private static readonly Color TraumaBorderColor = new(95f / 255f, 45f / 255f, 50f / 255f, 0.8f);

    private static readonly Color SoftRose = new(0.92f, 0.50f, 0.50f);
    private static readonly Color SoftAmber = new(0.92f, 0.80f, 0.45f);
    private static readonly Color MutedGray = new(0.65f, 0.68f, 0.72f);

    private List<TabRecord> Tabs => _tabs ??= new List<TabRecord>
    {
        new TabRecord("RimTalk.PersonaEditor.TabPersonality".Translate(), () => _currentTab = EditorTab.Personality, () => _currentTab == EditorTab.Personality),
        new TabRecord("RimTalk.PersonaEditor.TabMemories".Translate(), () => _currentTab = EditorTab.Memories, () => _currentTab == EditorTab.Memories)
    };

    public PersonaEditorWindow(Pawn pawn)
    {
        SetTargetPawn(pawn);

        doCloseX = true;
        draggable = true;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    private void SetTargetPawn(Pawn pawn)
    {
        _pawn = pawn;
        _editingPersonality = PersonaService.GetPersonality(pawn) ?? "";
        _talkInitiationWeight = PersonaService.GetTalkInitiationWeight(pawn);
        _scrollPos = Vector2.zero;
        _memoryScrollPos = Vector2.zero;
        _pawnChanged = true;
        _sortColumn = MemorySortColumn.Time;
        _sortAscending = false;
        _sortDirty = true;
    }

    private static bool IsValidTarget(Pawn pawn)
    {
        return pawn != null && !pawn.Dead && (pawn.IsColonist || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony || pawn.HasVocalLink());
    }

    public override Vector2 InitialSize => new Vector2(580f, 460f);

    public override void DoWindowContents(Rect inRect)
    {
        if (_pawnChanged)
        {
            _pawnChanged = false;
            GUI.FocusControl(null);
        }

        Text.Font = GameFont.Medium;
        Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
        Widgets.Label(titleRect, "RimTalk.PersonaEditor.Title".Translate(_pawn.LabelShort));
        Text.Font = GameFont.Small;

        float tabTopMargin = 40f;
        Rect contentRect = new Rect(inRect.x, titleRect.yMax + tabTopMargin, inRect.width, inRect.yMax - (titleRect.yMax + tabTopMargin));

        DrawTabButtons(contentRect);

        try
        {
            if (_currentTab == EditorTab.Personality)
            {
                DrawPersonalityTab(contentRect);
            }
            else
            {
                DrawMemoriesTab(contentRect);
            }
        }
        finally
        {
            Text.WordWrap = true;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }

    private static void DrawFlatCard(Rect rect, Color? bgColor = null, Color? borderColor = null)
    {
        Widgets.DrawBoxSolid(rect, bgColor ?? CardBgColor);
        GUI.color = borderColor ?? CardBorderColor;
        Widgets.DrawBox(rect, 1);
        GUI.color = Color.white;
    }

    private static bool DrawFlatDeleteButton(Rect rect, string tooltip = null)
    {
        if (!GUI.enabled) return false;

        bool mouseOver = Mouse.IsOver(rect);
        if (mouseOver)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.6f, 0.15f, 0.15f, 0.6f));
            GUI.color = new Color(1f, 0.45f, 0.45f);
        }
        else
        {
            GUI.color = new Color(0.50f, 0.53f, 0.58f, 0.7f);
        }

        TextAnchor prevAnchor = Text.Anchor;
        GameFont prevFont = Text.Font;
        Text.Anchor = TextAnchor.MiddleCenter;
        Text.Font = GameFont.Small;
        Widgets.Label(rect, "×");
        Text.Anchor = prevAnchor;
        Text.Font = prevFont;
        GUI.color = Color.white;

        if (!string.IsNullOrEmpty(tooltip))
        {
            TooltipHandler.TipRegion(rect, tooltip);
        }

        if (Widgets.ButtonInvisible(rect))
        {
            SoundDefOf.Click.PlayOneShotOnCamera(null);
            return true;
        }

        return false;
    }

    private void DrawTabButtons(Rect tabBaseRect)
    {
        const float maxTabWidth = 140f;
        float tabWidth = Mathf.Floor(Mathf.Min(maxTabWidth, tabBaseRect.width / Tabs.Count));
        float tabHeight = 32f;
        float curX = tabBaseRect.x;

        TextAnchor prevAnchor = Text.Anchor;
        GameFont prevFont = Text.Font;
        Color prevColor = GUI.color;

        Text.Anchor = TextAnchor.MiddleCenter;
        Text.Font = GameFont.Small;

        bool isMemoryEnabled = Settings.Get()?.Context?.EnableMemory ?? true;

        for (int i = 0; i < Tabs.Count; i++)
        {
            TabRecord tab = Tabs[i];
            bool isSelected = tab.Selected;
            bool isMemoriesTab = (i == 1);
            float h = isSelected ? tabHeight : tabHeight - 3f;
            Rect tabRect = new Rect(curX, tabBaseRect.y - h, tabWidth, isSelected ? h + 2f : h);

            Widgets.DrawBoxSolid(tabRect, isSelected ? TabActiveBgColor : TabInactiveBgColor);
            GUI.color = SectionBorderColor;
            Widgets.DrawLineHorizontal(tabRect.x, tabRect.y, tabWidth);
            Widgets.DrawLineVertical(tabRect.x, tabRect.y, (i == 0 && isSelected) ? h + 2f : h);
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

            if (isMemoriesTab && !isMemoryEnabled)
            {
                GUI.color = isSelected ? Color.white : new Color(0.55f, 0.55f, 0.55f);
                TooltipHandler.TipRegion(tabRect, "RimTalk.Memory.DisabledWarning".Translate());
            }
            else
            {
                GUI.color = isSelected ? Color.white : new Color(0.70f, 0.70f, 0.70f);
            }

            Widgets.Label(new Rect(tabRect.x, tabBaseRect.y - h, tabWidth, h), tab.label);
            curX += tabWidth;
        }

        Text.Anchor = prevAnchor;
        Text.Font = prevFont;
        GUI.color = SectionBorderColor;
        if (curX < tabBaseRect.xMax) Widgets.DrawLineHorizontal(curX, tabBaseRect.y, tabBaseRect.xMax - curX);
        GUI.color = prevColor;
    }

    private void DrawPersonalityTab(Rect inRect)
    {
        // Instruction text
        Text.Font = GameFont.Small;
        const float contentTopPadding = 8f;
        string instructText = "RimTalk.PersonaEditor.Instruct".Translate();
        float instructHeight = Text.CalcHeight(instructText, inRect.width);
        Rect instructRect = new Rect(inRect.x, inRect.y + contentTopPadding, inRect.width, instructHeight);
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(instructRect, instructText);
        GUI.color = Color.white;
        
        // --- Scrollable multi-line text area ---
        Rect textBoxRect = new Rect(inRect.x, instructRect.yMax + 4f, inRect.width, 160f);

        float innerWidth = textBoxRect.width - 16f;

        float contentHeight = Mathf.Max(textBoxRect.height, Text.CalcHeight(
            string.IsNullOrEmpty(_editingPersonality) ? " " : _editingPersonality, innerWidth));
        
        Widgets.BeginScrollView(textBoxRect, ref _scrollPos, new Rect(0f, 0f, innerWidth, contentHeight));
        GUI.SetNextControlName(_textControlName);
        _editingPersonality = Widgets.TextArea(new Rect(0f, 0f, innerWidth, contentHeight), _editingPersonality);
        Widgets.EndScrollView();

        // Character count
        Rect countRect = new Rect(inRect.x, textBoxRect.yMax + 2f, inRect.width, 18f);
        Text.Font = GameFont.Tiny;
        Color countColor = _editingPersonality.Length > 300 ? Color.yellow : Color.gray;
        if (_editingPersonality.Length >= MaxLength) countColor = Color.red;
        GUI.color = countColor;
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(countRect, "RimTalk.PersonaEditor.Characters".Translate(_editingPersonality.Length, 300));
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // --- Chattiness Section ---
        Rect tendencyTitleRect = new Rect(inRect.x, countRect.yMax + 8f, inRect.width, 22f);
        string tendencyTitle = "RimTalk.PersonaEditor.Chattiness".Translate();
        Widgets.Label(tendencyTitleRect, tendencyTitle);

        // Add a question mark icon with a tooltip
        string tendencyDesc = "RimTalk.PersonaEditor.TalkFrequencyDesc".Translate();
        Vector2 titleSize = Text.CalcSize(tendencyTitle);
        float iconSize = 18f;
        Rect questionMarkRect = new Rect(tendencyTitleRect.x + titleSize.x + 5f, tendencyTitleRect.y + (tendencyTitleRect.height - iconSize) / 2f, iconSize, iconSize);
        TooltipHandler.TipRegion(questionMarkRect, tendencyDesc);
        GUI.DrawTexture(questionMarkRect, TexButton.Info);

        Rect sliderRowRect = new Rect(inRect.x, tendencyTitleRect.yMax + 4f, inRect.width, 22f);

        Rect listenerLabelRect = new Rect(sliderRowRect.x, sliderRowRect.y, 70f, sliderRowRect.height);
        Rect initiatorLabelRect;
        var originalAnchor = Text.Anchor;
        try
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(listenerLabelRect, "RimTalk.PersonaEditor.Quiet".Translate());

            initiatorLabelRect = new Rect(sliderRowRect.xMax - 80f, sliderRowRect.y, 80f, sliderRowRect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(initiatorLabelRect, "RimTalk.PersonaEditor.Chatty".Translate());
        }
        finally
        {
            Text.Anchor = originalAnchor;
        }

        float sliderMargin = 5f;
        float sliderX = listenerLabelRect.xMax + sliderMargin;
        float sliderWidth = (initiatorLabelRect.x - sliderMargin) - sliderX;

        // Slider itself (leave room for value display)
        Rect frequencySliderRect = new Rect(sliderX, sliderRowRect.y, sliderWidth - 40f, sliderRowRect.height);
        _talkInitiationWeight = Widgets.HorizontalSlider(frequencySliderRect, _talkInitiationWeight, 0f, 1.0f, true);

        // Value label (numeric display)
        Rect valueLabelRect = new Rect(frequencySliderRect.xMax + 5f, sliderRowRect.y, 40f, sliderRowRect.height);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(valueLabelRect, _talkInitiationWeight.ToString("0.00"));
        Text.Anchor = TextAnchor.UpperLeft;

        // Buttons
        float buttonWidth = 90f;
        float buttonHeight = 28f;
        float spacing = 10f;
        float buttonY = sliderRowRect.yMax + 14f;

        // Center the button group (4 buttons total)
        float totalWidth = (buttonWidth * 4f) + (spacing * 3f);
        float startX = inRect.center.x - (totalWidth / 2f);

        Rect saveButton = new Rect(startX, buttonY, buttonWidth, buttonHeight);
        Rect smartGenButton = new Rect(saveButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);
        Rect rollGenButton = new Rect(smartGenButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);
        Rect clearButton = new Rect(rollGenButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);

        if (UIUtil.ButtonText(saveButton, "RimTalk.PersonaEditor.Save".Translate()))
        {
            PersonaService.SetPersonality(_pawn, _editingPersonality.Trim());
            PersonaService.SetTalkInitiationWeight(_pawn, _talkInitiationWeight);

            Messages.Message("RimTalk.PersonaEditor.Updated".Translate(_pawn.LabelShort), MessageTypeDefOf.TaskCompletion, false);
            Close();
        }

        if (UIUtil.ButtonText(smartGenButton, _isGenerating ?
                "RimTalk.PersonaEditor.Generating".Translate().ToString() :
                "RimTalk.PersonaEditor.SmartGen".Translate().ToString()))
        {
            if (!_isGenerating)
            {
                _isGenerating = true;
                PersonaService.GeneratePersona(_pawn).ContinueWith(task =>
                {
                    _isGenerating = false;

                    var result = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                    if (result == null)
                    {
                        Logger.Warning("Persona generation failed - see the API log.");
                        return;
                    }

                    _editingPersonality = result.Persona ?? "";
                    _talkInitiationWeight = result.Chattiness;
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        if (UIUtil.ButtonText(rollGenButton, "RimTalk.PersonaEditor.RollGen".Translate()))
        {
            PersonalityData rollGenData = Constant.Personalities.RandomElement();
            _editingPersonality = rollGenData.Persona;
            _talkInitiationWeight = rollGenData.Chattiness;
        }

        if (UIUtil.ButtonText(clearButton, "RimTalk.PersonaEditor.Clear".Translate()))
        {
            _editingPersonality = "";
        }
    }

    private void DrawMemoriesTab(Rect inRect)
    {
        Text.Font = GameFont.Small;
        var hediff = Hediff_Persona.GetOrAddNew(_pawn);
        var memories = hediff?.Memories;
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        bool isMemoryEnabled = Settings.Get()?.Context?.EnableMemory ?? false;
        bool prevEnabled = GUI.enabled;

        if (!isMemoryEnabled)
        {
            GUI.enabled = false;
        }

        try
        {
            // Top Controls: Colony Seniority on left, Clear All button on right
            const float contentTopPadding = 8f;
            Rect topBar = new Rect(inRect.x, inRect.y + contentTopPadding, inRect.width, 26f);

            // Colony Seniority
            int seniorityYears = PawnMemoryTracker.CalculateColonyYears(_pawn?.records?.GetValue(RimWorld.RecordDefOf.TimeAsColonistOrColonyAnimal) ?? 0f);
            Rect seniorityRect = new Rect(topBar.x + 2f, topBar.y, 200f, 26f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.9f, 0.85f, 0.5f);
            Widgets.Label(seniorityRect, "RimTalk.Memory.ColonySeniority".Translate(seniorityYears));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect btnClear = new Rect(topBar.xMax - 100f, topBar.y, 100f, 26f);
            Rect btnHelp = new Rect(btnClear.x - 28f, topBar.y + (topBar.height - 20f) / 2f, 20f, 20f);
            if (Widgets.ButtonImage(btnHelp, TexButton.Info))
            {
                Find.WindowStack.Add(new Dialog_MessageBox("RimTalk.Memory.HelpDesc".Translate(), "OK".Translate(), title: "RimTalk.Memory.HelpTitle".Translate()));
            }

            if (Widgets.ButtonText(btnClear, "RimTalk.Memory.ClearAll".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "RimTalk.Memory.ClearAllConfirm".Translate(_pawn.LabelShort),
                    () =>
                    {
                        memories?.Clear();
                        _sortDirty = true;
                        MemoryHistory.Add(new MemoryLogEntry
                        {
                            SourcePawnName = _pawn?.LabelShort ?? string.Empty,
                            SourcePawnId = _pawn?.thingIDNumber ?? -1,
                            ChangeType = MemoryChangeType.Cleared,
                            Tick = currentTick,
                            Details = "All memories cleared via Persona Editor"
                        });
                        Messages.Message("RimTalk.Memory.Cleared".Translate(_pawn.LabelShort), MessageTypeDefOf.TaskCompletion, false);
                    },
                    destructive: true));
            }

            // Active Directives Box with Dynamic Multiline Word-Wrapping
            var directives = PawnMemoryTracker.SelectActiveDirectives(memories, currentTick);
            float dirTextWidth = inRect.width - 50f;
            Text.Font = GameFont.Tiny;
            float[] dirHeights = new float[directives.Count];
            float totalDirContentHeight = 0f;
            for (int d = 0; d < directives.Count; d++)
            {
                float h = Mathf.Max(20f, Text.CalcHeight($"- {directives[d].Note}", dirTextWidth) + 4f);
                dirHeights[d] = h;
                totalDirContentHeight += h;
            }
            Text.Font = GameFont.Small;

            float directiveHeight = directives.Count > 0 ? 24f + totalDirContentHeight + 6f : 30f;
            Rect directiveRect = new Rect(inRect.x, topBar.yMax + 6f, inRect.width, directiveHeight);
            DrawFlatCard(directiveRect, borderColor: directives.Count > 0 ? DirectiveBorderColor : CardBorderColor);

            if (directives.Count == 0)
            {
                GUI.color = MutedGray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(directiveRect.x + 8f, directiveRect.y + 6f, directiveRect.width - 16f, 20f), "RimTalk.Memory.ActiveOrdersNone".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            else
            {
                Rect titleRect = new Rect(directiveRect.x + 8f, directiveRect.y + 3f, directiveRect.width - 16f, 18f);
                GUI.color = SoftGreen;
                Text.Font = GameFont.Tiny;
                Widgets.Label(titleRect, "RimTalk.Memory.ActiveOrdersHeader".Translate());
                GUI.color = Color.white;

                float curDirY = directiveRect.y + 22f;
                for (int d = 0; d < directives.Count; d++)
                {
                    var dir = directives[d];
                    float rowH = dirHeights[d];
                    Rect rowRect = new Rect(directiveRect.x + 8f, curDirY, dirTextWidth, rowH);
                    GUI.color = new Color(0.88f, 0.94f, 0.88f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(rowRect, $"- {dir.Note}");
                    GUI.color = Color.white;

                Rect btnDeleteDir = new Rect(directiveRect.xMax - 24f, curDirY + (rowH - 18f) / 2f, 18f, 18f);
                if (DrawFlatDeleteButton(btnDeleteDir))
                {
                    MemoryHistory.Add(new MemoryLogEntry
                    {
                        SourcePawnName = _pawn?.LabelShort ?? string.Empty,
                        SourcePawnId = _pawn?.thingIDNumber ?? -1,
                        TargetPawnName = "Player",
                        TargetPawnId = -999,
                        ChangeType = MemoryChangeType.DirectiveRemoved,
                        EventKey = dir.EventKey,
                        Note = dir.Note,
                        OldWeight = dir.BaseWeight,
                        NewWeight = 0f,
                        IsDirective = true,
                        Tick = currentTick,
                        Details = "Directive manually removed via Persona Editor"
                    });
                    memories?.Remove(dir);
                    Text.Font = GameFont.Small;
                    break;
                }
                curDirY += rowH;
            }
            Text.Font = GameFont.Small;
        }

        // Core Trauma Box with Dynamic Height and Safe Padding
        var traumas = PawnMemoryTracker.SelectCoreTraumas(memories, currentTick);
        string plainText;
        string richText;
        if (traumas.Count > 0)
        {
            string prefix = "RimTalk.Memory.CoreTraumaPrefix".Translate();
            var sbPlain = new StringBuilder(prefix);
            var sbRich = new StringBuilder(prefix.Colorize(SoftRose));

            var contentColor = new Color(0.85f, 0.88f, 0.92f);
            for (int i = 0; i < traumas.Count; i++)
            {
                if (i > 0)
                {
                    sbPlain.Append("; ");
                    sbRich.Append("; ");
                }
                sbPlain.Append(traumas[i].Note);
                sbRich.Append(traumas[i].Note.Colorize(contentColor));
            }
            plainText = sbPlain.ToString();
            richText = sbRich.ToString();
        }
        else
        {
            plainText = "RimTalk.Memory.CoreTraumaNone".Translate();
            richText = plainText.Colorize(MutedGray);
        }

        float traumaTextWidth = inRect.width - 20f;
        Text.Font = GameFont.Tiny;
        float traumaTextHeight = Text.CalcHeight(plainText, traumaTextWidth);
        float traumaHeight = Mathf.Max(32f, traumaTextHeight + 12f);
        Text.Font = GameFont.Small;

        Rect traumaRect = new Rect(inRect.x, directiveRect.yMax + 4f, inRect.width, traumaHeight);
        DrawFlatCard(traumaRect, borderColor: traumas.Count > 0 ? TraumaBorderColor : CardBorderColor);
        Rect traumaLabelRect = new Rect(traumaRect.x + 8f, traumaRect.y + (traumaHeight - traumaTextHeight) / 2f, traumaTextWidth, traumaTextHeight);
        Text.Font = GameFont.Tiny;
        Widgets.Label(traumaLabelRect, richText);
        Text.Font = GameFont.Small;

        // Unified Memory Table Frame
        Rect tableFrameRect = new Rect(inRect.x, traumaRect.yMax + 6f, inRect.width, inRect.yMax - traumaRect.yMax - 6f);
        Widgets.DrawBoxSolid(tableFrameRect, new Color(0.10f, 0.11f, 0.13f, 0.6f));
        GUI.color = CardBorderColor;
        Widgets.DrawBox(tableFrameRect, 1);
        GUI.color = Color.white;

        // Integrated Header Bar
        Rect listHeaderRect = new Rect(tableFrameRect.x, tableFrameRect.y, tableFrameRect.width, 24f);
        Widgets.DrawBoxSolid(listHeaderRect, new Color(0.16f, 0.18f, 0.22f, 0.95f));
        GUI.color = CardBorderColor;
        Widgets.DrawLineHorizontal(tableFrameRect.x, listHeaderRect.yMax, tableFrameRect.width);
        GUI.color = Color.white;

        Text.Font = GameFont.Tiny;
        Rect targetHeaderRect = new Rect(listHeaderRect.x + 8f, listHeaderRect.y, 75f, 24f);
        Rect timeHeaderRect = new Rect(listHeaderRect.x + 89f, listHeaderRect.y, 60f, 24f);
        Rect weightHeaderRect = new Rect(listHeaderRect.x + 155f, listHeaderRect.y, 58f, 24f);
        Rect noteHeaderRect = new Rect(listHeaderRect.x + 220f, listHeaderRect.y, listHeaderRect.width - 245f, 24f);

        DrawSortableHeader(targetHeaderRect, "RimTalk.Memory.TableTarget".Translate(), MemorySortColumn.Target);
        DrawSortableHeader(timeHeaderRect, "RimTalk.Memory.TableTime".Translate(), MemorySortColumn.Time);
        DrawSortableHeader(weightHeaderRect, "RimTalk.Memory.TableWeight".Translate(), MemorySortColumn.Weight);
        DrawSortableHeader(noteHeaderRect, "RimTalk.Memory.TableNote".Translate(), MemorySortColumn.Note);

        Rect scrollOuter = new Rect(tableFrameRect.x + 1f, listHeaderRect.yMax + 1f, tableFrameRect.width - 2f, tableFrameRect.height - 26f);

        int validCount = 0;
        if (memories != null)
        {
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i] != null && !memories[i].IsDirective)
                    validCount++;
            }
        }

        if (_sortDirty || _displayedMemories.Count != validCount)
        {
            RefreshDisplayedMemories(memories, currentTick);
        }

        float rowHeight = 24f;
        float totalContentHeight = _displayedMemories.Count * rowHeight;
        bool hasScrollbar = totalContentHeight > scrollOuter.height;
        float contentWidth = hasScrollbar ? (scrollOuter.width - 16f) : scrollOuter.width;
        float viewHeight = Mathf.Max(scrollOuter.height, totalContentHeight);

        Widgets.BeginScrollView(scrollOuter, ref _memoryScrollPos, new Rect(0f, 0f, contentWidth, viewHeight));
        if (_displayedMemories.Count > 0)
        {
            bool prevWrap = Text.WordWrap;
            Text.WordWrap = false;
            Text.Font = GameFont.Tiny;
            try
            {
                float curY = 0f;
                float rightMargin = hasScrollbar ? 22f : 24f;
                for (int i = 0; i < _displayedMemories.Count; i++)
                {
                    var m = _displayedMemories[i];
                    if (m == null) continue;
                    Rect row = new Rect(0f, curY, contentWidth, rowHeight);
                    if (i % 2 == 1)
                    {
                        Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.035f));
                    }
                    if (GUI.enabled) Widgets.DrawHighlightIfMouseover(row);

                    float decayed = m.GetDecayedWeight(currentTick);

                    string targetLabel = !string.IsNullOrEmpty(m.TargetPawnName)
                        ? m.TargetPawnName
                        : (m.TargetPawnId == -999 ? "RimTalk.Memory.TargetPlayer".Translate() : "RimTalk.Memory.TargetSelfAll".Translate());
                    Widgets.Label(new Rect(row.x + 7f, row.y + 2f, 75f, rowHeight), targetLabel);

                    Rect timeRect = new Rect(row.x + 88f, row.y + 2f, 60f, rowHeight);
                    string timeLabel = GetRelativeTimeString(m.CreatedTick, currentTick);
                    GUI.color = MutedGray;
                    Widgets.Label(timeRect, timeLabel);
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(timeRect, GetTimeTooltip(m.CreatedTick, currentTick));

                    Rect weightRect = new Rect(row.x + 154f, row.y + 2f, 58f, rowHeight);
                    if (m.IsMilestone)
                    {
                        GUI.color = SoftAmber;
                        Widgets.Label(weightRect, $"★ {m.BaseWeight:F0}");
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(weightRect, "RimTalk.Memory.WeightMilestoneTip".Translate(m.BaseWeight.ToString("F0")));
                    }
                    else
                    {
                        Color weightColor = decayed > 5f ? SoftGreen : (decayed < -5f ? SoftRose : MutedGray);
                        GUI.color = weightColor;
                        Widgets.Label(weightRect, $"{decayed:F0}");
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(weightRect, "RimTalk.Memory.WeightTip".Translate(decayed.ToString("F0"), m.BaseWeight.ToString("F0")));
                    }

                    Rect btnDelete = new Rect(row.xMax - rightMargin, row.y + 3f, 18f, 18f);
                    Rect noteRect = new Rect(row.x + 219f, row.y + 2f, Mathf.Max(50f, (btnDelete.x - 6f) - (row.x + 219f)), rowHeight);
                    string cleanNote = PawnMemoryTracker.StripBoilerplate(m.Note);
                    if (m.IsMilestone)
                    {
                        GUI.color = SoftAmber;
                        Widgets.Label(noteRect, $"★ {cleanNote}");
                        GUI.color = Color.white;
                    }
                    else
                    {
                        Widgets.Label(noteRect, cleanNote);
                    }
                    TooltipHandler.TipRegion(noteRect, m.IsMilestone ? $"[★ Milestone] {cleanNote} (Permanent - does not decay)" : cleanNote);

                    if (DrawFlatDeleteButton(btnDelete))
                    {
                        memories?.Remove(m);
                        _sortDirty = true;
                        break;
                    }

                    curY += rowHeight;
                }
            }
            finally
            {
                Text.WordWrap = prevWrap;
                Text.Font = GameFont.Small;
            }
        }
        else
        {
            GUI.color = MutedGray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(8f, 10f, scrollOuter.width - 16f, 30f), "RimTalk.Memory.NoMemories".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
        Widgets.EndScrollView();
    }
    finally
    {
        GUI.enabled = prevEnabled;
    }

    if (!isMemoryEnabled)
    {
        // 1. Translucent dark veil over the entire tab content to grey it out cleanly
        Widgets.DrawBoxSolid(inRect, new Color(0.04f, 0.05f, 0.07f, 0.60f));

        // 2. Centered warning card
        string warningMsg = "RimTalk.Memory.DisabledWarning".Translate();
        float cardWidth = Mathf.Min(460f, inRect.width - 30f);
        float textWidth = cardWidth - 28f;
        Text.Font = GameFont.Tiny;
        float textHeight = Text.CalcHeight(warningMsg, textWidth);
        float cardHeight = textHeight + 22f;
        Rect cardRect = new Rect(
            inRect.x + (inRect.width - cardWidth) / 2f,
            inRect.y + (inRect.height - cardHeight) / 2f,
            cardWidth,
            cardHeight);

        DrawFlatCard(cardRect, new Color(0.12f, 0.13f, 0.16f, 0.96f), new Color(0.85f, 0.68f, 0.28f, 0.9f));
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = new Color(0.95f, 0.85f, 0.50f);
        Widgets.Label(new Rect(cardRect.x + 14f, cardRect.y + (cardHeight - textHeight) / 2f, textWidth, textHeight), warningMsg);
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;

        if (Event.current.type == EventType.MouseDown && cardRect.Contains(Event.current.mousePosition))
        {
            Event.current.Use();
        }
    }
    }

    private void DrawSortableHeader(Rect rect, string label, MemorySortColumn column)
    {
        if (GUI.enabled) Widgets.DrawHighlightIfMouseover(rect);

        bool isActive = _sortColumn == column;
        string displayLabel = isActive ? $"{label} {(_sortAscending ? "▲" : "▼")}" : label;

        GUI.color = isActive ? Color.white : new Color(0.85f, 0.88f, 0.92f);
        Widgets.Label(new Rect(rect.x, rect.y + 3f, rect.width, 18f), displayLabel);
        GUI.color = Color.white;

        if (GUI.enabled && Widgets.ButtonInvisible(rect))
        {
            if (_sortColumn == column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = column;
                _sortAscending = (column == MemorySortColumn.Target || column == MemorySortColumn.Note);
            }
            _sortDirty = true;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }
    }

    private void RefreshDisplayedMemories(List<MemoryEntry> memories, int currentTick)
    {
        _displayedMemories.Clear();
        if (memories != null)
        {
            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                if (m != null && !m.IsDirective)
                    _displayedMemories.Add(m);
            }
        }

        _displayedMemories.Sort((a, b) =>
        {
            // 1. Permanent milestones are always pinned to the top
            if (a.IsMilestone != b.IsMilestone)
                return a.IsMilestone ? -1 : 1;

            // 2. Column-based sorting
            int result = 0;
            switch (_sortColumn)
            {
                case MemorySortColumn.Target:
                    string targetA = !string.IsNullOrEmpty(a.TargetPawnName)
                        ? a.TargetPawnName
                        : (a.TargetPawnId == -999 ? "RimTalk.Memory.TargetPlayer".Translate().Resolve() : "RimTalk.Memory.TargetSelfAll".Translate().Resolve());
                    string targetB = !string.IsNullOrEmpty(b.TargetPawnName)
                        ? b.TargetPawnName
                        : (b.TargetPawnId == -999 ? "RimTalk.Memory.TargetPlayer".Translate().Resolve() : "RimTalk.Memory.TargetSelfAll".Translate().Resolve());
                    result = string.Compare(targetA, targetB, System.StringComparison.CurrentCultureIgnoreCase);
                    break;

                case MemorySortColumn.Time:
                    result = a.CreatedTick.CompareTo(b.CreatedTick);
                    break;

                case MemorySortColumn.Weight:
                    float weightA = a.IsMilestone ? a.BaseWeight : a.GetDecayedWeight(currentTick);
                    float weightB = b.IsMilestone ? b.BaseWeight : b.GetDecayedWeight(currentTick);
                    result = weightA.CompareTo(weightB);
                    break;

                case MemorySortColumn.Note:
                    string noteA = PawnMemoryTracker.StripBoilerplate(a.Note);
                    string noteB = PawnMemoryTracker.StripBoilerplate(b.Note);
                    result = string.Compare(noteA, noteB, System.StringComparison.CurrentCultureIgnoreCase);
                    break;

                default:
                    result = a.CreatedTick.CompareTo(b.CreatedTick);
                    break;
            }

            if (result != 0)
                return _sortAscending ? result : -result;

            return b.CreatedTick.CompareTo(a.CreatedTick);
        });

        _sortDirty = false;
    }

    private static string GetRelativeTimeString(int createdTick, int currentTick)
    {
        int diff = Mathf.Max(0, currentTick - createdTick);
        if (diff < 2500)
        {
            return "RimTalk.Memory.TimeJustNow".Translate();
        }
        return diff.ToStringTicksToPeriod(allowSeconds: false, shortForm: true);
    }

    private string GetTimeTooltip(int createdTick, int currentTick)
    {
        int diff = Mathf.Max(0, currentTick - createdTick);
        string period = diff < 2500 ? "RimTalk.Memory.TimeJustNow".Translate().Resolve() : diff.ToStringTicksToPeriod(allowSeconds: false, shortForm: false);
        Vector2 longLat = _pawn?.MapHeld != null ? Find.WorldGrid.LongLatOf(_pawn.MapHeld.Tile) : Vector2.zero;
        string fullDate = GenDate.DateFullStringAt(createdTick, longLat);
        return "RimTalk.Memory.TimeTip".Translate(fullDate, period);
    }

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        if (_isGenerating)
        {
            // This will cause the window to repaint continuously while generating
            return;
        }

        if (Find.Selector?.SingleSelectedThing is Pawn newPawn && newPawn != _pawn && IsValidTarget(newPawn))
        {
            SetTargetPawn(newPawn);
        }
    }
}