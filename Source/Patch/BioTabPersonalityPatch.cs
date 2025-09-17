using HarmonyLib;
using RimTalk.UI;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimTalk.Patches
{
    [HarmonyPatch(typeof(CharacterCardUtility), nameof(CharacterCardUtility.DrawCharacterCard))]
    public static class BioTabPersonalityPatch
    {
        public static void Postfix(Rect rect, Pawn pawn)
        {
            // The pawn check is all we need.
            if (pawn?.RaceProps?.Humanlike != true || !pawn.IsFreeColonist)
            {
                return;
            }

            const float nameAreaHeight = 38f;
            const float ageAreaHeight = 24f;
            const float verticalPadding = 4f;

            float raceInfoLineY = rect.y + nameAreaHeight + ageAreaHeight + verticalPadding;

            const float buttonWidth = 120f;
            const float buttonHeight = 22f;
            const float rightMargin = 3f;

            float buttonX = rect.xMax - buttonWidth - rightMargin;

            Rect linkRect = new Rect(buttonX, raceInfoLineY, buttonWidth, buttonHeight);

            if (Widgets.ButtonText(linkRect, "RimTalk.BioTab.RimTalkPersona".Translate()))
            {
                Find.WindowStack.Add(new PersonaEditorWindow(pawn));
            }
        }
    }
}