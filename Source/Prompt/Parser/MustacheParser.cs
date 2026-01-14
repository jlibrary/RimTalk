using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimTalk.API;
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
    // Regex to match {{...}}
    private static readonly Regex MustacheRegex = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);
    
    // Regex to match section blocks {{#section}}...{{/section}}
    private static readonly Regex SectionRegex = new(@"\{\{#(\w+)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Compiled | RegexOptions.Singleline);
    
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
            // Step 1: Process section blocks {{#section}}...{{/section}}
            template = ProcessSections(template, context);
            
            // Step 2: Process regular variables {{...}}
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
    /// Processes section blocks like {{#pawns}}...{{/pawns}}.
    /// Supports iterating over pawn collections.
    /// </summary>
    private static string ProcessSections(string template, MustacheContext context)
    {
        return SectionRegex.Replace(template, match =>
        {
            var sectionName = match.Groups[1].Value.ToLowerInvariant();
            var innerTemplate = match.Groups[2].Value;
            
            return sectionName switch
            {
                "pawns" => ProcessPawnsSection(innerTemplate, context),
                _ => match.Value // Unknown section, keep as-is
            };
        });
    }
    
    /// <summary>
    /// Processes {{#pawns}}...{{/pawns}} section.
    /// Iterates over all pawns in context.Pawns.
    /// Inside the block, variables like {{name}}, {{job}} refer to the current pawn.
    /// </summary>
    private static string ProcessPawnsSection(string innerTemplate, MustacheContext context)
    {
        var pawns = context.Pawns;
        if (pawns == null || pawns.Count == 0)
            return "";
        
        var sb = new StringBuilder();
        for (int i = 0; i < pawns.Count; i++)
        {
            var pawn = pawns[i];
            if (pawn == null || pawn.IsPlayer()) continue;
            
            // Create a child context with the current pawn as the scope
            var pawnContext = CreatePawnScopedContext(pawn, context, i);
            
            // Parse the inner template with the pawn-scoped context
            var result = ParseWithPawnScope(innerTemplate, pawnContext);
            if (!string.IsNullOrWhiteSpace(result))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(result.TrimEnd());
            }
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Creates a context scoped to a specific pawn for section iteration.
    /// </summary>
    private static MustacheContext CreatePawnScopedContext(Pawn pawn, MustacheContext parentContext, int index)
    {
        return new MustacheContext
        {
            CurrentPawn = pawn,
            AllPawns = parentContext.AllPawns,  // Use AllPawns instead of read-only Pawns
            Map = parentContext.Map ?? pawn?.Map,
            DialoguePrompt = parentContext.DialoguePrompt,
            DialogueType = parentContext.DialogueType,
            DialogueStatus = parentContext.DialogueStatus,
            TalkRequest = parentContext.TalkRequest,  // Preserve TalkRequest for IsMonologue
            PawnContext = parentContext.PawnContext,
            VariableStore = parentContext.VariableStore,
            ChatHistory = parentContext.ChatHistory,
            // Store the current index for {{index}} variable
            ScopedPawnIndex = index
        };
    }
    
    /// <summary>
    /// Parses a template with pawn-scoped variables.
    /// Variables like {{name}}, {{job}} resolve to the scoped pawn's properties.
    /// </summary>
    private static string ParseWithPawnScope(string template, MustacheContext context)
    {
        return MustacheRegex.Replace(template, match =>
        {
            var expression = match.Groups[1].Value.Trim().ToLowerInvariant();
            
            // Check if it's a pawn property (no prefix)
            var pawnValue = GetScopedPawnProperty(expression, context);
            if (pawnValue != null)
                return pawnValue;
            
            // Fall back to regular expression evaluation
            return EvaluateExpression(expression, context);
        });
    }
    
    /// <summary>
    /// Gets a property value from the scoped pawn in section context.
    /// Supports hooks and custom pawn variables for extensibility.
    /// Returns null if the expression is not a simple pawn property.
    /// </summary>
    private static string GetScopedPawnProperty(string expression, MustacheContext context)
    {
        var pawn = context.CurrentPawn;
        if (pawn == null) return null;
        
        // Handle special index variable
        if (expression == "index")
            return (context.ScopedPawnIndex + 1).ToString(); // 1-based index
        
        if (expression == "index0")
            return context.ScopedPawnIndex.ToString(); // 0-based index
        
        // Try to get pawn property using shared method
        var result = GetRawPawnPropertyValue(pawn, expression);
        
        // If not a built-in property, check for custom pawn variables
        if (result == null)
        {
            if (ContextHookRegistry.TryGetPawnVariable(expression, pawn, out var customValue))
                return customValue;
            return null; // Return null to fall back to regular evaluation
        }
        
        // Apply hooks via unified API if there's a matching category
        var category = MapVarNameToCategory(expression);
        if (category != null)
            return ContextHookRegistry.ApplyPawnHooks(category.Value, pawn, result);
        
        return result;
    }
    
    /// <summary>
    /// Gets the raw property value for a pawn without applying hooks.
    /// This is the shared core implementation used by both GetScopedPawnProperty and GetPawnProperty.
    /// Returns null if the property is not recognized.
    /// </summary>
    private static string GetRawPawnPropertyValue(Pawn pawn, string property)
    {
        if (pawn == null) return null;
        
        return property switch
        {
            "name" => pawn.LabelShort ?? "",
            "fullname" => pawn.Name?.ToStringFull ?? "",
            "gender" => pawn.gender.ToString(),
            "age" => pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "",
            "race" => GetPawnRace(pawn),
            "mood" => GetPawnMood(pawn),
            "moodpercent" => pawn.needs?.mood?.CurLevelPercentage.ToString("P0") ?? "",
            "personality" => Cache.Get(pawn)?.Personality ?? "",
            "title" => pawn.story?.title ?? "",
            "faction" => pawn.Faction?.Name ?? "",
            "job" => GetPawnActivity(pawn),
            "role" => pawn.GetRole() ?? "",
            "profile" => GetPawnProfile(pawn),
            "backstory" => GetPawnBackstory(pawn),
            "traits" => GetPawnTraits(pawn),
            "skills" => GetPawnSkills(pawn),
            "health" => GetPawnHealth(pawn),
            "thoughts" => GetPawnThoughts(pawn),
            "relations" => GetPawnRelations(pawn),
            "equipment" => GetPawnEquipment(pawn),
            "genes" => GetPawnGenes(pawn),
            "ideology" => GetPawnIdeology(pawn),
            "captive_status" => GetPawnCaptiveStatus(pawn),
            "location" => MustacheContextProvider.GetLocationString(pawn),
            "terrain" => pawn?.Position.GetTerrain(pawn.Map)?.LabelCap ?? "",
            "beauty" => MustacheContextProvider.GetBeautyString(pawn),
            "cleanliness" => MustacheContextProvider.GetCleanlinessString(pawn),
            "surroundings" => GetSurroundingsString(pawn),
            _ => null // Not a built-in pawn property
        };
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
    /// Value can contain :: separators, they will be preserved.
    /// </summary>
    private static string HandleSetVar(string[] parts, MustacheContext context)
    {
        if (parts.Length >= 2 && context.VariableStore != null)
        {
            var key = parts[1].Trim();
            // Join all parts after key with :: to preserve separators in value
            var value = parts.Length >= 3
                ? string.Join("::", parts, 2, parts.Length - 2)
                : "";
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
        
        // 1. Check for custom context variables (e.g., {{memory}})
        if (ContextHookRegistry.TryGetContextVariable(lowerExpr, context, out var contextValue))
            return contextValue;
        
        // 2. Check for custom environment variables (e.g., {{radiation}})
        var map = context.Map ?? context.CurrentPawn?.Map;
        if (map != null && ContextHookRegistry.TryGetEnvironmentVariable(lowerExpr, map, out var envValue))
            return envValue;
        
        // 3. Get built-in variable value
        var result = EvaluateBuiltinVariable(lowerExpr, context);
        
        // 4. Apply any registered hooks to modify the result
        result = ApplyAppenders(lowerExpr, context, result);
        
        return result;
    }
    
    // Applies hooks to modify the variable value using unified ContextHookRegistry.
    // Maps mustache variable names to ContextCategory for hook application.
    private static string ApplyAppenders(string varName, MustacheContext context, string originalValue)
    {
        var pawn = context.CurrentPawn;
        var map = context.Map ?? pawn?.Map;
        
        // Try to map varName to a ContextCategory and apply hooks
        var category = MapVarNameToCategory(varName);
        if (category == null)
            return originalValue;
        
        if (category.Value.Type == ContextType.Pawn && pawn != null)
            return ContextHookRegistry.ApplyPawnHooks(category.Value, pawn, originalValue);
        
        if (category.Value.Type == ContextType.Environment && map != null)
            return ContextHookRegistry.ApplyEnvironmentHooks(category.Value, map, originalValue);
        
        return originalValue;
    }
    
    // Maps mustache variable names to ContextCategory for hook application.
    private static ContextCategory? MapVarNameToCategory(string varName)
    {
        return varName switch
        {
            // Pawn categories
            "pawn.race" or "race" => ContextCategories.Pawn.Race,
            "pawn.mood" or "mood" => ContextCategories.Pawn.Mood,
            "backstory" => ContextCategories.Pawn.Backstory,
            "traits" => ContextCategories.Pawn.Traits,
            "skills" => ContextCategories.Pawn.Skills,
            "health" => ContextCategories.Pawn.Health,
            "thoughts" => ContextCategories.Pawn.Thoughts,
            "relations" => ContextCategories.Pawn.Relations,
            "equipment" => ContextCategories.Pawn.Equipment,
            "genes" => ContextCategories.Pawn.Genes,
            "ideology" => ContextCategories.Pawn.Ideology,
            "captive_status" => ContextCategories.Pawn.CaptiveStatus,
            
            // Pawn location-based categories (moved from Environment)
            "location" => ContextCategories.Pawn.Location,
            "terrain" => ContextCategories.Pawn.Terrain,
            "beauty" => ContextCategories.Pawn.Beauty,
            "cleanliness" => ContextCategories.Pawn.Cleanliness,
            "surroundings" => ContextCategories.Pawn.Surroundings,
            
            // Environment categories
            "weather" => ContextCategories.Environment.Weather,
            "wealth" => ContextCategories.Environment.Wealth,
            "time.hour" or "time.hour12" => ContextCategories.Environment.Time,
            "time.date" => ContextCategories.Environment.Date,
            "time.season" => ContextCategories.Environment.Season,
            
            _ => null
        };
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
            "pawn.job" => GetPawnActivity(pawn),
            "pawn.role" => pawn?.GetRole() ?? "",
            "pawn.profile" => GetPawnProfile(pawn),
            
            // Multiple pawns related
            "pawns.all" => GetAllPawnsProfiles(context),
            "pawns.nearby" => GetNearbyPawnsSummary(context),
            "pawns.count" => context.Pawns?.Count.ToString() ?? "0",
            
            // Time related
            "time.hour" => map != null ? GenLocalDate.HourOfDay(map).ToString() : "",
            "time.hour12" => map != null ? CommonUtil.GetInGameHour12HString(map) : "",
            "time.day" => map != null ? GenLocalDate.DayOfYear(map).ToString() : "",
            "time.date" => map != null ? GetDateString(map) : "",
            "time.quadrum" => map != null ? GenDate.Quadrum(Find.TickManager.TicksAbs, map.Tile).Label() : "",
            "time.year" => map != null ? GenLocalDate.Year(map).ToString() : "",
            "time.season" => map != null ? GenLocalDate.Season(map).Label() : "",
            
            // Weather/environment related - reuse MustacheContextProvider methods to avoid duplication
            "weather" => map?.weatherManager?.curWeather?.label ?? "",
            "temperature" => map != null ? Mathf.RoundToInt(map.mapTemperature.OutdoorTemp).ToString() : "",
            "location" => MustacheContextProvider.GetLocationString(pawn),
            "terrain" => pawn?.Position.GetTerrain(pawn.Map)?.LabelCap ?? "",
            "beauty" => MustacheContextProvider.GetBeautyString(pawn),
            "cleanliness" => MustacheContextProvider.GetCleanlinessString(pawn),
            "surroundings" => GetSurroundingsString(pawn),
            "wealth" => map != null ? Describer.Wealth(map.wealthWatcher.WealthTotal) : "",
            
            // Colony related
            "colony.name" => Find.CurrentMap?.Parent?.LabelCap ?? "",
            "colony.wealth" => map?.wealthWatcher?.WealthTotal.ToString("F0") ?? "",
            "colony.population" => map?.mapPawns?.FreeColonistsCount.ToString() ?? "",
            
            // Dialogue related
            "dialogue" => context.DialoguePrompt ?? "",
            "dialogue.type" => context.DialogueType ?? "",
            "dialogue.status" => context.DialogueStatus ?? "",
            "dialogue.ismonologue" => context.IsMonologue ? "true" : "false",
            
            // Language
            "lang" => LanguageDatabase.activeLanguage?.info?.friendlyNameNative ?? "English",
            
            // JSON format instruction (dynamic based on ApplyMoodAndSocialEffects setting)
            "json.format" => Constant.GetJsonInstruction(Settings.Get().ApplyMoodAndSocialEffects),
            
            // Legacy compatibility - context variable
            "context" => context.PawnContext ?? "",
            
            // Chat history marker - returns empty string when parsed inline
            // (actual history insertion is handled by PromptManager.BuildMessages)
            "chat.history" => "",
            
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
        
        return GetPawnProperty(pawn, property, context);
    }

    private static string GetPawnProperty(Pawn pawn, string property, MustacheContext context)
    {
        // Get raw property value using shared method
        var result = GetRawPawnPropertyValue(pawn, property);
        
        // If not a built-in property, check for custom pawn variables
        if (result == null)
        {
            if (ContextHookRegistry.TryGetPawnVariable(property, pawn, out var customValue))
                return customValue;
            return "";
        }
        
        // Apply hooks via unified API if there's a matching category
        var category = MapVarNameToCategory(property);
        if (category != null && pawn != null)
            return ContextHookRegistry.ApplyPawnHooks(category.Value, pawn, result);
        
        return result;
    }

    private static string GetPawnMood(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetMoodContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    // ===== Pawn Context Helpers =====

    private static string GetPawnProfile(Pawn pawn)
    {
        if (pawn == null) return "";
        return PromptService.CreatePawnContext(pawn, PromptService.InfoLevel.Normal);
    }

    private static string GetPawnBackstory(Pawn pawn)
    {
        if (pawn == null) return "";
        // Return only the childhood and adulthood backstory, not the full pawn introduction
        return ContextBuilder.GetBackstoryContext(pawn, PromptService.InfoLevel.Normal) ?? "";
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

    private static string GetPawnGenes(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetNotableGenesContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnIdeology(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetIdeologyContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }

    private static string GetPawnCaptiveStatus(Pawn pawn)
    {
        if (pawn == null) return "";
        return ContextBuilder.GetPrisonerSlaveContext(pawn, PromptService.InfoLevel.Normal) ?? "";
    }
    /// <summary>
    /// Gets a pawn's current activity using GetActivity() extension method.
    /// Returns detailed descriptions like "enjoying packaged survival meal" instead of just "ingest".
    /// </summary>
    private static string GetPawnActivity(Pawn pawn)
    {
        if (pawn == null) return "";
        var activity = pawn.GetActivity();
        return string.IsNullOrEmpty(activity) ? "wandering" : activity;
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
            var activity = GetPawnActivity(pawn);
            summaries.Add($"- {pawn.LabelShort}({role}) is {activity}.");
        }
        
        return string.Join("\n", summaries);
    }

    // ===== Environment Helpers =====

    private static string GetDateString(Map map)
    {
        if (map == null) return "";
        var gameData = CommonUtil.GetInGameData();
        return gameData.DateString;
    }

    // GetLocationString, GetBeautyString, GetCleanlinessString now reuse ContextBuilder methods

    private static string GetSurroundingsString(Pawn pawn)
    {
        if (pawn?.Map == null) return "";
        return ContextHelper.CollectNearbyContextText(pawn, 3) ?? "";
    }

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

    // ===== Variable Registry =====

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
                ("pawn1.genes", "RimTalk.MustacheVar.pawn.genes".Translate()),
                ("pawn1.ideology", "RimTalk.MustacheVar.pawn.ideology".Translate()),
                ("pawn1.captive_status", "RimTalk.MustacheVar.pawn.captive_status".Translate())
            },
            ["RimTalk.MustacheVar.Category.Pawn2Plus".Translate()] = new()
            {
                ("pawn2.name", "RimTalk.MustacheVar.pawn2.name".Translate()),
                ("pawn2.profile", "RimTalk.MustacheVar.pawn2.profile".Translate()),
                ("pawn3.name", "RimTalk.MustacheVar.pawn3.name".Translate()),
                ("pawnN.xxx", "RimTalk.MustacheVar.pawnN.xxx".Translate())
            },
            ["RimTalk.MustacheVar.Category.Sections".Translate()] = new()
            {
                ("#pawns}}...{{/pawns", "RimTalk.MustacheVar.section.pawns".Translate()),
                ("index", "RimTalk.MustacheVar.section.index".Translate()),
                ("name", "RimTalk.MustacheVar.section.name".Translate()),
                ("profile", "RimTalk.MustacheVar.section.profile".Translate())
            },
            ["RimTalk.MustacheVar.Category.Dialogue".Translate()] = new()
            {
                ("dialogue", "RimTalk.MustacheVar.dialogue".Translate()),
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
                ("json.format", "RimTalk.MustacheVar.json.format".Translate()),
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
}
