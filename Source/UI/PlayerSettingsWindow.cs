using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.UI;

public class PlayerSettingsWindow : Window
{
    private const int MaxLength = 500;
    private readonly RimTalkSettings _settings;
    private Settings.PlayerDialogueMode _editingMode;
    private string _editingName;
    private string _editingPersona;
    private Vector2 _scrollPos = Vector2.zero;
    private const string TextControlName = "RimTalk_PlayerPersona_TextArea";

    public PlayerSettingsWindow()
    {
        _settings = Settings.Get();
        _editingMode = _settings.PlayerDialogueMode;
        _editingName = _settings.PlayerName ?? "Player";
        _editingPersona = _settings.PlayerPersona ?? "";

        doCloseX = true;
        draggable = true;
        closeOnAccept = false;
        closeOnCancel = true;
        absorbInputAroundWindow = true;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new Vector2(580f, 490f);

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
        Widgets.Label(titleRect, "RimTalk.PlayerSettings.Title".Translate());

        // Instruction text
        Text.Font = GameFont.Small;
        Rect instructRect = new Rect(inRect.x, titleRect.yMax + 2f, inRect.width, 22f);
        GUI.color = new Color(0.8f, 0.8f, 0.8f);
        Widgets.Label(instructRect, "RimTalk.PlayerSettings.Instruct".Translate());
        GUI.color = Color.white;

        float curY = instructRect.yMax + 8f;

        // 1. Dialogue Mode Dropdown
        const float labelWidth = 140f;
        const float fieldWidth = 200f;
        Rect modeRowRect = new Rect(inRect.x, curY, inRect.width, 28f);
        Rect modeLabelRect = new Rect(modeRowRect.x, modeRowRect.y, labelWidth, modeRowRect.height);
        Rect modeDropdownRect = new Rect(modeLabelRect.xMax + 10f, modeRowRect.y, fieldWidth, modeRowRect.height);

        TextAnchor originalAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(modeLabelRect, "RimTalk.Settings.PlayerToNpc".Translate());
        Text.Anchor = originalAnchor;

        string currentModeLabel = GetPlayerDialogueModeLabel(_editingMode);
        if (Widgets.ButtonText(modeDropdownRect, currentModeLabel))
        {
            var options = (from Settings.PlayerDialogueMode mode in Enum.GetValues(typeof(Settings.PlayerDialogueMode))
                select new FloatMenuOption(GetPlayerDialogueModeLabel(mode), () => _editingMode = mode)).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        curY = modeRowRect.yMax + 6f;

        // 1.1 In-window Mode Description (replaces hover tooltip)
        string modeDesc = "RimTalk.PlayerSettings.ModeDesc".Translate();
        Text.Font = GameFont.Tiny;
        float descHeight = Text.CalcHeight(modeDesc, inRect.width);
        Rect descRect = new Rect(inRect.x + 4f, curY, inRect.width - 8f, descHeight);
        GUI.color = new Color(0.72f, 0.72f, 0.72f);
        Widgets.Label(descRect, modeDesc);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        curY = descRect.yMax + 10f;

        // 2. Player Name Input
        bool isPlayerDialogueEnabled = _editingMode != Settings.PlayerDialogueMode.Disabled;
        bool isPersonaEnabled = _editingMode == Settings.PlayerDialogueMode.AIDriven;

        Rect nameRowRect = new Rect(inRect.x, curY, inRect.width, 28f);
        Rect nameLabelRect = new Rect(nameRowRect.x, nameRowRect.y, labelWidth, nameRowRect.height);
        Rect nameFieldRect = new Rect(nameLabelRect.xMax + 10f, nameRowRect.y, fieldWidth, nameRowRect.height);

        Color savedColor = GUI.color;
        if (!isPlayerDialogueEnabled)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
        }

        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(nameLabelRect, "RimTalk.Settings.PlayerName".Translate());
        Text.Anchor = originalAnchor;

        if (isPlayerDialogueEnabled)
        {
            _editingName = Widgets.TextField(nameFieldRect, _editingName);
        }
        else
        {
            GUI.enabled = false;
            Widgets.TextField(nameFieldRect, _editingName);
            GUI.enabled = true;
        }
        GUI.color = savedColor;
        TooltipHandler.TipRegion(nameRowRect, "RimTalk.Settings.PlayerNameTooltip".Translate());

        curY = nameRowRect.yMax + 10f;

        // 3. Player Persona Section (Only active when AIDriven)
        Rect personaHeaderRect = new Rect(inRect.x, curY, inRect.width, 24f);
        string personaTitle = "RimTalk.Settings.PlayerPersona".Translate();
        if (!isPersonaEnabled)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            personaTitle += " " + "RimTalk.PlayerSettings.PersonaOnlyAIDriven".Translate();
        }

