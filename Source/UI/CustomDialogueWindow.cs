using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimTalk.UI;

public enum DialogueMode
{
    Direct,
    Announce
}

[StaticConstructorOnStartup]
public class CustomDialogueWindow : Window
{
    private readonly Pawn _initiator;
    private readonly Pawn _recipient;
    private string _text = "";
    private const string TextFieldControlName = "CustomTalkTextField";
    
    private DialogueMode _mode = DialogueMode.Direct;
    private bool _attachPhoto;
    private static Texture2D _directIcon;
    private static Texture2D DirectIcon => _directIcon ??= ContentFinder<Texture2D>.Get("UI/ChatGizmo");
    private static Texture2D _announceIcon;
    private static Texture2D AnnounceIcon => _announceIcon ??= ContentFinder<Texture2D>.Get("UI/AnnounceGizmo");
    private static Texture2D _cameraIcon;
    private static Texture2D CameraIcon => _cameraIcon ??= ContentFinder<Texture2D>.Get("UI/VisionGizmo");
    private const float IconSize = 28f;
    private const float IconSpacing = 4f;

    public CustomDialogueWindow(Pawn initiator, Pawn recipient, DialogueMode defaultMode = DialogueMode.Direct)
    {
        _initiator = initiator;
        _recipient = recipient;
        _mode = Settings.Get().AllowAnnouncement ? defaultMode : DialogueMode.Direct;
        doCloseX = true;
        draggable = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new(450f, 155f);

    public override void DoWindowContents(Rect inRect)
    {
        bool allowAnnouncement = Settings.Get().AllowAnnouncement;
        if (Event.current.type == EventType.KeyDown)
        {
            bool isCtrlOrCmd = Event.current.control || Event.current.command;
            if (isCtrlOrCmd && (Event.current.keyCode == KeyCode.Alpha1 || Event.current.keyCode == KeyCode.Keypad1))
            {
                _attachPhoto = !_attachPhoto;
                Event.current.Use();
            }
            else if (allowAnnouncement && Event.current.keyCode == KeyCode.Tab)
            {
                _mode = _mode == DialogueMode.Direct ? DialogueMode.Announce : DialogueMode.Direct;
                Event.current.Use();
            }
        }

        Text.Font = GameFont.Small;

        int buttonCount = (allowAnnouncement ? 2 : 0) + 1; // +1 for camera toggle
        float iconStartX = inRect.width - (buttonCount * IconSize + (buttonCount - 1) * IconSpacing);

        float currentBtnX = iconStartX;
        if (allowAnnouncement)
        {
            Rect directRect = new Rect(currentBtnX, 0f, IconSize, IconSize);
            currentBtnX += IconSize + IconSpacing;
            Rect announceRect = new Rect(currentBtnX, 0f, IconSize, IconSize);
            currentBtnX += IconSize + IconSpacing;

            DrawModeButton(directRect, DialogueMode.Direct, DirectIcon, "RimTalk.FloatMenu.DirectModeTooltip".Translate());
            DrawModeButton(announceRect, DialogueMode.Announce, AnnounceIcon, "RimTalk.FloatMenu.AnnounceModeTooltip".Translate());
        }

        Rect cameraRect = new Rect(currentBtnX, 0f, IconSize, IconSize);
        DrawToggleButton(cameraRect, ref _attachPhoto, CameraIcon, "RimTalk.FloatMenu.AttachPhotoTooltip".Translate());

        float labelWidth = iconStartX - 6f;
        string recipientLabel = PromptService.GetUniqueName(_recipient).CapitalizeFirst();
        string initiatorLabel = PromptService.GetUniqueName(_initiator).CapitalizeFirst();
        string labelText = _mode switch
        {
            DialogueMode.Direct => _initiator.IsPlayer()
                ? "RimTalk.FloatMenu.WhatToSayToSelf".Translate(recipientLabel)
                : "RimTalk.FloatMenu.WhatToSayToOther".Translate(initiatorLabel, recipientLabel),
            DialogueMode.Announce => _initiator.IsPlayer()
                ? "RimTalk.FloatMenu.WhatToAnnounceToSelf".Translate(recipientLabel)
                : "RimTalk.FloatMenu.WhatToAnnounceToOther".Translate(initiatorLabel),
            _ => ""
        };

        // Measure actual label height so it never clips
        float minHeaderHeight = allowAnnouncement ? IconSize : 24f;
        float labelHeight = Mathf.Max(minHeaderHeight, Text.CalcHeight(labelText, labelWidth));
        Rect labelRect = new Rect(0f, 0f, labelWidth, labelHeight);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(labelRect, labelText);
        Text.Anchor = TextAnchor.UpperLeft;

        float fieldY = labelHeight + 4f;
        GUI.SetNextControlName(TextFieldControlName);
        _text = Widgets.TextField(new Rect(0f, fieldY, inRect.width, 35f), _text);

        if (Event.current.type == EventType.Repaint && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
        {
            GUI.FocusControl(TextFieldControlName);
        }
        
        if (GUI.GetNameOfFocusedControl() == TextFieldControlName && Event.current.isKey && Event.current.keyCode == KeyCode.Return)
        {
            if (!string.IsNullOrWhiteSpace(_text))
            {
                SendDialogue(_text);
            }
            Event.current.Use();
        }

        float buttonY = fieldY + 39f;
        if (Widgets.ButtonText(new Rect(0f, buttonY, inRect.width / 2f - 5f, 35f), "RimTalk.FloatMenu.Send".Translate()))
        {
            if (!string.IsNullOrWhiteSpace(_text))
            {
                SendDialogue(_text);
            }
        }

        if (Widgets.ButtonText(new Rect(inRect.width / 2f + 5f, buttonY, inRect.width / 2f - 5f, 35f), "RimTalk.FloatMenu.Cancel".Translate()))
        {
            Close();
        }
    }

    private void DrawModeButton(Rect rect, DialogueMode mode, Texture2D icon, string tooltip)
    {
        bool isSelected = _mode == mode;
        if (isSelected)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.3f, 0.6f, 0.9f, 0.25f));
            Widgets.DrawHighlightSelected(rect);
        }
        else
        {
            Widgets.DrawHighlightIfMouseover(rect);
        }

