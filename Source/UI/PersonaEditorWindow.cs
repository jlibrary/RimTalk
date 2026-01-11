using RimTalk.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.UI;

public class PersonaEditorWindow : Window
{
    private const int MaxLength = 500; // Reasonable limit
    private readonly Pawn _pawn;
    private string _editingPersonality;
    private float _talkInitiationWeight;
    private bool _isGenerating = false;
    private Vector2 _scrollPos = Vector2.zero;
    private readonly string _textControlName = "RimTalk_Persona_TextArea";

    public PersonaEditorWindow(Pawn pawn)
    {
        _pawn = pawn;
        _editingPersonality = Data.PersonaService.GetPersonality(pawn) ?? "";
        _talkInitiationWeight = Data.PersonaService.GetTalkInitiationWeight(pawn);

        doCloseX = true;
        draggable = true;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new Vector2(520f, 440f);

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
        Widgets.Label(titleRect, "RimTalk.PersonaEditor.Title".Translate(_pawn.LabelShort));

        // Instruction text
        Text.Font = GameFont.Small;
        Rect instructRect = new Rect(inRect.x, titleRect.yMax + 5f, inRect.width, 40f);
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(instructRect, "RimTalk.PersonaEditor.Instruct".Translate());
        GUI.color = Color.white;
        
        // --- Scrollable multi-line text area ---
        Rect textBoxRect = new Rect(inRect.x, instructRect.yMax + 10f, inRect.width, 180f);

        float innerWidth = textBoxRect.width - 16f;

        float contentHeight = Mathf.Max(textBoxRect.height, Text.CalcHeight(
            string.IsNullOrEmpty(_editingPersonality) ? " " : _editingPersonality, innerWidth));
        
        Widgets.BeginScrollView(textBoxRect, ref _scrollPos, new Rect(0f, 0f, innerWidth, contentHeight));
        GUI.SetNextControlName(_textControlName);
        _editingPersonality = Widgets.TextArea(new Rect(0f, 0f, innerWidth, contentHeight), _editingPersonality);
        Widgets.EndScrollView();

        // Character count
        Rect countRect = new Rect(inRect.x, textBoxRect.yMax + 2f, inRect.width, 20f);
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
        Rect tendencyTitleRect = new Rect(inRect.x, countRect.yMax + 15f, inRect.width, 22f);
        string tendencyTitle = "RimTalk.PersonaEditor.Chattiness".Translate();
        Widgets.Label(tendencyTitleRect, tendencyTitle);

        // Add a question mark icon with a tooltip
        string tendencyDesc = "RimTalk.PersonaEditor.TalkFrequencyDesc".Translate();
        Vector2 titleSize = Text.CalcSize(tendencyTitle);
        float iconSize = 18f;
        Rect questionMarkRect = new Rect(tendencyTitleRect.x + titleSize.x + 5f, tendencyTitleRect.y + (tendencyTitleRect.height - iconSize) / 2f, iconSize, iconSize);
        TooltipHandler.TipRegion(questionMarkRect, tendencyDesc);
        GUI.DrawTexture(questionMarkRect, TexButton.Info);

        Rect sliderRowRect = new Rect(inRect.x, tendencyTitleRect.yMax + 5f, inRect.width, 22f);

        Rect listenerLabelRect = new Rect(sliderRowRect.x, sliderRowRect.y, 70f, sliderRowRect.height);
        Rect initiatorLabelRect;
        var originalAnchor = Text.Anchor;
        try
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(listenerLabelRect, "RimTalk.PersonaEditor.Quiet".Translate());

            initiatorLabelRect = new Rect(sliderRowRect.xMax - 70f, sliderRowRect.y, 70f, sliderRowRect.height);
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
        float buttonY = sliderRowRect.yMax + 15f;

        // Center the button group (4 buttons total)
        float totalWidth = (buttonWidth * 4f) + (spacing * 3f);
        float startX = inRect.center.x - (totalWidth / 2f);

        Rect saveButton = new Rect(startX, buttonY, buttonWidth, buttonHeight);
        Rect smartGenButton = new Rect(saveButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);
        Rect rollGenButton = new Rect(smartGenButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);
        Rect clearButton = new Rect(rollGenButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);

        if (Widgets.ButtonText(saveButton, "RimTalk.PersonaEditor.Save".Translate()))
        {
            Data.PersonaService.SetPersonality(_pawn, _editingPersonality.Trim());
            Data.PersonaService.SetTalkInitiationWeight(_pawn, _talkInitiationWeight);

            Messages.Message("RimTalk.PersonaEditor.Updated".Translate(_pawn.LabelShort), MessageTypeDefOf.TaskCompletion, false);
            Close();
        }

        if (Widgets.ButtonText(smartGenButton, _isGenerating ?
                "RimTalk.PersonaEditor.Generating".Translate().ToString() :
                "RimTalk.PersonaEditor.SmartGen".Translate().ToString()))
        {
            if (!_isGenerating)
            {
                _isGenerating = true;
                Data.PersonaService.GeneratePersona(_pawn).ContinueWith(task =>
                {
                    _isGenerating = false;
                    if (task.IsCompleted)
                    {
                        _editingPersonality = task.Result.Persona ?? "";
                        _talkInitiationWeight = task.Result.Chattiness;
                    }
                });
            }
        }

        if (Widgets.ButtonText(rollGenButton, "RimTalk.PersonaEditor.RollGen".Translate()))
        {
            PersonalityData rollGenData = Constant.Personalities.RandomElement();
            _editingPersonality = rollGenData.Persona;
            _talkInitiationWeight = rollGenData.Chattiness;
        }

        if (Widgets.ButtonText(clearButton, "RimTalk.PersonaEditor.Clear".Translate()))
        {
            _editingPersonality = "";
        }
    }

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        if (_isGenerating)
        {
            // This will cause the window to repaint continuously while generating
        }
    }
}