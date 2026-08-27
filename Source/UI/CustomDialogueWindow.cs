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
    private static readonly Texture2D DirectIcon = ContentFinder<Texture2D>.Get("UI/ChatGizmo");
    private static readonly Texture2D AnnounceIcon = ContentFinder<Texture2D>.Get("UI/AnnounceGizmo");
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

    public override Vector2 InitialSize => new(420f, 155f);

    public override void DoWindowContents(Rect inRect)
    {
        bool allowAnnouncement = Settings.Get().AllowAnnouncement;
        if (allowAnnouncement && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Tab)
        {
            _mode = _mode == DialogueMode.Direct ? DialogueMode.Announce : DialogueMode.Direct;
            Event.current.Use();
        }

        Text.Font = GameFont.Small;

        float iconStartX = allowAnnouncement ? inRect.width - (IconSize * 2 + IconSpacing) : inRect.width;
        if (allowAnnouncement)
        {
            Rect directRect = new Rect(iconStartX, 0f, IconSize, IconSize);
            Rect announceRect = new Rect(iconStartX + IconSize + IconSpacing, 0f, IconSize, IconSize);

            DrawModeButton(directRect, DialogueMode.Direct, DirectIcon, "RimTalk.FloatMenu.DirectModeTooltip".Translate());
            DrawModeButton(announceRect, DialogueMode.Announce, AnnounceIcon, "RimTalk.FloatMenu.AnnounceModeTooltip".Translate());
        }

        float labelWidth = iconStartX - (allowAnnouncement ? 6f : 0f);
        string labelText = _mode switch
        {
            DialogueMode.Direct => _initiator.IsPlayer()
                ? "RimTalk.FloatMenu.WhatToSayToSelf".Translate(_recipient.LabelShortCap)
                : "RimTalk.FloatMenu.WhatToSayToOther".Translate(_initiator.LabelShortCap, _recipient.LabelShortCap),
            DialogueMode.Announce => _initiator.IsPlayer()
                ? "RimTalk.FloatMenu.WhatToAnnounceToSelf".Translate(_recipient.LabelShortCap)
                : "RimTalk.FloatMenu.WhatToAnnounceToOther".Translate(_initiator.LabelShortCap),
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
                Close();
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
            Close();
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

    public override void OnAcceptKeyPressed()
    {
        if (!string.IsNullOrWhiteSpace(_text))
        {
            SendDialogue(_text);
        }
        Close();
        Event.current.Use();
    }

    private void SendDialogue(string dialogue)
    {
        bool isAnnouncement = _mode == DialogueMode.Announce;
        if (CustomDialogueService.CanTalk(_initiator, _recipient))
        {
            // Already close and in same room (or talking to self) - execute immediately
            CustomDialogueService.ExecuteDialogue(_initiator, _recipient, dialogue, isAnnouncement);
        }
        else
        {
            // Store pending dialogue and make pawn walk to target
            CustomDialogueService.PendingDialogues[_initiator] = 
                new CustomDialogueService.PendingDialogue(_recipient, dialogue, isAnnouncement);

            Job job = JobMaker.MakeJob(JobDefOf.Goto, _recipient);
            job.playerForced = true;
            job.collideWithPawns = false;
            job.locomotionUrgency = LocomotionUrgency.Jog;

            _initiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}