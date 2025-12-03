using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

public static class PromptService
{
    private static readonly MethodInfo VisibleHediffsMethod = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");
    public enum InfoLevel { Short, Normal, Full }

    public static string BuildContext(List<Pawn> pawns)
    {
        var context = new StringBuilder();
        context.AppendLine(Constant.Instruction).AppendLine();

        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;
            var pawnContext = CreatePawnContext(pawn, i == 0 ? InfoLevel.Normal : InfoLevel.Short);

            Cache.Get(pawn).Context = pawnContext;
            context.AppendLine()
                   .AppendLine($"[Person {i + 1} START]")
                   .AppendLine(pawnContext)
                   .AppendLine($"[Person {i + 1} END]");
        }

        return context.ToString();
    }

    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var name = pawn.LabelShort;
        var title = pawn.story?.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = pawn.GetRole(true);
        if (role != null)
            sb.AppendLine($"Role: {role}");

        if (contextSettings.IncludeRace && ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
            sb.AppendLine($"Race: {pawn.genes.Xenotype.LabelCap}");


        // Notable genes (Normal/Full only, not for enemies/visitors)
        if (contextSettings.IncludeNotableGenes && infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy() && 
            ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
        {
            var notableGenes = pawn.genes.GenesListForReading
                .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                .Select(g => g.def.LabelCap);

            if (notableGenes.Any())
                sb.AppendLine($"Notable Genes: {string.Join(", ", notableGenes)}");
        }

        // Ideology
        if (contextSettings.IncludeIdeology && ModsConfig.IdeologyActive && pawn.ideo?.Ideo != null)
        {
            var ideo = pawn.ideo.Ideo;
            sb.AppendLine($"Ideology: {ideo.name}");

            var memes = ideo.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
                sb.AppendLine($"Memes: {string.Join(", ", memes)}");
        }

        //// INVADER AND VISITOR STOP
        if (pawn.IsEnemy() || pawn.IsVisitor())
            return sb.ToString();

        // Backstory
        if (contextSettings.IncludeBackstory)
        {
            if (pawn.story?.Childhood != null)
                sb.AppendLine(ContextHelper.FormatBackstory("Childhood", pawn.story.Childhood, pawn, infoLevel));

            if (pawn.story?.Adulthood != null)
                sb.AppendLine(ContextHelper.FormatBackstory("Adulthood", pawn.story.Adulthood, pawn, infoLevel));
        }

        // Traits
        if (contextSettings.IncludeTraits)
        {
            var traits = new List<string>();
            foreach (var trait in pawn.story?.traits?.TraitsSorted ?? Enumerable.Empty<Trait>())
            {
                var degreeData = trait.def.degreeDatas.FirstOrDefault(d => d.degree == trait.Degree);
                if (degreeData != null)
                {
                    var traitText = infoLevel == InfoLevel.Full
                        ? $"{degreeData.label}:{ContextHelper.Sanitize(degreeData.description, pawn)}"
                        : degreeData.label;
                    traits.Add(traitText);
                }
            }

            if (traits.Any())
            {
                var separator = infoLevel == InfoLevel.Full ? "\n" : ",";
                sb.AppendLine($"Traits: {string.Join(separator, traits)}");
            }
        }

        // Skills
        if (contextSettings.IncludeSkills && infoLevel != InfoLevel.Short)
        {
            var skills = pawn.skills?.skills?.Select(s => $"{s.def.label}: {s.Level}");
            if (skills?.Any() == true)
                sb.AppendLine($"Skills: {string.Join(", ", skills)}");
        }

        return sb.ToString();
    }

    private static string CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Health
        if (contextSettings.IncludeHealth)
        {
            var hediffs = (IEnumerable<Hediff>)VisibleHediffsMethod.Invoke(null, [pawn, false]);
            var healthInfo = string.Join(",", hediffs
                .GroupBy(h => h.def)
                .Select(g => $"{g.Key.label}({string.Join(",", g.Select(h => h.Part?.Label ?? ""))})"));

            if (!string.IsNullOrEmpty(healthInfo))
                sb.AppendLine($"Health: {healthInfo}");
        }

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        //// INVADER STOP
        if (pawn.IsEnemy())
            return sb.ToString();

        // Mood
        if (contextSettings.IncludeMood)
        {
            var m = pawn.needs?.mood;
            if (m?.MoodString != null)
            {
                string mood = pawn.Downed && !pawn.IsBaby()
                    ? "Critical: Downed (in pain/distress)"
                    : pawn.InMentalState
                        ? $"Mood: {pawn.MentalState?.InspectLine} (in mental break)"
                        : $"Mood: {m.MoodString} ({(int)(m.CurLevelPercentage * 100)}%)";
                sb.AppendLine(mood);
            }
        }

        // Thoughts
        if (contextSettings.IncludeThoughts)
        {
            var thoughts = ContextHelper.GetThoughts(pawn).Keys.Select(t => ContextHelper.Sanitize(t.LabelCap));
            if (thoughts.Any())
                sb.AppendLine($"Memory: {string.Join(", ", thoughts)}");
        }

        if (contextSettings.IncludePrisonerSlaveStatus && (pawn.IsSlave || pawn.IsPrisoner))
            sb.AppendLine(pawn.GetPrisonerSlaveStatus());

        // Visitor activity
        if (pawn.IsVisitor())
        {
            var lord = pawn.GetLord() ?? pawn.CurJob?.lord;
            if (lord?.LordJob != null)
            {
                var cleanName = lord.LordJob.GetType().Name.Replace("LordJob_", "");
                sb.AppendLine($"Activity: {cleanName}");
            }
        }

        if (contextSettings.IncludeRelations)
            sb.AppendLine(RelationsService.GetRelationsString(pawn));

        // Equipment
        if (contextSettings.IncludeEquipment && infoLevel != InfoLevel.Short)
        {
            var equipment = new List<string>();
            if (pawn.equipment?.Primary != null)
                equipment.Add($"Weapon: {pawn.equipment.Primary.LabelCap}");

            var apparelLabels = pawn.apparel?.WornApparel?.Select(a => a.LabelCap);
            if (apparelLabels?.Any() == true)
                equipment.Add($"Apparel: {string.Join(", ", apparelLabels)}");

            if (equipment.Any())
                sb.AppendLine($"Equipment: {string.Join(", ", equipment)}");
        }

        return sb.ToString();
    }

    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();
        var mainPawn = pawns[0];
        var shortName = $"{mainPawn.LabelShort}";

        // Dialogue type
        if (talkRequest.TalkType == TalkType.User)
        {
            sb.Append($"{pawns[1].LabelShort}({pawns[1].GetRole()}) said to '{shortName}: {talkRequest.Prompt}'.");
            if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.Manual)
                sb.Append($"Generate dialogue starting after this. Do not generate any further lines for {pawns[1].LabelShort}");
            else if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.AIDriven)
                sb.Append($"Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {mainPawn.LabelShort}");
        }
        else
        {
            if (pawns.Count == 1)
            {
                sb.Append($"{shortName} short monologue");
            }
            else if (mainPawn.IsInCombat() || mainPawn.GetMapRole() == MapRole.Invading)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !mainPawn.InMentalState)
                    talkRequest.Prompt = null;

                talkRequest.TalkType = TalkType.Urgent;
                sb.Append(mainPawn.IsSlave || mainPawn.IsPrisoner
                    ? $"{shortName} dialogue short (worry)"
                    : $"{shortName} dialogue short, urgent tone ({mainPawn.GetMapRole().ToString().ToLower()}/command)");
            }
            else
            {
                sb.Append($"{shortName} starts conversation, taking turns");
            }

            // Modifiers
            if (mainPawn.InMentalState)
                sb.Append("\nbe dramatic (mental break)");
            else if (mainPawn.Downed && !mainPawn.IsBaby())
                sb.Append("\n(downed in pain. Short, strained dialogue)");
            else
                sb.Append($"\n{talkRequest.Prompt}");
        }

        // Time and weather
        sb.Append($"\n{status}");
        if (contextSettings.IncludeTimeAndDate)
        {
            sb.Append($"\nTime: {gameData.Hour12HString}");
            sb.Append($"\nToday: {gameData.DateString}");
        }
        if (contextSettings.IncludeSeason)
            sb.Append($"\nSeason: {gameData.SeasonString}");
        if (contextSettings.IncludeWeather)
            sb.Append($"\nWeather: {gameData.WeatherString}");

        // Location
        if (contextSettings.IncludeLocationAndTemperature)
        {
            var locationStatus = ContextHelper.GetPawnLocationStatus(mainPawn);
            if (!string.IsNullOrEmpty(locationStatus))
            {
                var temperature = Mathf.RoundToInt(mainPawn.Position.GetTemperature(mainPawn.Map));
                var room = mainPawn.GetRoom();
                var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "Room" : "";

                sb.Append(string.IsNullOrEmpty(roomRole)
                    ? $"\nLocation: {locationStatus};{temperature}C"
                    : $"\nLocation: {locationStatus};{temperature}C;{roomRole}");
            }
        }

        // Environment
        if (contextSettings.IncludeTerrain)
        {
            var terrain = mainPawn.Position.GetTerrain(mainPawn.Map);
            if (terrain != null)
                sb.Append($"\nTerrain: {terrain.LabelCap}");
        }

        if (contextSettings.IncludeBeauty)
        {
            var nearbyCells = ContextHelper.GetNearbyCells(mainPawn);
            if (nearbyCells.Count > 0)
            {
                var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, mainPawn.Map));
                sb.Append($"\nCellBeauty: {Describer.Beauty(beautySum / nearbyCells.Count)}");
            }
        }

        var pawnRoom = mainPawn.GetRoom();
        if (contextSettings.IncludeCleanliness && pawnRoom is { PsychologicallyOutdoors: false })
            sb.Append($"\nCleanliness: {Describer.Cleanliness(pawnRoom.GetStat(RoomStatDefOf.Cleanliness))}");

        // Surroundings
        if (contextSettings.IncludeSurroundings)
        {
            var items = ContextHelper.CollectNearbyItems(mainPawn, 3);
            if (items.Any())
            {
                var grouped = items.GroupBy(i => i).Select(g => g.Count() > 1 ? $"{g.Key} x {g.Count()}" : g.Key);
                sb.Append($"\nSurroundings: {string.Join(", ", grouped)}");
            }
        }

        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal)}");

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();
    }
}