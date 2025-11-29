using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Patches;

[StaticConstructorOnStartup]
public static class BioTabPersonalityPatch
{
    private static readonly Texture2D RimTalkIcon = ContentFinder<Texture2D>.Get("UI/RimTalkIcon");

    private static void AddPersonaElement(Pawn pawn)
    {
        if (!pawn.IsColonist && !pawn.IsPrisonerOfColony && !pawn.HasVocalLink())
            return;

        var tmpStackElements =
            (List<GenUI.AnonymousStackElement>)AccessTools.Field(typeof(CharacterCardUtility), "tmpStackElements")
                .GetValue(null);
        if (tmpStackElements == null) return;

        string personaLabelText = "RimTalk.BioTab.RimTalkPersona".Translate();
        float textWidth = Text.CalcSize(personaLabelText).x;
        float totalLabelWidth = 22f + 5f + textWidth + 5f; // Icon + padding + text + padding

        tmpStackElements.Add(new GenUI.AnonymousStackElement
        {
            width = totalLabelWidth,
            drawer = rect =>
            {
                Widgets.DrawOptionBackground(rect, false);
                Widgets.DrawHighlightIfMouseover(rect);

                string persona = PersonaService.GetPersonality(pawn);
                float chattiness = PersonaService.GetTalkInitiationWeight(pawn);
                string tooltipText =
                    $"{"RimTalk.PersonaEditor.Title".Translate(pawn.LabelShort).Colorize(ColoredText.TipSectionTitleColor)}\n\n{persona}\n\n{"RimTalk.PersonaEditor.Chattiness".Translate().Colorize(ColoredText.TipSectionTitleColor)} {chattiness:0.00}";
                TooltipHandler.TipRegion(rect, tooltipText);

                Rect iconRect = new Rect(rect.x + 2f, rect.y + 1f, 20f, 20f);
                GUI.DrawTexture(iconRect, RimTalkIcon);

                Rect labelRect = new Rect(iconRect.xMax + 5f, rect.y, textWidth, rect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, personaLabelText);
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(rect))
                {
                    Find.WindowStack.Add(new PersonaEditorWindow(pawn));
                }
            }
        });
    }

    [HarmonyPatch(typeof(CharacterCardUtility), "DoTopStack")]
    public static class DoTopStack_Patch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo anchorMethod = AccessTools.Method(
                typeof(QuestUtility),
                nameof(QuestUtility.AppendInspectStringsFromQuestParts),
                new Type[]
                {
                    typeof(Action<string, Quest>),
                    typeof(ISelectable),
                    typeof(int).MakeByRefType()
                }
            );

            foreach (var instruction in instructions)
            {
                yield return instruction;

                if (instruction.Calls(anchorMethod))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // Load 'pawn'
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(BioTabPersonalityPatch), nameof(AddPersonaElement)));
                }
            }
        }
    }
}