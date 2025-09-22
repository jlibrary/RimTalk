using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
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
            
            Map eventMap = null;
            
            if (archivable.LookTargets != null)
            {
                eventMap = archivable.LookTargets.PrimaryTarget.Map;
                if (eventMap == null)
                {
                    eventMap = archivable.LookTargets.targets
                        .Select(t => t.Map)
                        .FirstOrDefault(m => m != null);
                }
            }
            
            var prompt = "";
            if (archivable is ChoiceLetter choiceLetter && choiceLetter.quest != null)
            {
                prompt += $"(Talk if you want to accept quest)\n{choiceLetter.quest.description.ToString().StripTags()}";
            }
            else
            {
                prompt += $"(Talk about incident)\n{archivable.ArchivedTooltip.StripTags()}";
            }
            
            // Use the correctly determined map's unique ID
            TalkRequestPool.Add(prompt, mapId: eventMap?.uniqueID ?? 0);
        }
    }
}