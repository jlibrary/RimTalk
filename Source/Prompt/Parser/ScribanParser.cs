using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using UnityEngine;
using RimWorld;
using Verse;
using Cache = RimTalk.Data.Cache;
using Logger = RimTalk.Util.Logger;

namespace RimTalk.Prompt;

public static class ScribanParser
{
	private static Dictionary<string, object> _sessionVariables = new Dictionary<string, object>();

	public static void ResetSessionVariables()
	{
		_sessionVariables.Clear();
	}

	public static void SetSessionVar(string key, object value)
	{
		if (!string.IsNullOrEmpty(key))
		{
			_sessionVariables[key.ToLowerInvariant()] = value;
		}
	}

	public static object GetSessionVar(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return "";
		}
		object value;
		return _sessionVariables.TryGetValue(key.ToLowerInvariant(), out value) ? value : "";
	}
    public static string Render(string templateText, PromptContext context, bool logErrors = true)
    {
        if (string.IsNullOrWhiteSpace(templateText)) return "";
        
        try
        {
            var template = Template.Parse(templateText);
            if (template.HasErrors)
            {
                if (logErrors) Logger.Error($"Scriban Parse Errors: {string.Join("\n", template.Messages)}");
                return templateText;
            }

            var scriptObject = new ScriptObject();
            
            // 1. IMPORT Objects & Context
            scriptObject.Import(context, filter: m => !(m is MethodInfo mi && mi.ReturnType == typeof(void)));
            scriptObject.Add("ctx", context);
            scriptObject.Add("pawn", context.CurrentPawn);
            scriptObject.Add("recipient", context.TalkRequest?.Recipient);
            scriptObject.Add("pawns", context.AllPawns);
            scriptObject.Add("map", context.Map);
            scriptObject.Add("settings", Settings.Get());
            
            // 2.1 Session variable functions (cross-entry variables)
			scriptObject.Import("setvar", new Action<string, object>(SetSessionVar));
			scriptObject.Import("getvar", new Func<string, object>(GetSessionVar));
			scriptObject.Import("random", new Func<int, int, int>((min, max) => UnityEngine.Random.Range(min, max)));

            // 2. IMPORT UTILITIES (Extension Methods support)
            // This allows: {{ pawn | IsTalkEligible }} or {{ GetRole pawn }}
            // We force PascalCase to match the UI list and TemplateContext settings
            scriptObject.Import(typeof(PawnUtil), renamer: m => m.Name, filter: m => !(m is MethodInfo mi && mi.ReturnType == typeof(void)));
            scriptObject.Import(typeof(CommonUtil), renamer: m => m.Name, filter: m => !(m is MethodInfo mi && mi.ReturnType == typeof(void)));
            
            // 2.5 USEFUL STATIC CLASSES
            scriptObject.Add("PawnsFinder", typeof(PawnsFinder));
            scriptObject.Add("Find", typeof(Find));
            scriptObject.Add("GenDate", typeof(GenDate));
            
            // 3. ROOT PROPERTIES & SYSTEM
            scriptObject.Add("lang", Constant.Lang);
            
            // Structured game state plus convenient root aliases.
            // Keep `date` under `game` because `date` is a Scriban builtin object.
            var game = CreateGameState(context.Map);
            scriptObject.Add("game", game);
            foreach (var name in new[] { "time", "hour", "day", "quadrum", "year", "season", "weather", "temperature", "wealth" })
                scriptObject.Add(name, game[name]);
            
            var json = new ScriptObject();
            json.Add("format", Constant.GetJsonInstruction(Settings.Get().ApplyMoodAndSocialEffects));
            scriptObject.Add("json", json);

            var chat = new ScriptObject();
            string historyText = GetChatHistoryText(context);
            chat.Add("history", historyText);
            chat.Add("history_simplified", GetChatHistoryText(context, simplified: true));
            scriptObject.Add("chat", chat);
            
            // 4. SHORTHANDS
            scriptObject.Add("prompt", context.DialoguePrompt);
            scriptObject.Add("context", context.PawnContext);
            
            // 5. GLOBALVARIABLES
            if (context.VariableStore != null)
                foreach (var kvp in context.VariableStore.GetAllVariables())
                    if (!scriptObject.ContainsKey(kvp.Key))
                        scriptObject.Add(kvp.Key, kvp.Value);

            var templateContext = new TemplateContext { 
                MemberRenamer = m => m.Name,
                MemberFilter = m =>
                {
                    if (m is MethodInfo mi && mi.ReturnType == typeof(void)) return false;
                    if (m.DeclaringType == typeof(Pawn))
                    {
                        var name = m.Name;
                        if (name.Equals("skills", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("health", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("equipment", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("genes", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("surroundings", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("social", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("relations", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    return true;
                }
            };
            
            // 6. THE BRIDGE (Hooks & Magic Shorthands & Case Insensitivity)
            templateContext.TryGetVariable = (TemplateContext tctx, SourceSpan span, Scriban.Syntax.ScriptVariable variable, out object value) =>
            {
                value = null;
                string varName = variable.Name;
                if (string.IsNullOrEmpty(varName)) return false;

                // A. RimTalk API Context Variables
                if (ContextHookRegistry.TryGetContextVariable(varName, context, out var apiValue))
                {
                    value = apiValue;
                    return true;
                }

                // B. Builtin / scriptObject (Case-Insensitive)
                var global = tctx.BuiltinObject;
                if (global.TryGetValue(varName, out value)) return true;
                
                var key = global.Keys.FirstOrDefault(k => k.Equals(varName, StringComparison.OrdinalIgnoreCase));
                if (key != null)
                {
                    value = global[key];
                    return true;
                }

                return false;
            };

            templateContext.TryGetMember = (TemplateContext tctx, SourceSpan span, object target, string member, out object value) =>
            {
                value = null;
                
                // A. RimTalk Magic Hooks
                if (target is PromptContext ctx)
                {
                    if (ContextHookRegistry.TryGetContextVariable(member, ctx, out var ctxValue)) { value = ctxValue; return true; }
                    var raw = GetMagicContextValue(ctx, member);
                    if (raw != null) { value = raw; return true; }
                }
                if (target is Pawn p)
                {
                    var normalized = NormalizePawnMember(member);
                    if (ContextHookRegistry.TryGetPawnVariable(member, p, out var custom) ||
                        (normalized != member && ContextHookRegistry.TryGetPawnVariable(normalized, p, out custom)))
                    {
                        value = custom;
                        return true;
                    }
                    var cat = ContextCategories.TryGetPawnCategory(normalized);
                    if (cat.HasValue) {
                        var raw = GetMagicPawnValue(p, normalized);
                        value = ContextHookRegistry.ApplyPawnHooks(cat.Value, p, raw);
                        return true;
                    }
                }
                else if (target is Map m)
                {
                    if (ContextHookRegistry.TryGetEnvironmentVariable(member, m, out var env)) { value = env; return true; }
                    var cat = ContextCategories.TryGetEnvironmentCategory(member);
                    if (cat.HasValue) {
                        var raw = GetMagicMapValue(m, member);
                        value = ContextHookRegistry.ApplyEnvironmentHooks(cat.Value, m, raw);
                        return true;
                    }
                }
                
                // B. Dictionary/ScriptObject Access (Case-Insensitive)
                // This handles Global variables (chat.history) and imported functions (GetRole)
                if (target is System.Collections.Generic.IDictionary<string, object> dict)
                {
                    if (dict.TryGetValue(member, out value)) return true; // Fast exact match
                    
                    var key = dict.Keys.FirstOrDefault(k => k.Equals(member, StringComparison.OrdinalIgnoreCase));
                    if (key != null)
                    {
                        value = dict[key];
                        return true;
                    }
                }
                
                // B2. Static Class Access (When target is a Type object)
                if (target is Type t)
                {
                    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                    
                    var prop = t.GetProperties(flags)
                        .FirstOrDefault(p => p.Name.Equals(member, StringComparison.OrdinalIgnoreCase));

                    if (prop != null)
                    {
                        value = prop.GetValue(null);
                        return true;
                    }
                    
                    var field = t.GetFields(flags)
                        .FirstOrDefault(f => f.Name.Equals(member, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        value = field.GetValue(null);
                        return true;
                    }
                }
                
                // C. CLR Object Access (Case-Insensitive Reflection)
                // This handles C# properties (pawn.LabelShort)
                if (target != null && !(target is System.Collections.Generic.IDictionary<string, object>))
                {
                    var type = target.GetType();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
                    
                    var prop = type.GetProperties(flags)
                        .FirstOrDefault(p => p.Name.Equals(member, StringComparison.OrdinalIgnoreCase));

                    if (prop != null)
                    {
                        value = prop.GetValue(target);
                        return true;
                    }
                    
                    var field = type.GetFields(flags)
                        .FirstOrDefault(f => f.Name.Equals(member, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        value = field.GetValue(target);
                        return true;
                    }
                }

                return false; 
            };

            templateContext.PushGlobal(scriptObject);
            return template.Render(templateContext);
        }
        catch (Exception ex)
        {
            if (logErrors) Logger.Error($"Scriban Render Error: {ex.Message}");
            return templateText;
        }
    }

    private static string GetMagicPawnValue(Pawn pawn, string member) {
        return member.ToLowerInvariant() switch {
            "name" => pawn.LabelShort,
            "fullname" => pawn.Name?.ToStringFull ?? "",
            "gender" => pawn.gender.ToString(),
            "age" => pawn.ageTracker?.AgeBiologicalYears.ToString() ?? "",
            "race" => ModsConfig.BiotechActive && pawn.genes?.Xenotype != null
                ? pawn.genes.XenotypeLabel
                : pawn.def?.LabelCap.RawText ?? "",
            "title" => pawn.GetTitle(),
            "faction" => pawn.Faction?.Name ?? "",
            "job" => pawn.GetActivity(),
            "role" => pawn.GetRole(),
            "mood" => pawn.needs?.mood?.MoodString ?? "",
            "moodpercent" => pawn.needs?.mood != null
                ? pawn.needs.mood.CurLevelPercentage.ToString("P0")
                : "",
            "personality" => Cache.Get(pawn)?.Personality ?? "",
            "profile" => PromptService.CreatePawnContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "backstory" => ContextBuilder.GetBackstoryContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "traits" => ContextBuilder.GetTraitsContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "skills" => ContextBuilder.GetSkillsContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "health" => ContextBuilder.GetHealthContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "thoughts" => ContextBuilder.GetThoughtsContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "fullthought" => ContextBuilder.GetAllThoughtsContext(pawn) ?? "",
            "relations" => ContextBuilder.GetRelationsContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "equipment" => ContextBuilder.GetEquipmentContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "genes" => ContextBuilder.GetAllGenesContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "notable_genes" => ContextBuilder.GetNotableGenesContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "ideology" => ContextBuilder.GetIdeologyContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "captive_status" => ContextBuilder.GetPrisonerSlaveContext(pawn, PromptService.InfoLevel.Normal) ?? "",
            "social" => RelationsService.GetRelationsString(pawn),
            "fullsocial" => RelationsService.GetAllSocialString(pawn),
            "fullrelation" => RelationsService.GetAllRelationsString(pawn),
            "fullinteraction" => RelationsService.GetAllInteractionString(pawn),
            "location" => PromptContextProvider.GetLocationString(pawn),
            "terrain" => pawn.Map != null ? pawn.Position.GetTerrain(pawn.Map)?.LabelCap ?? "" : "",
            "beauty" => PromptContextProvider.GetBeautyString(pawn),
            "cleanliness" => PromptContextProvider.GetCleanlinessString(pawn),
            "surroundings" => ContextHelper.CollectNearbyContextText(pawn, 3) ?? "",
            _ => null
        };
    }

    private static ScriptObject CreateGameState(Map map)
    {
        var game = new ScriptObject();
        var ticks = Find.TickManager?.TicksAbs ?? 0;
        var longLat = map != null && Find.WorldGrid != null
            ? Find.WorldGrid.LongLatOf(map.Tile)
            : Vector2.zero;

        game.Add("time", ApplyEnvironmentHook(map, ContextCategories.Environment.Time,
            CommonUtil.GetInGameHour12HString(ticks, longLat)));
        game.Add("hour", GenDate.HourOfDay(ticks, longLat.x));
        game.Add("date", ApplyEnvironmentHook(map, ContextCategories.Environment.Date,
            GenDate.DateFullStringAt(ticks, longLat)));
        game.Add("day", GenDate.DayOfQuadrum(ticks, longLat.x) + 1);
        game.Add("quadrum", GenDate.Quadrum(ticks, longLat.x).Label());
        game.Add("year", GenDate.Year(ticks, longLat.x));
        game.Add("season", ApplyEnvironmentHook(map, ContextCategories.Environment.Season,
            map != null ? GenDate.Season(ticks, longLat).Label() : Season.Undefined.Label()));
        game.Add("weather", ApplyEnvironmentHook(map, ContextCategories.Environment.Weather,
            map?.weatherManager?.curWeather?.label ?? ""));
        game.Add("temperature", ApplyEnvironmentHook(map, ContextCategories.Environment.Temperature,
            map != null ? Mathf.RoundToInt(map.mapTemperature.OutdoorTemp).ToString() : ""));
        game.Add("wealth", ApplyEnvironmentHook(map, ContextCategories.Environment.Wealth,
            map?.wealthWatcher != null ? Describer.Wealth(map.wealthWatcher.WealthTotal) : ""));
        return game;
    }

    private static string ApplyEnvironmentHook(Map map, ContextCategory category, string value)
    {
        return map != null
            ? ContextHookRegistry.ApplyEnvironmentHooks(category, map, value ?? "")
            : value ?? "";
    }

    private static string GetMagicMapValue(Map map, string member) {
        var ticks = Find.TickManager?.TicksAbs ?? 0;
        var longLat = map != null && Find.WorldGrid != null
            ? Find.WorldGrid.LongLatOf(map.Tile)
            : Vector2.zero;

        return member.ToLowerInvariant() switch {
            "time" => CommonUtil.GetInGameHour12HString(ticks, longLat),
            "date" => GenDate.DateFullStringAt(ticks, longLat),
            "season" => map != null ? GenDate.Season(ticks, longLat).Label() : Season.Undefined.Label(),
            "weather" => map.weatherManager?.curWeather?.label ?? "",
            "temperature" => Mathf.RoundToInt(map.mapTemperature.OutdoorTemp).ToString(),
            "wealth" => map?.wealthWatcher != null ? Describer.Wealth(map.wealthWatcher.WealthTotal) : "",
            _ => null
        };
    }

    private static string NormalizePawnMember(string member)
    {
        if (string.IsNullOrEmpty(member)) return member;
        return member.ToLowerInvariant() switch
        {
            "skilltracker" => "skills",
            "healthtracker" => "health",
            "equipmenttracker" => "equipment",
            "genetracker" => "genes",
            "surroundingstracker" => "surroundings",
            _ => member
        };
    }

    private static object GetMagicContextValue(PromptContext context, string member) {
        return member.ToLowerInvariant() switch {
            "dialogue_type" or "dialoguetype" => context.DialogueType ?? "",
            "intent" => context.Intent ?? "",
            "conversation_topic" or "conversationtopic" => context.ConversationTopic ?? "",
            "dialogue_status" or "dialoguestatus" => context.DialogueStatus ?? "",
            "prompt" or "dialogue_prompt" or "dialogueprompt" => context.DialoguePrompt ?? "",
            "context" or "pawn_context" or "pawncontext" => context.PawnContext ?? "",
            "user_prompt" or "userprompt" => context.UserPrompt ?? "",
            "is_monologue" or "ismonologue" => context.IsMonologue,
            "talk_type" or "talktype" => context.TalkType,
            "history" or "chat_history" or "chathistory" => GetChatHistoryText(context),
            "pawn_count" or "pawncount" => context.AllPawns?.Count ?? 0,
            "map_id" or "mapid" => context.Map?.uniqueID ?? 0,
            _ => null
        };
    }

    private static string GetChatHistoryText(PromptContext context, bool simplified = false)
    {
        var history = simplified ? context.GetChatHistory(true) : context.ChatHistory;
        if (history != null && history.Count > 0)
        {
            var lines = history.Select((h, i) =>
            {
                var text = (h.message ?? "").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
                return $"- {i + 1} | role={h.role} | text={text}";
            });
            return "Conversation history (reference only; do not repeat or continue):\n" + string.Join("\n", lines);
        }

        if (context.IsPreview)
            return "Conversation history (reference only; do not repeat or continue):\n" +
                   "- 1 | role=User | text=Hello!\n" +
                   "- 2 | role=AI | text=Greetings from RimTalk. This is a placeholder for chat history.";

        return "";
    }
}
