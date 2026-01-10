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
    private const float IconSize = 32f;
    private const float IconSpacing = 1f;

    public CustomDialogueWindow(Pawn initiator, Pawn recipient)
    {
        _initiator = initiator;
        _recipient = recipient;
        doCloseX = true;
        draggable = true;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
    }

    public override Vector2 InitialSize => new(400f, 150f);

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Small;
        float headerHeight = 35f;

        float iconStartX = inRect.width - (IconSize * 2 + IconSpacing);
        float iconOffsetY = (headerHeight - IconSize) / 2f;

        Rect directRect = new Rect(iconStartX, iconOffsetY, IconSize, IconSize);
        Rect announceRect = new Rect(iconStartX + IconSize + IconSpacing, iconOffsetY, IconSize, IconSize);

        DrawModeButton(directRect, DialogueMode.Direct, DirectIcon);
        DrawModeButton(announceRect, DialogueMode.Announce, AnnounceIcon);

        Rect labelRect = new Rect(0f, 0f, iconStartX - 5f, headerHeight);
        Text.Anchor = TextAnchor.MiddleLeft;
        
        string label = _mode switch
        {
            DialogueMode.Direct => _initiator.IsPlayer() 
                ? "RimTalk.FloatMenu.WhatToSayToSelf".Translate(_recipient.LabelShortCap)
                : "RimTalk.FloatMenu.WhatToSayToOther".Translate(_initiator.LabelShortCap, _recipient.LabelShortCap),
            DialogueMode.Announce => "RimTalk.FloatMenu.WhatToAnnounce".Translate(_recipient.LabelShortCap),
            _ => ""
        };
        
        Widgets.Label(labelRect, label);
        Text.Anchor = TextAnchor.UpperLeft;

        GUI.SetNextControlName(TextFieldControlName);
        _text = Widgets.TextField(new Rect(0f, 40f, inRect.width, 35f), _text);

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

        if (Widgets.ButtonText(new Rect(0f, 85f, inRect.width / 2f - 5f, 35f), "RimTalk.FloatMenu.Send".Translate()))
        {
            if (!string.IsNullOrWhiteSpace(_text))
            {
                SendDialogue(_text);
            }
            Close();
        }

        if (Widgets.ButtonText(new Rect(inRect.width / 2f + 5f, 85f, inRect.width / 2f - 5f, 35f), "RimTalk.FloatMenu.Cancel".Translate()))
        {
            Close();
        }
    }

    private void DrawModeButton(Rect rect, DialogueMode mode, Texture2D icon)
    {
        if (_mode == mode)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 1f, new Color(0.4f, 0.7f, 1f, 0.5f), 0f, 0f);
        }
        if (Widgets.ButtonImage(rect, icon))
        {
            _mode = mode;
        }
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
        if (_mode == DialogueMode.Announce)
        {
            CustomDialogueService.ExecuteDialogue(_recipient, null, dialogue);
            return;
        }

        // Direct Mode (Original Behavior)
        if (CustomDialogueService.CanTalk(_initiator, _recipient))
        {
            // Already close and in same room (or talking to self) - execute immediately
            CustomDialogueService.ExecuteDialogue(_initiator, _recipient, dialogue);
        }
        else
        {
            // Store pending dialogue and make pawn walk to target
            CustomDialogueService.PendingDialogues[_initiator] = 
                new CustomDialogueService.PendingDialogue(_recipient, dialogue);

            Job job = JobMaker.MakeJob(JobDefOf.Goto, _recipient);
            job.playerForced = true;
            job.collideWithPawns = false;
            job.locomotionUrgency = LocomotionUrgency.Jog;

            _initiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}