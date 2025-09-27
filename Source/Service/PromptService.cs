using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Service;

public static class PromptService
{
    public enum InfoLevel
    {
        Short, Normal, Full
    }
    public static string BuildContext(List<Pawn> pawns)
    {
        // build context with 2 other nearby pawns
        List<Pawn> nearbyPawns = PawnSelector.GetNearByTalkablePawns(pawns[0]);

        // Create a new list with nearby pawns
        List<Pawn> mergedPawns = pawns.Concat(nearbyPawns).Distinct().Take(3).ToList();
            
        StringBuilder context = new StringBuilder();
        var instruction = Regex.Replace(Constant.Instruction, @"\r\n", "\n");
        instruction = Regex.Replace(instruction, @"  +", " ");

        context.AppendLine(instruction).AppendLine();

        int count = 0;
        foreach (Pawn pawn in mergedPawns)
        {
            // Main pawn gets more detail, others get basic info
            InfoLevel infoLevel = pawn == pawns[0] ? InfoLevel.Normal : InfoLevel.Short;
            string pawnContext = CreatePawnContext(pawn, infoLevel);
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
        var title = pawn.story.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = $"Role: {PawnService.GetRole(pawn)}";
        sb.AppendLine(role);

        if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
        {
            var xenotypeInfo = $"Race: {pawn.genes.Xenotype.LabelCap}";
            if (!pawn.genes.Xenotype.descriptionShort.NullOrEmpty())
                xenotypeInfo += $" - {pawn.genes.Xenotype.descriptionShort}";
            sb.AppendLine(xenotypeInfo);
        }

        if (infoLevel != InfoLevel.Short)
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

        //// INVADER STOP
        if (PawnService.IsInvader(pawn))
            return sb.ToString();

        if (pawn.story.Childhood != null)
        {
            var childHood =
                $"Childhood: {pawn.story.Childhood.title}({pawn.story.Childhood.titleShort})";
            if (infoLevel == InfoLevel.Full) childHood += $":{Sanitize(pawn.story.Childhood.description, pawn)}";
            sb.AppendLine(childHood);
        }

        if (pawn.story.Adulthood != null)
        {
            var adulthood =
                $"Adulthood: {pawn.story.Adulthood.title}({pawn.story.Adulthood.titleShort})";
            if (infoLevel == InfoLevel.Full) adulthood += $":{Sanitize(pawn.story.Adulthood.description, pawn)}";
            sb.AppendLine(adulthood);
        }

        var traits = "Traits: ";
        foreach (Trait trait in pawn.story.traits.TraitsSorted)
        {
            foreach (TraitDegreeData degreeData in trait.def.degreeDatas)
            {
                if (degreeData.degree == trait.Degree)
                {
                    traits += degreeData.label + (infoLevel == InfoLevel.Full ? $":{Sanitize(degreeData.description, pawn)}\n" : ",");
                    break;
                }
            }
        }

        sb.AppendLine(traits);

        if (infoLevel != InfoLevel.Short)
        {
            var skills = "Skills: ";
            foreach (SkillRecord skillRecord in pawn.skills.skills)
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
        IEnumerable<Hediff> hediffs = (IEnumerable<Hediff>)method.Invoke(null, new object[] { pawn, false });

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
        if (PawnService.IsInvader(pawn))
            return sb.ToString();

        var m = pawn.needs?.mood;
        var mood = $"Mood: {m?.MoodString ?? "N/A"} ({(int)((m?.CurLevelPercentage ?? 0) * 100)})";
            
        sb.AppendLine(mood);

        var thoughts = "Memory: ";
        foreach (Thought thought in PawnService.GetThoughts(pawn).Keys)
        {
            thoughts += $"{Sanitize(thought.LabelCap)}, ";
        }

        sb.AppendLine(thoughts);

        //// VISITOR STOP
        if (PawnService.IsVisitor(pawn))
            return sb.ToString();

        sb.AppendLine(RelationsService.GetRelationsString(pawn));

        if (infoLevel != InfoLevel.Short)
        {
            var equipment = "Equipment: ";
            if (pawn.equipment?.Primary != null)
                equipment += $"Weapon: {pawn.equipment.Primary.LabelCap}, ";

            var wornApparel = pawn.apparel?.WornApparel;
            var apparelLabels =
                wornApparel != null ? wornApparel.Select(a => a.LabelCap) : Enumerable.Empty<string>();

            if (apparelLabels.Any())
            {
                equipment += $"Apparel: {string.Join(", ", apparelLabels)}";
            }

            if (equipment != "Equipment: ")
                sb.AppendLine(equipment);
        }

        return sb.ToString();
    }

        public static string DecoratePrompt(string prompt, Pawn pawn1, Pawn pawn2, string status)
        {
            var sb = new StringBuilder();
            CommonUtil.InGameData gameData = CommonUtil.GetInGameData();
            
            // add pawn names
            sb.Append(pawn1.LabelShort);
            if (pawn2 != null)
            {
                sb.Append($" and {pawn2.LabelShort}");
            }
            sb.Append(": ");
            
            // add prompt
            sb.Append(prompt);
            
            // add pawn status
            sb.Append($"\n{status}");
            
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

            return sb.ToString();
        }
        
    private static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }
}