        Widgets.Label(personaHeaderRect, personaTitle);
        GUI.color = Color.white;

        Vector2 titleSize = Text.CalcSize(personaTitle);
        float iconSize = 18f;
        Rect questionMarkRect = new Rect(personaHeaderRect.x + titleSize.x + 5f, personaHeaderRect.y + (personaHeaderRect.height - iconSize) / 2f, iconSize, iconSize);
        TooltipHandler.TipRegion(questionMarkRect, "RimTalk.Settings.PlayerPersonaTooltip".Translate());
        GUI.DrawTexture(questionMarkRect, TexButton.Info);

        curY = personaHeaderRect.yMax + 4f;

        // Scrollable multi-line text area
        Rect textBoxRect = new Rect(inRect.x, curY, inRect.width, 130f);
        float innerWidth = textBoxRect.width - 16f;
        float contentHeight = Mathf.Max(textBoxRect.height, Text.CalcHeight(
            string.IsNullOrEmpty(_editingPersona) ? " " : _editingPersona, innerWidth));

        if (!isPersonaEnabled)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
        }

        Widgets.BeginScrollView(textBoxRect, ref _scrollPos, new Rect(0f, 0f, innerWidth, contentHeight));
        GUI.SetNextControlName(TextControlName);
        if (isPersonaEnabled)
        {
            _editingPersona = Widgets.TextArea(new Rect(0f, 0f, innerWidth, contentHeight), _editingPersona);
        }
        else
        {
            GUI.enabled = false;
            Widgets.TextArea(new Rect(0f, 0f, innerWidth, contentHeight), _editingPersona);
            GUI.enabled = true;
        }
        Widgets.EndScrollView();
        GUI.color = savedColor;

        // Character count
        Rect countRect = new Rect(inRect.x, textBoxRect.yMax + 2f, inRect.width, 18f);
        Text.Font = GameFont.Tiny;
        Color countColor = _editingPersona.Length > 300 ? Color.yellow : Color.gray;
        if (_editingPersona.Length >= MaxLength) countColor = Color.red;
        if (!isPersonaEnabled) countColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
        GUI.color = countColor;
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(countRect, "RimTalk.PersonaEditor.Characters".Translate(_editingPersona.Length, 300));
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        // 4. Bottom Buttons (Save & Clear only)
        float buttonWidth = 110f;
        float buttonHeight = 30f;
        float spacing = 16f;
        float buttonY = countRect.yMax + 10f;

        float totalWidth = (buttonWidth * 2f) + spacing;
        float startX = inRect.center.x - (totalWidth / 2f);

        Rect saveButton = new Rect(startX, buttonY, buttonWidth, buttonHeight);
        Rect clearButton = new Rect(saveButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);

        if (Widgets.ButtonText(saveButton, "RimTalk.PlayerSettings.Save".Translate()))
        {
            _settings.PlayerDialogueMode = _editingMode;
            _settings.PlayerName = string.IsNullOrWhiteSpace(_editingName) ? "Player" : _editingName.Trim();
            _settings.PlayerPersona = _editingPersona.Trim();

            // Re-initialize player pawn name and cache
            Cache.InitializePlayerPawn();

            Messages.Message("RimTalk.PlayerSettings.Saved".Translate(), MessageTypeDefOf.TaskCompletion, false);
            Close();
        }

        if (!isPersonaEnabled) GUI.enabled = false;
        if (Widgets.ButtonText(clearButton, "RimTalk.PersonaEditor.Clear".Translate()))
        {
            _editingPersona = "";
        }
        if (!isPersonaEnabled) GUI.enabled = true;
    }

    private static string GetPlayerDialogueModeLabel(Settings.PlayerDialogueMode mode)
    {
        return mode switch
        {
            Settings.PlayerDialogueMode.Disabled => "RimTalk.Settings.Disabled".Translate().ToString(),
            Settings.PlayerDialogueMode.Manual => "RimTalk.Settings.PlayerDialogueMode.Manual".Translate().ToString(),
            Settings.PlayerDialogueMode.AIDriven => "RimTalk.Settings.PlayerDialogueMode.AIDriven".Translate().ToString(),
            Settings.PlayerDialogueMode.AIDrivenPawnOnly => "RimTalk.Settings.PlayerDialogueMode.AIDrivenPawnOnly".Translate().ToString(),
            _ => mode.ToString()
        };
    }
}
