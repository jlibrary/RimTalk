using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimTalk.Service;

public static class PromptService
{
    public enum InfoLevel
    {
        Short,
        Normal,
        Full
    }

    public static string BuildContext(List<Pawn> pawns)
    {
        StringBuilder context = new StringBuilder();
        context.AppendLine(Constant.Instruction).AppendLine();

        int count = 0;
        foreach (Pawn pawn in pawns)
        {
            string pawnContext;
            if (pawn.IsPlayer())
            {
                pawnContext =  $"{pawn.LabelShort}\nRole: {pawn.GetRole()}";   
            }
            else
            {
                // Main pawn gets more detail, others get basic info
                InfoLevel infoLevel = pawn == pawns[0] ? InfoLevel.Normal : InfoLevel.Short;
                pawnContext = CreatePawnContext(pawn, infoLevel);
            }

            Cache.Get(pawn).Context = pawnContext;
            count++;
            context.AppendLine();
            context.AppendLine($"[Person {count} START]");
            context.AppendLine(pawnContext);
            context.AppendLine($"[Person {count} END]");
        }

        return context.ToString();
    }

    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        StringBuilder sb = new StringBuilder();

        var name = pawn.LabelShort;
        var title = pawn.story?.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = $"Role: {pawn.GetRole(true)}";
        sb.AppendLine(role);

        if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
        {
            var xenotypeInfo = $"Race: {pawn.genes.Xenotype.LabelCap}";
            // if (!pawn.genes.Xenotype.descriptionShort.NullOrEmpty())
            // xenotypeInfo += $" - {pawn.genes.Xenotype.descriptionShort}";
            sb.AppendLine(xenotypeInfo);
        }

        if (infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy())
        {
            if (ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
            {
                var notableGenes = pawn.genes.GenesListForReading
                    .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                    .Select(g => g.def.LabelCap);

                if (notableGenes.Any())
                {
                    sb.AppendLine($"Notable Genes: {string.Join(", ", notableGenes)}");
                }
            }
        }

        // Add Ideology information
        if (ModsConfig.IdeologyActive && pawn.ideo?.Ideo != null)
        {
            var ideo = pawn.ideo.Ideo;

            var ideologyInfo = $"Ideology: {ideo.name}";
            sb.AppendLine(ideologyInfo);

            var memes = ideo?.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label))
                .ToList();

