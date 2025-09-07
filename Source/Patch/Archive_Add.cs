using HarmonyLib;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(Archive), nameof(Archive.Add))]
    public static class Archive_Add
    {
        public static void Prefix(IArchivable archivable)
        {
            var settings = Settings.Get();

            string typeName = archivable.GetType().FullName;

            // Check if this type should be processed
            bool shouldProcess = settings.enabledArchivableTypes.ContainsKey(typeName)
                ? settings.enabledArchivableTypes[typeName]
                : false;

            if (!shouldProcess)
            {
                return;
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
            
            TalkRequestPool.Add(prompt);
        }
    }
}