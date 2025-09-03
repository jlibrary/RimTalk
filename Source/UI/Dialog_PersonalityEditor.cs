using RimTalk.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.UI
{
    public class Dialog_PersonalityEditor : Window
    {
        private Pawn pawn;
        private string editingPersonality;
        private PersonalityManager personalityManager;
        private static readonly int MAX_LENGTH = 300; // Reasonable limit

        public Dialog_PersonalityEditor(Pawn pawn)
        {
            this.pawn = pawn;
            this.personalityManager = Current.Game.GetComponent<PersonalityManager>();
            this.editingPersonality = personalityManager?.GetPersonality(pawn) ?? "";

            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(520f, 380f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            Widgets.Label(titleRect, "RimTalk.PersonalityEditor.Title".Translate(pawn.Name.ToStringShort));

            // Instruction text
            Text.Font = GameFont.Small;
            Rect instructRect = new Rect(inRect.x, titleRect.yMax + 5f, inRect.width, 40f);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(instructRect,
                "RimTalk.PersonalityEditor.Instruct".Translate());
            GUI.color = Color.white;

            // Text area with character counter
            Rect textAreaRect = new Rect(inRect.x, instructRect.yMax + 10f, inRect.width, 180f);
            string previousText = editingPersonality;
            editingPersonality = Widgets.TextArea(textAreaRect, editingPersonality);

            // Enforce character limit
            if (editingPersonality.Length > MAX_LENGTH)
            {
                editingPersonality = editingPersonality.Substring(0, MAX_LENGTH);
            }

            // Character count
            Rect countRect = new Rect(inRect.x, textAreaRect.yMax + 2f, inRect.width, 20f);
            Text.Font = GameFont.Tiny;
            Color countColor = editingPersonality.Length > MAX_LENGTH * 0.9f ? Color.yellow : Color.gray;
            if (editingPersonality.Length >= MAX_LENGTH) countColor = Color.red;
            GUI.color = countColor;
            Widgets.Label(countRect, "RimTalk.PersonalityEditor.Characters".Translate(editingPersonality.Length, MAX_LENGTH));
            GUI.color = Color.white;

            // Buttons
            float buttonWidth = 90f;
            float buttonHeight = 28f;
            float spacing = 10f;
            float buttonY = countRect.yMax + 15f;

            Rect randomButton = new Rect(inRect.center.x - (buttonWidth / 2f), buttonY, buttonWidth, buttonHeight);
            Rect saveButton = new Rect(randomButton.x - buttonWidth - spacing, buttonY, buttonWidth, buttonHeight);
            Rect clearButton = new Rect(randomButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);

            if (Widgets.ButtonText(saveButton, "RimTalk.PersonalityEditor.Save".Translate()))
            {
                personalityManager?.SetPersonality(pawn, editingPersonality.Trim());
                Messages.Message("RimTalk.PersonalityEditor.Updated".Translate(pawn.Name.ToStringShort),
                    MessageTypeDefOf.TaskCompletion, false);
                Close();
            }

            if (Widgets.ButtonText(randomButton, "RimTalk.PersonalityEditor.Random".Translate()))
            {
                editingPersonality = Constant.Personalities.RandomElement();
            }

            if (Widgets.ButtonText(clearButton, "RimTalk.PersonalityEditor.Clear".Translate()))
            {
                editingPersonality = "";
            }
        }
    }
}