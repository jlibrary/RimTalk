using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimTalk.UI;

public class CustomDialogueWindow : Window
{
    private readonly Pawn _initiator;
    private readonly Pawn _recipient;
    private string _text = "";
    private const string TextFieldControlName = "CustomTalkTextField";

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
        
        string labelText = _initiator.IsPlayer()
            ? "RimTalk.FloatMenu.WhatToSayToSelf".Translate(_recipient.LabelShortCap)
            : "RimTalk.FloatMenu.WhatToSayToOther".Translate(_initiator.LabelShortCap, _recipient.LabelShortCap);
        
        Widgets.Label(new Rect(0f, 0f, inRect.width, 25f), labelText);

        GUI.SetNextControlName(TextFieldControlName);
        _text = Widgets.TextField(new Rect(0f, 30f, inRect.width, 35f), _text);

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

        if (Widgets.ButtonText(new Rect(0f, 75f, inRect.width / 2f - 5f, 35f), "RimTalk.FloatMenu.Send".Translate()))
        {
            if (!string.IsNullOrWhiteSpace(_text))
            {
                SendDialogue(_text);
            }
            Close();
        }

        if (Widgets.ButtonText(new Rect(inRect.width / 2f + 5f, 75f, inRect.width / 2f - 5f, 35f), "RimTalk.FloatMenu.Cancel".Translate()))
        {
            Close();
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