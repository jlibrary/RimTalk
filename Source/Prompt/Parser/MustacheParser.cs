using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Logger = RimTalk.Util.Logger;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Prompt;

/// <summary>
/// Mustache syntax parser - parses and substitutes {{...}} syntax.
/// </summary>
public static class MustacheParser
{
    // Registered custom variable providers (mod extensions)
    private static readonly Dictionary<string, Func<MustacheContext, string>> CustomProviders = new();
    
    // Registered appenders for existing variables (mod extensions)
    // Each variable can have multiple appenders that modify the value in order
    private static readonly Dictionary<string, List<Func<MustacheContext, string, string>>> Appenders = new();
    
    // Regex to match {{...}}
    private static readonly Regex MustacheRegex = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);
    
    // Pawn index matching regex (pawn1, pawn2, pawn3...)
    private static readonly Regex PawnIndexRegex = new(@"^pawn(\d+)\.(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses and substitutes mustache syntax.
    /// </summary>
    /// <param name="template">Template string containing mustache syntax</param>
    /// <param name="context">Parse context (containing current pawn, etc.)</param>
    /// <returns>Substituted string</returns>
    public static string Parse(string template, MustacheContext context)
    {
        if (string.IsNullOrEmpty(template)) return "";
        if (context == null) context = new MustacheContext();
        
        try
        {
            return MustacheRegex.Replace(template, match =>
            {
                var expression = match.Groups[1].Value.Trim();
                return EvaluateExpression(expression, context);
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"MustacheParser.Parse failed: {ex.Message}");
            return template; // Return original on error
        }
    }

    /// <summary>
    /// Evaluates a single expression.
    /// </summary>
    private static string EvaluateExpression(string expression, MustacheContext context)
    {
        if (string.IsNullOrEmpty(expression)) return "";
        
        // Parse expression type (using :: as delimiter)
        var parts = expression.Split(new[] { "::" }, StringSplitOptions.None);
        var command = parts[0].ToLowerInvariant().Trim();
        
        return command switch
        {
            "setvar" => HandleSetVar(parts, context),
            "getvar" => HandleGetVar(parts, context),
            _ => HandleCustomOrBuiltin(expression, context)
        };
    }

    /// <summary>
    /// Handles setvar command: {{setvar::key::value}}
    /// </summary>
    private static string HandleSetVar(string[] parts, MustacheContext context)
    {
        if (parts.Length >= 2 && context.VariableStore != null)
        {
            var key = parts[1].Trim();
            var value = parts.Length >= 3 ? parts[2] : "";
            context.VariableStore.SetVar(key, value);
        }
        return ""; // setvar produces no output
    }

    /// <summary>
    /// Handles getvar command: {{getvar::key}} or {{getvar::key::default}}
    /// </summary>
    private static string HandleGetVar(string[] parts, MustacheContext context)
    {
        if (parts.Length >= 2 && context.VariableStore != null)
        {
            var key = parts[1].Trim();
            var defaultValue = parts.Length >= 3 ? parts[2] : "";
            return context.VariableStore.GetVar(key, defaultValue);
        }
        return "";
    }

    /// <summary>
    /// Handles custom or built-in variables.
    /// </summary>
    private static string HandleCustomOrBuiltin(string expression, MustacheContext context)
    {
        var lowerExpr = expression.ToLowerInvariant().Trim();
        
        // 1. Check if there's a mod-registered provider (full replacement)
        if (CustomProviders.TryGetValue(lowerExpr, out var provider))
        {
            try
            {
                return provider(context) ?? "";
            }
            catch (Exception ex)
            {
                Logger.Warning($"Custom provider '{lowerExpr}' failed: {ex.Message}");
                return "";
            }
        }
        
        // 2. Get built-in variable value
        var result = EvaluateBuiltinVariable(lowerExpr, context);
        
        // 3. Apply any registered appenders to modify the result
        result = ApplyAppenders(lowerExpr, context, result);
        
        return result;
    }
    
    /// <summary>
    /// Applies all registered appenders to modify the variable value.
    /// </summary>
    private static string ApplyAppenders(string varName, MustacheContext context, string originalValue)
    {
        if (!Appenders.TryGetValue(varName, out var appenderList) || appenderList.Count == 0)
            return originalValue;
        
        var result = originalValue;
        foreach (var appender in appenderList)
        {
            try
            {
                result = appender(context, result) ?? result;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Appender for '{varName}' failed: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>
    /// Evaluates built-in variables.
    /// </summary>
    private static string EvaluateBuiltinVariable(string varName, MustacheContext context)
    {
        var pawn = context.CurrentPawn;
        var map = context.Map ?? pawn?.Map;
        
        // Check if it's a pawn index variable (pawn1.xxx, pawn2.xxx, ...)
        var pawnIndexMatch = PawnIndexRegex.Match(varName);
        if (pawnIndexMatch.Success)
        {
            return EvaluatePawnIndexVariable(pawnIndexMatch, context);
        }
        
        return varName switch
        {
            // Current pawn related (for compatibility, should use pawn1.xxx instead)
            "pawn.name" => pawn?.LabelShort ?? "",
            "pawn.fullname" => pawn?.Name?.ToStringFull ?? "",
            "pawn.gender" => pawn?.gender.ToString() ?? "",
            "pawn.age" => pawn?.ageTracker?.AgeBiologicalYears.ToString() ?? "",
            "pawn.race" => GetPawnRace(pawn),
            "pawn.mood" => pawn?.needs?.mood?.MoodString ?? "",
            "pawn.moodpercent" => pawn?.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "",
            "pawn.personality" => Cache.Get(pawn)?.Personality ?? "",
            "pawn.title" => pawn?.story?.title ?? "",
            "pawn.faction" => pawn?.Faction?.Name ?? "",
            "pawn.job" => pawn?.CurJob?.def?.label ?? "",
            "pawn.role" => pawn?.GetRole() ?? "",
            "pawn.profile" => GetPawnProfile(pawn),
            
            // Multiple pawns related
            "pawns.all" => GetAllPawnsProfiles(context),
            "pawns.nearby" => GetNearbyPawnsSummary(context),
            "pawns.count" => context.Pawns?.Count.ToString() ?? "0",
            
            // Time related
            "time.hour" => map != null ? GenLocalDate.HourOfDay(map).ToString() : "",
            "time.hour12" => map != null ? GetHour12String(map) : "",
            "time.day" => map != null ? GenLocalDate.DayOfYear(map).ToString() : "",
            "time.date" => map != null ? GetDateString(map) : "",
            "time.quadrum" => map != null ? GenDate.Quadrum(Find.TickManager.TicksAbs, map.Tile).Label() : "",
            "time.year" => map != null ? GenLocalDate.Year(map).ToString() : "",
            "time.season" => map != null ? GenLocalDate.Season(map).Label() : "",
            
            // Weather/environment related
            "weather" => map?.weatherManager?.curWeather?.label ?? "",
            "temperature" => map != null ? Mathf.RoundToInt(map.mapTemperature.OutdoorTemp).ToString() : "",
            "location" => GetLocationString(pawn),
            "terrain" => pawn?.Position.GetTerrain(pawn.Map)?.LabelCap ?? "",
            "beauty" => GetBeautyString(pawn),
            "cleanliness" => GetCleanlinessString(pawn),
            "surroundings" => GetSurroundingsString(pawn),
            "wealth" => map != null ? Describer.Wealth(map.wealthWatcher.WealthTotal) : "",
            
            // Colony related
            "colony.name" => Find.CurrentMap?.Parent?.LabelCap ?? "",
            "colony.wealth" => map?.wealthWatcher?.WealthTotal.ToString("F0") ?? "",
            "colony.population" => map?.mapPawns?.FreeColonistsCount.ToString() ?? "",
            
            // Dialogue related
            "dialogue.type" => context.DialogueType ?? "",
            "dialogue.status" => context.DialogueStatus ?? "",
            "dialogue.ismonologue" => context.IsMonologue ? "true" : "false",
            
            // Language
            "lang" => LanguageDatabase.activeLanguage?.info?.friendlyNameNative ?? "English",
            
            // Legacy compatibility - context variable
            "context" => context.PawnContext ?? "",
            
            // Unknown variable - keep as-is for debugging
            _ => $"{{{{unknown:{varName}}}}}"
        };
    }

    /// <summary>
    /// Evaluates pawn index variable (pawn1.xxx, pawn2.xxx, ...).
    /// </summary>
    private static string EvaluatePawnIndexVariable(Match match, MustacheContext context)
    {
        if (!int.TryParse(match.Groups[1].Value, out int index) || index < 1)
            return "";
        
        var pawns = context.Pawns;
        if (pawns == null || index > pawns.Count)
            return "";
        
        var pawn = pawns[index - 1]; // Convert to 0-based index
        var property = match.Groups[2].Value.ToLowerInvariant();
        
        return property switch
        {
            "name" => pawn?.LabelShort ?? "",
            "fullname" => pawn?.Name?.ToStringFull ?? "",
            "gender" => pawn?.gender.ToString() ?? "",
            "age" => pawn?.ageTracker?.AgeBiologicalYears.ToString() ?? "",
            "race" => GetPawnRace(pawn),
            "mood" => pawn?.needs?.mood?.MoodString ?? "",
            "moodpercent" => pawn?.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "",
            "personality" => Cache.Get(pawn)?.Personality ?? "",
            "title" => pawn?.story?.title ?? "",
            "faction" => pawn?.Faction?.Name ?? "",
            "job" => pawn?.CurJob?.def?.label ?? "",
            "role" => pawn?.GetRole() ?? "",
            "profile" => GetPawnProfile(pawn),
            "backstory" => GetPawnBackstory(pawn),
            "traits" => GetPawnTraits(pawn),
            "skills" => GetPawnSkills(pawn),
            "health" => GetPawnHealth(pawn),
            "thoughts" => GetPawnThoughts(pawn),
            "relations" => GetPawnRelations(pawn),
            "equipment" => GetPawnEquipment(pawn),
            "status" => GetPawnStatus(pawn, context),
            _ => ""
        };
    }

    #region Pawn Context Helpers

    private static string GetPawnProfile(Pawn pawn)
    {
        if (pawn == null) return "";
        return PromptService.CreatePawnContext(pawn, PromptService.InfoLevel.Normal);
    }

    private static string GetPawnBackstory(Pawn pawn)
    {
        if (pawn == null) return "";
        return PromptService.CreatePawnBackstory(pawn, PromptService.InfoLevel.Normal);
    }

    private static string GetPawnTraits(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetTraitsContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnSkills(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetSkillsContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnHealth(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetHealthContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnThoughts(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetThoughtsContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnRelations(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetRelationsContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnEquipment(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetEquipmentContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnStatus(Pawn pawn, MustacheContext context)
    {
        if (pawn == null) return "";
        
        var sb = new StringBuilder();
        
        // Current job
        var job = pawn.CurJob?.def?.label;
        if (!string.IsNullOrEmpty(job))
            sb.Append($"{pawn.LabelShort} is {job}.");
        
        return sb.ToString();
    }

    private static string GetAllPawnsProfiles(MustacheContext context)
    {
        if (context.Pawns == null || context.Pawns.Count == 0)
            return context.PawnContext ?? "";
        
        var sb = new StringBuilder();
        for (int i = 0; i < context.Pawns.Count; i++)
        {
            var pawn = context.Pawns[i];
            if (pawn.IsPlayer()) continue;
            
            var profile = PromptService.CreatePawnContext(pawn,
                i == 0 ? PromptService.InfoLevel.Normal : PromptService.InfoLevel.Short);
            
            sb.AppendLine($"[P{i + 1}]");
            sb.AppendLine(profile);
        }
        
        return sb.ToString().TrimEnd();
    }

    private static string GetNearbyPawnsSummary(MustacheContext context)
    {
        if (context.Pawns == null || context.Pawns.Count <= 1)
            return "";
        
        var summaries = new List<string>();
        for (int i = 1; i < context.Pawns.Count; i++) // Skip first (initiator)
        {
            var pawn = context.Pawns[i];
            if (pawn.IsPlayer()) continue;
            
            var role = pawn.GetRole();
            var job = pawn.CurJob?.def?.label ?? "wandering";
            summaries.Add($"- {pawn.LabelShort}({role}) is {job}.");
        }
        
        return string.Join("\n", summaries);
    }

    #endregion

    #region Environment Helpers

    private static string GetDateString(Map map)
    {
        if (map == null) return "";
        var gameData = CommonUtil.GetInGameData();
        return gameData.DateString;
    }

    private static string GetLocationString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        
        var locationStatus = ContextHelper.GetPawnLocationStatus(pawn);
        if (string.IsNullOrEmpty(locationStatus)) return "";
        
        var temperature = Mathf.RoundToInt(pawn.Position.GetTemperature(pawn.Map));
        var room = pawn.GetRoom();
        var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "" : "";

        return string.IsNullOrEmpty(roomRole)
            ? $"{locationStatus};{temperature}C"
            : $"{locationStatus};{temperature}C;{roomRole}";
    }

    private static string GetBeautyString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        
        var nearbyCells = ContextHelper.GetNearbyCells(pawn);
        if (nearbyCells.Count == 0) return "";
        
        var beautySum = nearbyCells.Sum(c => BeautyUtility.CellBeauty(c, pawn.Map));
        return Describer.Beauty(beautySum / nearbyCells.Count);
    }

    private static string GetCleanlinessString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        
        var room = pawn.GetRoom();
        if (room is not { PsychologicallyOutdoors: false }) return "";
        
        return Describer.Cleanliness(room.GetStat(RoomStatDefOf.Cleanliness));
    }

    private static string GetSurroundingsString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        return ContextHelper.CollectNearbyContextText(pawn, 3) ?? "";
    }

    #endregion

    /// <summary>
    /// Gets a pawn's race.
    /// </summary>
    private static string GetPawnRace(Pawn pawn)
    {
        if (pawn == null) return "";
        if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
        {
            return pawn.genes.XenotypeLabel;
        }
        return pawn.def.label;
    }

    /// <summary>
    /// Gets 12-hour format time.
    /// </summary>
    private static string GetHour12String(Map map)
    {
        var hour = GenLocalDate.HourOfDay(map);
        var hour12 = hour % 12;
        if (hour12 == 0) hour12 = 12;
        var amPm = hour < 12 ? "AM" : "PM";
        return $"{hour12} {amPm}";
    }

    #region Variable Registry

    /// <summary>
    /// Gets all available built-in variables (for UI display).
    /// </summary>
    public static Dictionary<string, List<(string name, string description)>> GetBuiltinVariables()
    {
        return new Dictionary<string, List<(string, string)>>
        {
            ["RimTalk.MustacheVar.Category.PawnsAll".Translate()] = new()
            {
                ("pawns.all", "RimTalk.MustacheVar.pawns.all".Translate()),
                ("pawns.nearby", "RimTalk.MustacheVar.pawns.nearby".Translate()),
                ("pawns.count", "RimTalk.MustacheVar.pawns.count".Translate())
            },
            ["RimTalk.MustacheVar.Category.Pawn1".Translate()] = new()
            {
                ("pawn1.name", "RimTalk.MustacheVar.pawn.name".Translate()),
                ("pawn1.fullname", "RimTalk.MustacheVar.pawn.fullname".Translate()),
                ("pawn1.profile", "RimTalk.MustacheVar.pawn.profile".Translate()),
                ("pawn1.backstory", "RimTalk.MustacheVar.pawn.backstory".Translate()),
                ("pawn1.gender", "RimTalk.MustacheVar.pawn.gender".Translate()),
                ("pawn1.age", "RimTalk.MustacheVar.pawn.age".Translate()),
                ("pawn1.race", "RimTalk.MustacheVar.pawn.race".Translate()),
                ("pawn1.role", "RimTalk.MustacheVar.pawn.role".Translate()),
                ("pawn1.faction", "RimTalk.MustacheVar.pawn.faction".Translate()),
                ("pawn1.job", "RimTalk.MustacheVar.pawn.job".Translate()),
                ("pawn1.mood", "RimTalk.MustacheVar.pawn.mood".Translate()),
                ("pawn1.moodpercent", "RimTalk.MustacheVar.pawn.moodpercent".Translate()),
                ("pawn1.personality", "RimTalk.MustacheVar.pawn.personality".Translate()),
                ("pawn1.traits", "RimTalk.MustacheVar.pawn.traits".Translate()),
                ("pawn1.skills", "RimTalk.MustacheVar.pawn.skills".Translate()),
                ("pawn1.health", "RimTalk.MustacheVar.pawn.health".Translate()),
                ("pawn1.thoughts", "RimTalk.MustacheVar.pawn.thoughts".Translate()),
                ("pawn1.relations", "RimTalk.MustacheVar.pawn.relations".Translate()),
                ("pawn1.equipment", "RimTalk.MustacheVar.pawn.equipment".Translate()),
                ("pawn1.status", "RimTalk.MustacheVar.pawn.status".Translate())
            },
            ["RimTalk.MustacheVar.Category.Pawn2Plus".Translate()] = new()
            {
                ("pawn2.name", "RimTalk.MustacheVar.pawn2.name".Translate()),
                ("pawn2.profile", "RimTalk.MustacheVar.pawn2.profile".Translate()),
                ("pawn3.name", "RimTalk.MustacheVar.pawn3.name".Translate()),
                ("pawnN.xxx", "RimTalk.MustacheVar.pawnN.xxx".Translate())
            },
            ["RimTalk.MustacheVar.Category.Dialogue".Translate()] = new()
            {
                ("dialogue.type", "RimTalk.MustacheVar.dialogue.type".Translate()),
                ("dialogue.status", "RimTalk.MustacheVar.dialogue.status".Translate()),
                ("dialogue.ismonologue", "RimTalk.MustacheVar.dialogue.ismonologue".Translate())
            },
            ["RimTalk.MustacheVar.Category.Time".Translate()] = new()
            {
                ("time.hour", "RimTalk.MustacheVar.time.hour".Translate()),
                ("time.hour12", "RimTalk.MustacheVar.time.hour12".Translate()),
                ("time.date", "RimTalk.MustacheVar.time.date".Translate()),
                ("time.day", "RimTalk.MustacheVar.time.day".Translate()),
                ("time.season", "RimTalk.MustacheVar.time.season".Translate()),
                ("time.quadrum", "RimTalk.MustacheVar.time.quadrum".Translate()),
                ("time.year", "RimTalk.MustacheVar.time.year".Translate())
            },
            ["RimTalk.MustacheVar.Category.Environment".Translate()] = new()
            {
                ("weather", "RimTalk.MustacheVar.weather".Translate()),
                ("temperature", "RimTalk.MustacheVar.temperature".Translate()),
                ("location", "RimTalk.MustacheVar.location".Translate()),
                ("terrain", "RimTalk.MustacheVar.terrain".Translate()),
                ("beauty", "RimTalk.MustacheVar.beauty".Translate()),
                ("cleanliness", "RimTalk.MustacheVar.cleanliness".Translate()),
                ("surroundings", "RimTalk.MustacheVar.surroundings".Translate()),
                ("wealth", "RimTalk.MustacheVar.wealth".Translate())
            },
            ["RimTalk.MustacheVar.Category.Colony".Translate()] = new()
            {
                ("colony.name", "RimTalk.MustacheVar.colony.name".Translate()),
                ("colony.wealth", "RimTalk.MustacheVar.colony.wealth".Translate()),
                ("colony.population", "RimTalk.MustacheVar.colony.population".Translate())
            },
            ["RimTalk.MustacheVar.Category.System".Translate()] = new()
            {
                ("lang", "RimTalk.MustacheVar.lang".Translate()),
                ("context", "RimTalk.MustacheVar.context".Translate()),
                ("chat.history", "RimTalk.MustacheVar.chat.history".Translate())
            },
            ["RimTalk.MustacheVar.Category.VariableOps".Translate()] = new()
            {
                ("setvar::key::value", "RimTalk.MustacheVar.setvar".Translate()),
                ("getvar::key", "RimTalk.MustacheVar.getvar".Translate()),
                ("getvar::key::default", "RimTalk.MustacheVar.getvar.default".Translate())
            }
        };
    }

    #endregion

    #region Mod API

    /// <summary>
    /// Registers a custom variable provider (for other mods to use).
    /// </summary>
    /// <param name="name">Variable name (e.g., "mymod.customvar")</param>
    /// <param name="provider">Provider function</param>
    public static void RegisterProvider(string name, Func<MustacheContext, string> provider)
    {
        if (string.IsNullOrEmpty(name) || provider == null) return;
        CustomProviders[name.ToLowerInvariant()] = provider;
        Logger.Debug($"Registered mustache provider: {name}");
    }

    /// <summary>
    /// Unregisters a custom variable provider.
    /// </summary>
    public static void UnregisterProvider(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        CustomProviders.Remove(name.ToLowerInvariant());
        Logger.Debug($"Unregistered mustache provider: {name}");
    }

    /// <summary>
    /// Checks if a provider is registered.
    /// </summary>
    public static bool HasProvider(string name)
    {
        return !string.IsNullOrEmpty(name) && CustomProviders.ContainsKey(name.ToLowerInvariant());
    }

    /// <summary>
    /// Gets all registered provider names (for UI display).
    /// </summary>
    public static IEnumerable<string> GetRegisteredProviders()
    {
        return CustomProviders.Keys;
    }

    /// <summary>
    /// Registers an appender that can modify an existing variable's value.
    /// Unlike providers which replace values, appenders receive the original value
    /// and can modify it (e.g., append additional information).
    /// </summary>
    /// <param name="name">Variable name to append to (e.g., "weather")</param>
    /// <param name="appender">Appender function that takes (context, originalValue) and returns modified value</param>
    public static void RegisterAppender(string name, Func<MustacheContext, string, string> appender)
    {
        if (string.IsNullOrEmpty(name) || appender == null) return;
        
        var lowerName = name.ToLowerInvariant();
        if (!Appenders.ContainsKey(lowerName))
        {
            Appenders[lowerName] = new List<Func<MustacheContext, string, string>>();
        }
        Appenders[lowerName].Add(appender);
        Logger.Debug($"Registered mustache appender for: {name}");
    }

    /// <summary>
    /// Unregisters a specific appender from a variable.
    /// </summary>
    public static void UnregisterAppender(string name, Func<MustacheContext, string, string> appender)
    {
        if (string.IsNullOrEmpty(name) || appender == null) return;
        
        var lowerName = name.ToLowerInvariant();
        if (Appenders.TryGetValue(lowerName, out var list))
        {
            list.Remove(appender);
            if (list.Count == 0)
            {
                Appenders.Remove(lowerName);
            }
        }
        Logger.Debug($"Unregistered mustache appender for: {name}");
    }

    /// <summary>
    /// Unregisters all appenders for a variable.
    /// </summary>
    public static void UnregisterAllAppenders(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        
        var lowerName = name.ToLowerInvariant();
        Appenders.Remove(lowerName);
        Logger.Debug($"Unregistered all mustache appenders for: {name}");
    }

    /// <summary>
    /// Checks if a variable has any appenders registered.
    /// </summary>
    public static bool HasAppenders(string name)
    {
        return !string.IsNullOrEmpty(name) && Appenders.ContainsKey(name.ToLowerInvariant());
    }

    /// <summary>
    /// Gets all variable names that have appenders registered.
    /// </summary>
    public static IEnumerable<string> GetVariablesWithAppenders()
    {
        return Appenders.Keys;
    }

    #endregion
}