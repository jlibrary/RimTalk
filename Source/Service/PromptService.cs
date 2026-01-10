using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RimTalk.Data;
using RimTalk.Util;
using Verse;
using Verse.AI.Group;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

/// <summary>
/// All public methods in this class are designed to be patchable with Harmony.
/// Use Prefix to replace functionality, Postfix to extend it.
/// </summary>
public static class PromptService
{
    public enum InfoLevel { Short, Normal, Full }

    public static string BuildContext(List<Pawn> pawns)
    {
        var context = new StringBuilder();
    
        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn.IsPlayer()) continue;
            InfoLevel infoLevel = Settings.Get().Context.EnableContextOptimization 
                                  || i != 0 ? InfoLevel.Short : InfoLevel.Normal;
            var pawnContext = CreatePawnContext(pawn, infoLevel);
            pawnContext = CommonUtil.StripFormattingTags(pawnContext);

            Cache.Get(pawn).Context = pawnContext;
            context.AppendLine($"[P{i + 1}]").AppendLine(pawnContext);
        }

        return context.ToString().TrimEnd();
    }

    /// <summary>Creates the basic pawn backstory section.</summary>
    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var name = pawn.LabelShort;
        var title = pawn.story?.title == null ? "" : $"({pawn.story.title})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "").Trim();
        sb.AppendLine($"{name} {title} ({genderAndAge})");

        var role = pawn.GetRole(true);
        if (role != null)
            sb.AppendLine($"Role: {role}");

        // Each section can be patched independently
        AppendIfNotEmpty(sb, ContextBuilder.GetRaceContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy())
            AppendIfNotEmpty(sb, ContextBuilder.GetNotableGenesContext(pawn, infoLevel));
        
        AppendIfNotEmpty(sb, ContextBuilder.GetIdeologyContext(pawn, infoLevel));

        // Stop here for invaders and visitors
        if (pawn.IsEnemy() || pawn.IsVisitor())
            return sb.ToString();

        AppendIfNotEmpty(sb, ContextBuilder.GetBackstoryContext(pawn, infoLevel));
        AppendIfNotEmpty(sb, ContextBuilder.GetTraitsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendIfNotEmpty(sb, ContextBuilder.GetSkillsContext(pawn, infoLevel));

        return sb.ToString();
    }

    /// <summary>Creates the full pawn context.</summary>
    public static string CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Each section can be patched independently
        AppendIfNotEmpty(sb, ContextBuilder.GetHealthContext(pawn, infoLevel));

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        // Stop here for invaders
        if (pawn.IsEnemy())
            return sb.ToString();

        AppendIfNotEmpty(sb, ContextBuilder.GetMoodContext(pawn, infoLevel));
        AppendIfNotEmpty(sb, ContextBuilder.GetThoughtsContext(pawn, infoLevel));
        AppendIfNotEmpty(sb, ContextBuilder.GetPrisonerSlaveContext(pawn, infoLevel));
        
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

        AppendIfNotEmpty(sb, ContextBuilder.GetRelationsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendIfNotEmpty(sb, ContextBuilder.GetEquipmentContext(pawn, infoLevel));

        return sb.ToString();
    }

    /// <summary>Decorates the prompt with dialogue type, time, weather, location, and environment.</summary>
    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();
        var mainPawn = pawns[0];
        var shortName = $"{mainPawn.LabelShort}";

        // Dialogue type
        ContextBuilder.BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn);
        sb.Append($"\n{status}");

        // Time and weather
        if (contextSettings.IncludeTime)
            sb.Append($"\nTime: {gameData.Hour12HString}");
        if (contextSettings.IncludeDate)
            sb.Append($"\nToday: {gameData.DateString}");
        if (contextSettings.IncludeSeason)
            sb.Append($"\nSeason: {gameData.SeasonString}");
        if (contextSettings.IncludeWeather)
            sb.Append($"\nWeather: {gameData.WeatherString}");

        // Location
        ContextBuilder.BuildLocationContext(sb, contextSettings, mainPawn);

        // Environment
        ContextBuilder.BuildEnvironmentContext(sb, contextSettings, mainPawn);

        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal)}");

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();
    }
    
    private static void AppendIfNotEmpty(StringBuilder sb, string text)
    {
        if (!string.IsNullOrEmpty(text))
            sb.AppendLine(text);
    }
}