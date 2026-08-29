using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Disambiguates duplicate pawn names in dialogue using Last Name or stable index among talk-eligible pawns.</summary>
    public static string GetUniqueName(Pawn pawn, List<Pawn> pawns = null)
    {
        if (pawn == null) return string.Empty;
        var pool = pawn.Map?.mapPawns?.AllPawnsSpawned ?? pawns ?? Find.CurrentMap?.mapPawns?.AllPawnsSpawned;
        if (pool == null) return pawn.LabelShort;

        int dupIndex = 0, dupCount = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            var p = pool[i];
            if (p == null || p.LabelShort != pawn.LabelShort || !p.IsTalkEligible()) continue;
            dupCount++;
            if (p.thingIDNumber <= pawn.thingIDNumber) dupIndex++;
        }

        if (dupCount <= 1) return pawn.LabelShort;
        if (pawn.Name is NameTriple t && !string.IsNullOrEmpty(t.Last))
        {
            var fullName = $"{pawn.LabelShort} {t.Last}";
            int nameDupCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                var p = pool[i];
                if (p != null && p.IsTalkEligible() && p.Name is NameTriple pt && $"{p.LabelShort} {pt.Last}" == fullName)
                    nameDupCount++;
            }
            if (nameDupCount <= 1) return fullName;
        }
        return $"{pawn.LabelShort} {dupIndex}";
    }

    public static string BuildContext(List<Pawn> pawns, bool isAnnouncement = false)
    {
        var context = new StringBuilder();
    
        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            var displayName = GetUniqueName(pawn, pawns);
            bool hasUniqueAlias = displayName != pawn.LabelShort;

            if (pawn.IsPlayer())
            {
                if (Settings.Get().PlayerDialogueMode == Settings.PlayerDialogueMode.AIDriven)
                {
                    var playerPersona = Settings.Get().PlayerPersona;
                    if (!string.IsNullOrWhiteSpace(playerPersona))
                    {
                        string playerContext = $"{displayName} (Player)\nPersonality: {playerPersona.Trim()}";
                        var playerState = Cache.Get(pawn);
                        if (playerState != null) playerState.Context = playerContext;
                        context.AppendLine($"[P{i + 1}]").AppendLine(playerContext);
                    }
                }
                continue;
            }

            if (isAnnouncement && i > 0)
            {
                var minimalContext = CreateMinimalListenerContext(pawn);
                // If duplicate names exist, replace leading LabelShort with unique display name
                if (hasUniqueAlias && !string.IsNullOrEmpty(pawn.LabelShort) && minimalContext.StartsWith(pawn.LabelShort))
                    minimalContext = displayName + minimalContext.Substring(pawn.LabelShort.Length);

                var pawnState = Cache.Get(pawn);
                if (pawnState != null) pawnState.Context = minimalContext;
                context.AppendLine($"[P{i + 1}]").AppendLine(minimalContext);
                continue;
            }

            InfoLevel infoLevel = Settings.Get().Context.EnableContextOptimization 
                                  || i != 0 ? InfoLevel.Short : InfoLevel.Normal;
            var pawnContext = CreatePawnContext(pawn, infoLevel);
            
            // Preserve Harmony patches on CreatePawnContext while safely replacing leading LabelShort with unique alias
            if (hasUniqueAlias && !string.IsNullOrEmpty(pawn.LabelShort) && pawnContext.StartsWith(pawn.LabelShort))
                pawnContext = displayName + pawnContext.Substring(pawn.LabelShort.Length);

            pawnContext = CommonUtil.StripFormattingTags(pawnContext);

            Cache.Get(pawn).Context = pawnContext;
            context.AppendLine($"[P{i + 1}]").AppendLine(pawnContext);
        }

        if (pawns.Count > 0 && pawns[0] != null)
        {
            var envContext = BuildEnvironmentContextString(pawns[0]);
            if (!string.IsNullOrWhiteSpace(envContext))
            {
                if (context.Length > 0 && !context.ToString().EndsWith("\n\n"))
                    context.AppendLine();
                context.AppendLine("[Environment]").AppendLine(envContext);
            }

            var eventsContext = BuildEventsContextString(pawns[0]);
            if (!string.IsNullOrWhiteSpace(eventsContext))
            {
                if (context.Length > 0 && !context.ToString().EndsWith("\n\n"))
                    context.AppendLine();
                context.AppendLine("[Events]").AppendLine(eventsContext);
            }
        }

        return context.ToString().TrimEnd();
    }

    /// <summary>Creates the events context section (Active Letters and Notifications).</summary>
    public static string BuildEventsContextString(Pawn mainPawn)
    {
        if (mainPawn == null || mainPawn.Map == null || mainPawn.IsEnemy()) return string.Empty;

        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeEvents || contextSettings.MaxEventsCount <= 0) return string.Empty;

        var events = ContextBuilder.GetEventsContext(mainPawn.Map, PromptService.InfoLevel.Normal);
        if (string.IsNullOrEmpty(events)) return string.Empty;

        return ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Events, events);
    }

    /// <summary>Creates the environment context section (Time, Date, Season, Weather, Location, Environment, Wealth).</summary>
    public static string BuildEnvironmentContextString(Pawn mainPawn)
    {
        if (mainPawn == null || mainPawn.Map == null) return string.Empty;

        var contextSettings = Settings.Get().Context;
        var sb = new StringBuilder();
        var gameData = CommonUtil.GetInGameData();

        // Time and weather (apply environment hooks with injections)
        if (contextSettings.IncludeTime)
            sb.Append($"Time: {ApplyEnvironmentWithHook(mainPawn.Map, ContextCategories.Environment.Time, gameData.Hour12HString)}");
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

        return sb.ToString().Trim();
    }

    /// <summary>Creates a token-efficient 1-line profile for announcement listeners using native Traits, Role, and Mood.</summary>
    public static string CreateMinimalListenerContext(Pawn pawn)
    {
        var role = pawn.GetRole(false) ?? "Colonist";
        var traits = pawn.story?.traits?.TraitsSorted?
            .Select(t => t.LabelCap.ToString())
            .Where(l => !string.IsNullOrEmpty(l));
        var traitsStr = traits != null && traits.Any() ? $", Traits: {string.Join(", ", traits)}" : "";
        var mood = pawn.needs?.mood?.MoodString;
        var moodStr = !string.IsNullOrEmpty(mood) ? $" | Mood: {mood}" : "";
        return $"{pawn.LabelShort} ({role}{traitsStr}){moodStr}";
    }

    /// <summary>Creates the basic pawn backstory section.</summary>
    public static string CreatePawnBackstory(Pawn pawn, InfoLevel infoLevel = InfoLevel.Normal)
    {
        var sb = new StringBuilder();
        var name = pawn.LabelShort;
        var pawnTitle = pawn.GetTitle();
        var title = string.IsNullOrWhiteSpace(pawnTitle) ? "" : $" ({pawnTitle})";
        var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "").Trim();
        sb.AppendLine($"{name}{title} ({genderAndAge})");

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

        var personality = Cache.Get(pawn)?.Personality;
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

    /// <summary>Decorates the prompt with dialogue type and status.</summary>
    public static void DecoratePrompt(TalkRequest talkRequest, List<Pawn> pawns, string status)
    {
        var sb = new StringBuilder();
        var mainPawn = pawns[0];
        var shortName = GetUniqueName(mainPawn, pawns);

        // Dialogue type
        ContextBuilder.BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn);
        sb.Append($"\n{status}");

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
