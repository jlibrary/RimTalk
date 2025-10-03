using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
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
        var prompt = "";
        if (archivable is ChoiceLetter choiceLetter && choiceLetter.quest != null)
        {
            prompt += $"(Talk if you want to accept quest)\n{choiceLetter.quest.description.ToString().StripTags()}";
        }
        else
        {
            prompt += $"(Talk about incident)\n{archivable.ArchivedTooltip.StripTags()}";
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
                IntVec3 targetPosition = archivable.LookTargets.PrimaryTarget.Cell;

                nearbyColonists = eventMap.mapPawns.AllPawnsSpawned
                    .Where(pawn => Cache.Get(pawn)?.CanDisplayTalk() == true)
                    .OrderBy(pawn => pawn.Position.DistanceTo(targetPosition))
                    .Take(3)
                    .ToList();
            }
        }

        // --- Decide which type of request to create ---
        if (nearbyColonists.Any())
        {
            // If specific colonists are nearby, create a request for each one.
            foreach (var pawn in nearbyColonists)
            {
                Cache.Get(pawn)?.AddTalkRequest(prompt, type: TalkRequest.Type.Event);
            }
        }
        else
        {
            TalkRequestPool.Add(prompt, mapId: eventMap?.uniqueID ?? 0);
        }
    }
}