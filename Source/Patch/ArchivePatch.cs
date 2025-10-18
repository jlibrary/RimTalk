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
        var settings = Settings.Get();
        string typeName = archivable.GetType().FullName;

        // Check if this type should be processed
        bool shouldProcess = settings.EnabledArchivableTypes.ContainsKey(typeName)
            ? settings.EnabledArchivableTypes[typeName]
            : false;

        if (!shouldProcess)
        {
            return;
        }

        // Generate the prompt text first, as it's needed in all cases.
        // Decide quest category & generate prompt (kept compatible with original text)
        var (prompt, talkType) = GeneratePrompt(archivable);
        var (eventMap, nearbyColonists) = FindLocationAndColonists(archivable);

        // If specific colonists are nearby, create a request for each one.
        if (nearbyColonists.Any())
        {
            foreach (var pawn in nearbyColonists)
                Cache.Get(pawn)?.AddTalkRequest(prompt, talkType: talkType);
        }
        else
            TalkRequestPool.Add(prompt, mapId: eventMap?.uniqueID ?? 0);
    }

    private static (string prompt, TalkType talkType) GeneratePrompt(IArchivable archivable)
    {
        var talkType = TalkType.Event;
        string prompt;

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
                prompt = $"(Talk about incident)\n[{tip.StripTags()}]";
            }
        }
        else
        {
            // Other events
            prompt = $"(Talk about incident)\n[{archivable.ArchivedTooltip.StripTags()}]";
        }

        return (prompt, talkType);
    }

    private static bool ContainsQuestReference(string label, string tip)
    {
        return label.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0
            || tip.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static (Map eventMap, List<Pawn> nearbyColonists) FindLocationAndColonists(IArchivable archivable)
    {
        Map eventMap = null;
        var nearbyColonists = new List<Pawn>();

        // --- Safely check for location and nearby pawns ---
        if (archivable.LookTargets is not { Any: true })
            return (null, nearbyColonists);

        // Try to determine the map from the look targets
        eventMap = archivable.LookTargets.PrimaryTarget.Map 
            ?? archivable.LookTargets.targets.Select(t => t.Map).FirstOrDefault(m => m != null);

        // If we successfully found a map, look for the nearest colonists
        if (eventMap != null)
        {
            nearbyColonists = eventMap.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn.IsFreeNonSlaveColonist && !pawn.IsQuestLodger() && Cache.Get(pawn)?.CanDisplayTalk() == true)
                .ToList();
        }

        return (eventMap, nearbyColonists);
    }
}