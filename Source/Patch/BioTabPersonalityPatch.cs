using System;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patches;

[StaticConstructorOnStartup]
[HarmonyPatch(typeof(CharacterCardUtility), nameof(CharacterCardUtility.DrawCharacterCard))]
public static class BioTabPersonalityPatch
{
    private static readonly Texture2D RimTalkIcon = ContentFinder<Texture2D>.Get("UI/RimTalkIcon");

    public static void Postfix(Rect rect, Pawn pawn, Action randomizeCallback = null)
    {
        if (pawn?.RaceProps?.Humanlike != true || !pawn.IsFreeColonist)
        {
            return;
        }

        bool creationMode = randomizeCallback != null;
        float curX = rect.x;
        const float elementHeight = 22f;
        const float elementGap = 4f;
        float curY = rect.y + 40f;

        if (ModsConfig.BiotechActive && creationMode)
        {
            curX += 22f + elementGap;
        }

        bool flag = ModsConfig.BiotechActive && creationMode;
        string mainDesc = pawn.MainDesc(false, !flag);
        float mainDescWidth = Text.CalcSize(mainDesc).x;
        curX += mainDescWidth + 5f + elementGap;

        if (ModsConfig.BiotechActive && pawn.genes != null && pawn.genes.GenesListForReading.Any())
        {
            float xenotypeWidth = 22f + 14f + Text.CalcSize(pawn.genes.XenotypeLabelCap).x;
            float availableWidth = creationMode
                ? (rect.width - 20f - Page_ConfigureStartingPawns.PawnPortraitSize.x)
                : (rect.width - 10f);

            if (curX + xenotypeWidth < availableWidth)
            {
                curX += xenotypeWidth + elementGap;
            }
        }

        string personaLabelText = "RimTalk.BioTab.RimTalkPersona".Translate();
        float textWidth = Text.CalcSize(personaLabelText).x;
        float totalLabelWidth = 22f + textWidth + 14f;

        Rect personaRect = new Rect(curX, curY, totalLabelWidth, elementHeight);

        // GUI.color = CharacterCardUtility.StackElementBackground;
        // GUI.DrawTexture(personaRect, BaseContent.WhiteTex);
        // GUI.color = Color.white;
        //
        // Widgets.DrawHighlightIfMouseover(personaRect);
        Widgets.DrawOptionBackground(personaRect, false);
        Widgets.DrawHighlightIfMouseover(personaRect);

        string persona = PersonaService.GetPersonality(pawn);
        float chattiness = PersonaService.GetTalkInitiationWeight(pawn);
        string tooltipText =
            $"{"RimTalk.PersonaEditor.Title".Translate(pawn.LabelShort).Colorize(ColoredText.TipSectionTitleColor)}\n\n{persona}\n\n{"RimTalk.PersonaEditor.Chattiness".Translate().Colorize(ColoredText.TipSectionTitleColor)} {chattiness:0.00}";

        TooltipHandler.TipRegion(personaRect, tooltipText);

        Rect iconRect = new Rect(personaRect.x + 1f, personaRect.y + 1f, 20f, 20f);
        GUI.DrawTexture(iconRect, RimTalkIcon);

        Rect labelRect = new Rect(personaRect.x + 22f + 5f, personaRect.y, textWidth, personaRect.height);
        var originalAnchor = Text.Anchor;
        try
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, personaLabelText);
        }
        finally
        {
            Text.Anchor = originalAnchor;
        }

        if (Widgets.ButtonInvisible(personaRect))
        {
            Find.WindowStack.Add(new PersonaEditorWindow(pawn));
        }
    }
}