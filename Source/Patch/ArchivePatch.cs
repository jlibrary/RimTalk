using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
public static class ArchivePatch
{
    public static void Prefix(IArchivable archivable)
    {
        if (!ShouldProcessArchivable(archivable))
        {
            return;
        }

        // Generate the prompt text first, as it's needed in all cases.
        // Decide quest category & generate prompt (kept compatible with original text)
        var (prompt, talkType) = GeneratePrompt(archivable);
        var eventMap = FindLocation(archivable);

        TalkRequestPool.Add(prompt, mapId: eventMap?.uniqueID ?? -1, talkType: talkType);
    }

    private static bool ShouldProcessArchivable(IArchivable archivable)
    {
        var settings = Settings.Get();
        var enabledTypes = settings.EnabledArchivableTypes;

        // Messages are disabled by default unless explicitly enabled
        if (archivable is Message message)
        {
            if (!enabledTypes.TryGetValue("Verse.Message", out var isTypeEnabled) || !isTypeEnabled)
                return false;

            if (message.def != null && enabledTypes.TryGetValue(message.def.defName, out var isDefEnabled) && !isDefEnabled)
                return false;

            return true;
        }

        // Letters and other events are enabled by default unless explicitly disabled
        string typeName = archivable.GetType().FullName;
        if (enabledTypes.TryGetValue(typeName, out var isEnabled) && !isEnabled)
        {
            return false;
        }

        if (archivable is Letter letter && letter.def != null)
        {
            if (enabledTypes.TryGetValue(letter.def.defName, out var isDefEnabled) && !isDefEnabled)
            {
                return false;
            }
        }

        return true;
    }


    private static (string prompt, TalkType talkType) GeneratePrompt(IArchivable archivable)
    {
        var talkType = TalkType.Event;
        string prompt;
        string targetSuffix = GetTargetSuffix(archivable);

        if (archivable is ChoiceLetter { quest: not null } choiceLetter)
        {
            if (choiceLetter.quest.State == QuestState.NotYetAccepted)
            {
                talkType = TalkType.QuestOffer;
                prompt = $"(Talk if you want to accept quest)\n[{choiceLetter.quest.description.ToString().StripTags()}]";
            }
            else
            {
                talkType = TalkType.QuestEnd;
                prompt = $"(Talk about quest result)\n[{archivable.ArchivedTooltip.StripTags()}]";
            }
        }
        else if (archivable is Letter and not ChoiceLetter)
        {
            var label = archivable.ArchivedLabel ?? string.Empty;
            var tip = archivable.ArchivedTooltip ?? string.Empty;
            
            if (ContainsQuestReference(label, tip))
            {
                talkType = TalkType.QuestEnd;
                prompt = $"(Talk about quest result)\n[{tip.StripTags()}]";
            }
            else
            {
                prompt = $"(Talk about incident)\n[{tip.StripTags()}{targetSuffix}]";
            }
        }
        else
        {
            // Other events
            prompt = $"(Talk about incident)\n[{archivable.ArchivedTooltip.StripTags()}{targetSuffix}]";
        }

        return (prompt, talkType);
    }

    private static string GetTargetSuffix(IArchivable archivable)
    {
        var pawn = archivable?.LookTargets?.PrimaryTarget.Thing as Pawn
            ?? archivable?.LookTargets?.targets?.Select(t => t.Thing as Pawn).FirstOrDefault(p => p != null);

        return pawn != null ? $" (Target: {pawn.LabelShort})" : string.Empty;
    }


    private static bool ContainsQuestReference(string label, string tip)
    {
        return label.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0
            || tip.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Map FindLocation(IArchivable archivable)
    {
        if (archivable.LookTargets is not { Any: true })
            return null;

        return archivable.LookTargets.PrimaryTarget.Map 
            ?? archivable.LookTargets.targets.Select(t => t.Map).FirstOrDefault(m => m != null);
    }
}
