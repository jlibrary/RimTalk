using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

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
        var prompt = "";
        var talkType = TalkType.Event; // default

        if (archivable is ChoiceLetter choiceLetter && choiceLetter.quest != null)
        {
            if (choiceLetter.quest.State == QuestState.NotYetAccepted)
            {
                talkType = TalkType.QuestOffer;
                prompt += $"(Talk if you want to accept quest)\n[{choiceLetter.quest.description.ToString().StripTags()}]";
            }
            else
            {
                talkType = TalkType.QuestEnd;
                prompt += $"(Talk about quest result)\n[{archivable.ArchivedTooltip.StripTags()}]";
            }
        }
        else if (archivable is Letter && !(archivable is ChoiceLetter))
        {
            var label = archivable.ArchivedLabel ?? string.Empty;
            var tip   = archivable.ArchivedTooltip ?? string.Empty;
            if (label.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0
                || tip.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0
                || label.Contains("Quest") || tip.Contains("Quest"))
            {
                talkType = TalkType.QuestEnd;
                prompt += $"(Talk about quest result)\n[{archivable.ArchivedTooltip.StripTags()}]";
            }
            else
            {
                prompt += $"(Talk about incident)\n[{archivable.ArchivedTooltip.StripTags()}]";
            }
        }
        else
        {
            // Other events
            prompt += $"(Talk about incident)\n[{archivable.ArchivedTooltip.StripTags()}]";
        }

        Map eventMap = null;
        var nearbyColonists = new List<Pawn>();

        // --- Safely check for location and nearby pawns ---
        if (archivable.LookTargets != null && archivable.LookTargets.Any)
        {
            // Try to determine the map from the look targets
            eventMap = archivable.LookTargets.PrimaryTarget.Map ?? 
                       archivable.LookTargets.targets.Select(t => t.Map).FirstOrDefault(m => m != null);
                
            // If we successfully found a map, look for the nearest colonists
            if (eventMap != null)
            {
                nearbyColonists = eventMap.mapPawns.AllPawnsSpawned
                    .Where(pawn => pawn.IsFreeColonist && Cache.Get(pawn)?.CanDisplayTalk() == true)
                    .ToList();
            }
        }

        // --- Decide which type of request to create ---
        if (nearbyColonists.Any())
        {
            // If specific colonists are nearby, create a request for each one.
            foreach (var pawn in nearbyColonists)
            {
                Cache.Get(pawn)?.AddTalkRequest(prompt, talkType: talkType);
            }
        }
        else
        {
            TalkRequestPool.Add(prompt, mapId: eventMap?.uniqueID ?? 0);
        }
    }
}