            if (memes != null && memes.Any())
            {
                sb.AppendLine($"Memes: {string.Join(", ", memes)}");
            }
        }

        //// INVADER AND VISITOR STOP
        if (pawn.IsEnemy() || pawn.IsVisitor())
            return sb.ToString();

        if (pawn.story?.Childhood != null)
        {
            var childHood =
                $"Childhood: {pawn.story.Childhood.title}({pawn.story.Childhood.titleShort})";
            if (infoLevel == InfoLevel.Full) childHood += $":{Sanitize(pawn.story.Childhood.description, pawn)}";
            sb.AppendLine(childHood);
        }

        if (pawn.story?.Adulthood != null)
        {
            var adulthood =
                $"Adulthood: {pawn.story.Adulthood.title}({pawn.story.Adulthood.titleShort})";
            if (infoLevel == InfoLevel.Full) adulthood += $":{Sanitize(pawn.story.Adulthood.description, pawn)}";
            sb.AppendLine(adulthood);
        }

        var traits = "Traits: ";
        foreach (Trait trait in pawn.story?.traits?.TraitsSorted ?? Enumerable.Empty<Trait>())
        {
            foreach (var degreeData in trait.def.degreeDatas.Where(degreeData => degreeData.degree == trait.Degree))
            {
                traits += degreeData.label + (infoLevel == InfoLevel.Full
                    ? $":{Sanitize(degreeData.description, pawn)}\n"
                    : ",");
                break;
            }
        }

        sb.AppendLine(traits);

        if (infoLevel != InfoLevel.Short)
        {
            var skills = "Skills: ";
            foreach (SkillRecord skillRecord in pawn.skills?.skills ?? Enumerable.Empty<SkillRecord>())
            {
                skills += $"{skillRecord.def.label}: {skillRecord.Level}, ";
            }

            sb.AppendLine(skills);
        }

        return sb.ToString();
    }

    public static string CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        StringBuilder sb = new StringBuilder();

        if (pawn.RaceProps.Humanlike)
            sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // add Health
        var method = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");
        IEnumerable<Hediff> hediffs = (IEnumerable<Hediff>)method.Invoke(null, [pawn, false]);

        var hediffDict = hediffs
            .GroupBy(hediff => hediff.def)
            .ToDictionary(
                group => group.Key,
                group => string.Join(",",
                    group.Select(hediff => hediff.Part?.Label ?? ""))); // Values are concatenated body parts

        var healthInfo = string.Join(",", hediffDict.Select(kvp => $"{kvp.Key.label}({kvp.Value})"));

        if (healthInfo != "")
            sb.AppendLine($"Health: {healthInfo}");

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        //// INVADER STOP
        if (pawn.IsEnemy())
            return sb.ToString();

        var m = pawn.needs?.mood;
        var mood = pawn.Downed
            ? "Critical: Downed (in pain/distress)"
            : pawn.InMentalState
                ? $"Mood: {pawn.MentalState?.InspectLine} (in mental break)"
                : $"Mood: {m?.MoodString ?? "N/A"} ({(int)((m?.CurLevelPercentage ?? 0) * 100)}%)";
        sb.AppendLine(mood);

        var thoughts = "Memory: ";
        foreach (Thought thought in GetThoughts(pawn).Keys)
        {
            thoughts += $"{Sanitize(thought.LabelCap)}, ";
        }

        sb.AppendLine(thoughts);

        if (pawn.IsSlave || pawn.IsPrisoner)
            sb.AppendLine(pawn.GetPrisonerSlaveStatus());

        //// VISITOR STOP
        if (pawn.IsVisitor())
        {
            Lord lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                string fullTypeName = lord.LordJob.GetType().Name;
                string cleanName = fullTypeName.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
            }
        }

        sb.AppendLine(RelationsService.GetRelationsString(pawn));

        if (infoLevel != InfoLevel.Short)
        {
            var equipment = "Equipment: ";
            if (pawn.equipment?.Primary != null)
                equipment += $"Weapon: {pawn.equipment.Primary.LabelCap}, ";

            var wornApparel = pawn.apparel?.WornApparel;
            var apparelLabels =
                wornApparel != null ? wornApparel.Select(a => a.LabelCap) : [];

            if (apparelLabels.Any())
            {
                equipment += $"Apparel: {string.Join(", ", apparelLabels)}";
            }

            if (equipment != "Equipment: ")
                sb.AppendLine(equipment);
        }

        return sb.ToString();
    }

    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var sb = new StringBuilder();
        CommonUtil.InGameData gameData = CommonUtil.GetInGameData();

        string shortName = $"{pawns[0].LabelShort}({pawns[0].GetRole()})";

        // Add the conversation part
        if (talkRequest.TalkType == TalkType.User)
        {
            sb.Append($"{pawns[1].LabelShort}({pawns[1].GetRole()}) said to '{pawns[0].LabelShort}({pawns[0].GetRole()}): {talkRequest.Prompt}'.");
            sb.Append($"Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {pawns[0].LabelShort}");
        }
        else
        {
            if (pawns.Count == 1)
                sb.Append($"{shortName} short monologue");
            else if (pawns[0].IsInCombat() || pawns[0].GetMapRole() == MapRole.Invading)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !pawns[0].InMentalState)
                {
                    talkRequest.Prompt = null;
                }

                talkRequest.TalkType = TalkType.Urgent;
                if (pawns[0].IsSlave || pawns[0].IsPrisoner)
                    sb.Append($"{shortName} dialogue short (worry)");
                else
                    sb.Append(
                        $"{shortName} dialogue short, urgent tone ({pawns[0].GetMapRole().ToString().ToLower()}/command)");
            }
            else
            {
                sb.Append($"{shortName} starts conversation, taking turns");
            }

            if (pawns[0].InMentalState)
                sb.Append($"\nbe dramatic (mental break)");
            else if (pawns[0].Downed)
                sb.Append($"\n(downed in pain. Short, strained dialogue)");
            else
                sb.Append($"\n{talkRequest.Prompt}");
        }


        // add pawn status
        sb.Append($"\n{status}");

        string locationStatus = GetPawnLocationStatus(pawns[0]);
        if (!string.IsNullOrEmpty(locationStatus))
            sb.Append($"\nLocation: {locationStatus}");

        // add time
        sb.Append($"\nTime: {gameData.Hour12HString}");

        // add date
        sb.Append($"\nToday: {gameData.DateString}");

        // add season
        sb.Append($"\nSeason: {gameData.SeasonString}");

        // add weather
        sb.Append($"\nWeather: {gameData.WeatherString}");

        // add language assurance
        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();
    }

    public static string GetPawnLocationStatus(Pawn pawn)
    {
        if (pawn == null || pawn.Map == null || pawn.Position == IntVec3.Invalid)
            return null;

        Room room = pawn.GetRoom();
        if (room != null && !room.PsychologicallyOutdoors)
            return "Indoors".Translate();
        return "Outdoors".Translate();
    }

    public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
    {
        var thoughts = new List<Thought>();
        pawn?.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

        return thoughts
            .GroupBy(t => t.def.defName)
            .ToDictionary(g => g.First(), g => g.Sum(t => t.MoodOffset()));
    }

    private static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }
}