        if (Widgets.ButtonImage(rect, icon))
        {
            _mode = mode;
            GUI.FocusControl(TextFieldControlName);
        }

        TooltipHandler.TipRegion(rect, tooltip);
    }

    private void DrawToggleButton(Rect rect, ref bool isToggled, Texture2D icon, string tooltip)
    {
        if (isToggled)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.3f, 0.8f, 0.4f, 0.25f));
            Widgets.DrawHighlightSelected(rect);
        }
        else
        {
            Widgets.DrawHighlightIfMouseover(rect);
        }

        if (Widgets.ButtonImage(rect, icon))
        {
            isToggled = !isToggled;
            GUI.FocusControl(TextFieldControlName);
        }

        TooltipHandler.TipRegion(rect, tooltip);
    }

    public override void OnAcceptKeyPressed()
    {
        if (!string.IsNullOrWhiteSpace(_text))
        {
            SendDialogue(_text);
        }
        Event.current.Use();
    }

    private void SendDialogue(string dialogue)
    {
        bool isAnnouncement = _mode == DialogueMode.Announce;

        Close();

        if (_attachPhoto)
        {
            VisionUtil.CaptureScreenAsync(imageBase64 =>
            {
                DispatchDialogue(dialogue, isAnnouncement, imageBase64);
            });
        }
        else
        {
            DispatchDialogue(dialogue, isAnnouncement, null);
        }
    }

    private void DispatchDialogue(string dialogue, bool isAnnouncement, string imageBase64)
    {
        CustomDialogueService.DispatchDialogue(_initiator, _recipient, dialogue, isAnnouncement, imageBase64);
    }
}