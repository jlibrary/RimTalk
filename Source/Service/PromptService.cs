using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
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

        // Each section applies hooks via AppendWithHook
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Race, ContextBuilder.GetRaceContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short && !pawn.IsVisitor() && !pawn.IsEnemy())
            AppendWithHook(sb, pawn, ContextCategories.Pawn.Genes, ContextBuilder.GetNotableGenesContext(pawn, infoLevel));
        
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Ideology, ContextBuilder.GetIdeologyContext(pawn, infoLevel));

        // Stop here for invaders and visitors
        if ((pawn.IsEnemy() || pawn.IsVisitor()) && !pawn.IsQuestLodger())
            return sb.ToString();

        AppendWithHook(sb, pawn, ContextCategories.Pawn.Backstory, ContextBuilder.GetBackstoryContext(pawn, infoLevel));
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Traits, ContextBuilder.GetTraitsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendWithHook(sb, pawn, ContextCategories.Pawn.Skills, ContextBuilder.GetSkillsContext(pawn, infoLevel));

        return sb.ToString();
    }

    /// <summary>Creates the full pawn context.</summary>
    public static string CreatePawnContext(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        sb.Append(CreatePawnBackstory(pawn, infoLevel));

        // Each section applies hooks via AppendWithHook
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Health, ContextBuilder.GetHealthContext(pawn, infoLevel));

        var personality = Cache.Get(pawn).Personality;
        if (personality != null)
            sb.AppendLine($"Personality: {personality}");

        // Stop here for invaders
        if (pawn.IsEnemy())
            return sb.ToString();

        AppendWithHook(sb, pawn, ContextCategories.Pawn.Mood, ContextBuilder.GetMoodContext(pawn, infoLevel));
        AppendWithHook(sb, pawn, ContextCategories.Pawn.Thoughts, ContextBuilder.GetThoughtsContext(pawn, infoLevel));
        AppendWithHook(sb, pawn, ContextCategories.Pawn.CaptiveStatus, ContextBuilder.GetPrisonerSlaveContext(pawn, infoLevel));
        
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

        AppendWithHook(sb, pawn, ContextCategories.Pawn.Social, ContextBuilder.GetRelationsContext(pawn, infoLevel));
        
        if (infoLevel != InfoLevel.Short)
            AppendWithHook(sb, pawn, ContextCategories.Pawn.Equipment, ContextBuilder.GetEquipmentContext(pawn, infoLevel));

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

        // Time and weather (apply environment hooks with injections)
        if (contextSettings.IncludeTime)
            sb.Append($"\nTime: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Time, gameData.Hour12HString)}");
        if (contextSettings.IncludeDate)
            sb.Append($"\nToday: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Date, gameData.DateString)}");
        if (contextSettings.IncludeSeason)
            sb.Append($"\nSeason: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Season, gameData.SeasonString)}");
        if (contextSettings.IncludeWeather)
            sb.Append($"\nWeather: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Weather, gameData.WeatherString)}");

        // Location
        ContextBuilder.BuildLocationContext(sb, contextSettings, mainPawn);

        // Environment
        ContextBuilder.BuildEnvironmentContext(sb, contextSettings, mainPawn);

        if (contextSettings.IncludeWealth)
            sb.Append($"\nWealth: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Wealth, Describer.Wealth(mainPawn.Map.wealthWatcher.WealthTotal))}");

        if (AIService.IsFirstInstruction())
            sb.Append($"\nin {Constant.Lang}");

        talkRequest.Prompt = sb.ToString();
    }
    
    /// <summary>
    /// Appends text to StringBuilder if not empty, with optional hook application.
    /// </summary>
    private static void AppendIfNotEmpty(StringBuilder sb, string text)
    {
        if (!string.IsNullOrEmpty(text))
            sb.AppendLine(text);
    }
    
    /// <summary>
    /// Appends pawn context text with hook and injection application.
    /// </summary>
    private static void AppendWithHook(StringBuilder sb, Pawn pawn, ContextCategory category, string text)
    {
        // Render Before injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.Before && provider is Func<Pawn, string> p)
                    AppendIfNotEmpty(sb, p(pawn));
        
        // Apply hooks (always call to allow Override hooks on empty categories)
        var hooked = ContextHookRegistry.ApplyPawnHooks(category, pawn, text ?? "");
        AppendIfNotEmpty(sb, hooked);
        
        // Render After injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.After && provider is Func<Pawn, string> p)
                    AppendIfNotEmpty(sb, p(pawn));
    }
    
    /// <summary>
    /// Appends environment context text with hook and injection application.
    /// </summary>
    private static string ApplyEnvironmentWithHook(Map map, ContextCategory category, string text)
    {
        var sb = new StringBuilder();
        
        // Render Before injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.Before && provider is Func<Map, string> p)
                    AppendIfNotEmpty(sb, p(map));
        
        // Apply hooks
        var hooked = ContextHookRegistry.ApplyEnvironmentHooks(category, map, text ?? "");
        AppendIfNotEmpty(sb, hooked);
        
        // Render After injections
        if (ContextHookRegistry.HasAnyInjections)
            foreach (var (_, pos, _, provider) in ContextHookRegistry.GetInjectedSectionsAt(category))
                if (pos == ContextHookRegistry.InjectPosition.After && provider is Func<Map, string> p)
                    AppendIfNotEmpty(sb, p(map));
        
        return sb.ToString().TrimEnd();
    }
}