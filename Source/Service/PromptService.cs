using System;
using System.Collections.Generic;
using System.Linq;
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

            Data.Cache.Get(pawn).Context = pawnContext;
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

        var role = pawn.GetRole(true);
        if (role != null)
            sb.AppendLine($"Role: {role}");

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

        var traits = "";
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

        if (traits != "")
            sb.AppendLine($"Traits: {traits}");

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

        var personality = Data.Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        //// INVADER STOP
        if (pawn.IsEnemy())
            return sb.ToString();

        var m = pawn.needs?.mood;
        if (m?.MoodString != null)
        {
            var mood = pawn.Downed && !pawn.IsBaby()
                ? "Critical: Downed (in pain/distress)"
                : pawn.InMentalState
                    ? $"Mood: {pawn.MentalState?.InspectLine} (in mental break)"
                    : $"Mood: {m.MoodString} ({(int)(m.CurLevelPercentage * 100)}%)";
            sb.AppendLine(mood);
        }

        var thoughts = "";
        foreach (Thought thought in GetThoughts(pawn).Keys)
        {
            thoughts += $"{Sanitize(thought.LabelCap)}, ";
        }

        if (thoughts != "")
            sb.AppendLine($"Memory: {thoughts}");

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
            var equipments = "";
            if (pawn.equipment?.Primary != null)
                equipments += $"Weapon: {pawn.equipment.Primary.LabelCap}, ";

            var wornApparel = pawn.apparel?.WornApparel;
            var apparelLabels =
                wornApparel != null ? wornApparel.Select(a => a.LabelCap) : [];

            if (apparelLabels.Any())
            {
                equipments += $"Apparel: {string.Join(", ", apparelLabels)}";
            }

            if (equipments != "")
                sb.AppendLine($"Equipments: {equipments}");
        }

        return sb.ToString();
    }

    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var sb = new StringBuilder();
        CommonUtil.InGameData gameData = CommonUtil.GetInGameData();

        string shortName = $"{pawns[0].LabelShort}({pawns[0].GetRole()})";
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
            else if (pawns[0].Downed && !pawns[0].IsBaby())
                sb.Append($"\n(downed in pain. Short, strained dialogue)");
            else
                sb.Append($"\n{talkRequest.Prompt}");
        }

        sb.Append($"\n{status}");
        sb.Append($"\nTime: {gameData.Hour12HString}");
        sb.Append($"\nToday: {gameData.DateString}");
        sb.Append($"\nSeason: {gameData.SeasonString}");
        sb.Append($"\nWeather: {gameData.WeatherString}");

        Pawn pawn = pawns[0];
        Map map = pawn.Map;

        string locationStatus = GetPawnLocationStatus(pawn);
        if (!string.IsNullOrEmpty(locationStatus))
        {
            int currentTemperature = Mathf.RoundToInt(pawn.Position.GetTemperature(map));
            string roomRole = string.Empty;
            Room room = pawn.GetRoom();
            if (room != null && !room.PsychologicallyOutdoors)
                roomRole = room.Role?.label ?? "Room";

            if (string.IsNullOrEmpty(roomRole))
                sb.Append($"\nLocation: {locationStatus};{currentTemperature}C");
            else
                sb.Append($"\nLocation: {locationStatus};{currentTemperature}C;{roomRole}");
        }

        TerrainDef terrain = pawn.Position.GetTerrain(map);
        if (terrain != null)
            sb.Append($"\nFloor: {terrain.LabelCap}");

        Building wall = FindWallInFrontAndBack(pawn, 8);
        if (wall != null)
            sb.Append($"\nWall: {wall.LabelCap}");

        List<IntVec3> nearbyCells = new();
        IntVec3 facing = pawn.Rotation.FacingCell;
        for (int i = 1; i <= 5; i++)
        {
            IntVec3 targetCell = pawn.Position + facing * i;
            for (int offset = -1; offset <= 1; offset++)
            {
                IntVec3 c = new IntVec3(targetCell.x + offset, targetCell.y, targetCell.z);
                if (c.InBounds(map))
                    nearbyCells.Add(c);
            }
        }

        float beautySum = 0f;
        int cellCount = nearbyCells.Count;
        if (cellCount > 0)
        {
            foreach (IntVec3 c in nearbyCells)
                beautySum += BeautyUtility.CellBeauty(c, map);
            string beautyDescription = GetBeautyDescription(beautySum / cellCount);
            sb.Append("\nbeauty: " + beautyDescription);
        }

        Room pawnRoom = pawn.GetRoom();
        if (pawnRoom != null && !pawnRoom.PsychologicallyOutdoors)
        {
            string cleanliness = GetCleanlinessDescription(pawnRoom.GetStat(RoomStatDefOf.Cleanliness));
            sb.Append("\nclean " + cleanliness);
        }

        List<string> items = new();
        HashSet<Thing> seenThings = new();
        foreach (IntVec3 c in nearbyCells.OrderBy(_ => Rand.Value).ToList())
        {
            if (items.Count >= 3)
                break;

            List<Thing> thingsHere = c.GetThingList(pawn.Map);
            if (thingsHere == null || thingsHere.Count == 0)
                continue;

            bool hasPawnOrAnimal = false;
            HashSet<ThingDef> thingDefs = new();
            foreach (Thing thing in thingsHere)
            {
                if (thing != null && thing.def != null && thingDefs.Add(thing.def) &&
                    thing.def.category != ThingCategory.Building && thing.def.category != ThingCategory.Plant &&
                    thing.def.category != ThingCategory.Item && !thing.def.IsFilth)
                {
                    hasPawnOrAnimal = true;
                    break;
                }
            }

            if (hasPawnOrAnimal)
                continue;

            List<Thing> candidateThings = new();
            HashSet<ThingCategory> categories = new();
            foreach (Thing thing in thingsHere)
            {
                if (thing == null || thing.def == null)
                    continue;

                bool isValidCategory = thing.def.category == ThingCategory.Building ||
                                       thing.def.category == ThingCategory.Plant ||
                                       thing.def.category == ThingCategory.Item ||
                                       thing.def.IsFilth;

                if (!isValidCategory)
                    continue;

                if (thing.def.category == ThingCategory.Building && IsWall(thing))
                    continue;

                if (categories.Add(thing.def.category))
                    candidateThings.Add(thing);
            }

            if (candidateThings.Count == 0)
                continue;

            Thing picked = candidateThings.RandomElement();
            if (seenThings.Contains(picked))
                continue;

            if (picked is Building_Storage storage)
            {
                List<Thing> stored = new();
                foreach (IntVec3 cell in storage.AllSlotCells())
                {
                    List<Thing> contents = cell.GetThingList(pawn.Map);
                    stored.AddRange(contents);
                }

                List<Thing> distinctStored = stored.Distinct().ToList();
                if (distinctStored.Count == 0)
                {
                    seenThings.Add(storage);
                }
                else
                {
                    string storedSample = string.Join(", ", distinctStored.OrderBy(_ => Rand.Value).Take(3).Select(i => i.LabelCap));
                    string storageLabel = $"{storage.LabelCap} ({storedSample})";
                    seenThings.Add(storage);
                    items.Add(storageLabel);
                }
            }
            else
            {
                seenThings.Add(picked);
                items.Add(picked.LabelCap);
            }
        }

        if (items.Count > 0)
            sb.Append("\nitems: " + string.Join(", ", items));

        float wealthTotal = pawn.Map.wealthWatcher.WealthTotal;
        sb.Append("\nwealth " + GetWealthDescription(wealthTotal));

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

    public static string GetDecoratedName(Pawn pawn)
    {
        if (!pawn.RaceProps.Humanlike)
            return $"{pawn.LabelShort}(Age:{pawn.ageTracker.AgeBiologicalYears};Race:{pawn.def.LabelCap})";
        return $"{pawn.LabelShort}(Age:{pawn.ageTracker.AgeBiologicalYears};{pawn.gender.GetLabel()};ID:{pawn.GetRole(true)};{GetRaceLabel(pawn)})";
    }

    private static string GetRaceLabel(Pawn pawn)
    {
        return ModsConfig.BiotechActive && pawn.genes?.Xenotype != null
            ? pawn.genes.Xenotype.LabelCap
            : pawn.def.LabelCap;
    }

    private static string GetBeautyDescription(float beauty)
    {
        return beauty switch
        {
            > 100f => "wondrously", 
            > 20f => "impressive",
            > 10f => "beautiful",
            > 5f => "decent",
            > -1f => "general",
            > -5f => "awful",
            > -20f => "very awful",
            _ => "disgusting"
        };
    }

    private static string GetCleanlinessDescription(float cleanliness)
    {
        return cleanliness switch
        {
            > 1.5f => "spotless",
            > 0.5f => "clean",
            > -0.5f => "neat",
            > -1.5f => "a bit dirty",
            > -2.5f => "dirty",
            > -5f => "very dirty",
            _ => "foul"
        };
    }

    private static Building FindWallInFrontAndBack(Pawn pawn, int range)
    {
        if (pawn.Map == null)
            return null;

        Rot4 forward = pawn.Rotation;
        List<IntVec3> cells = new();
        for (int i = 1; i <= range; i++)
            cells.Add(pawn.Position + forward.FacingCell * i);

        Rot4 backward = forward.Opposite;
        for (int i = 1; i <= range; i++)
            cells.Add(pawn.Position + backward.FacingCell * i);

        foreach (IntVec3 c in cells)
        {
            if (!c.InBounds(pawn.Map))
                continue;

            Building edifice = c.GetEdifice(pawn.Map);
            if (edifice != null && IsWall(edifice))
                return edifice;
        }

        return null;
    }

    private static bool IsWall(Thing thing)
    {
        GraphicData data = thing.def.graphicData;
        return data != null && data.linkFlags.HasFlag((Enum)LinkFlags.Wall);
    }

    private static string GetWealthDescription(float wealthTotal)
    {
        return wealthTotal switch
        {
            < 50_000f => "impecunious",
            < 100_000f => "needy",
            < 200_000f => "just rid of starving",
            < 300_000f => "moderately prosperous",
            < 400_000f => "rich",
            < 600_000f => "luxurious",
            < 1_000_000f => "extravagant",
            < 1_500_000f => "treasures fill the home",
            < 2_000_000f => "as rich as glitter world",
            _ => "richest in the galaxy"
        };
    }

    private static string Sanitize(string text, Pawn pawn = null)
    {
        if (pawn != null)
            text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).Resolve();
        return text.StripTags().RemoveLineBreaks();
    }
}
