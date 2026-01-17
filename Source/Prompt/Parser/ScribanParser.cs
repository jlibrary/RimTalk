using System;
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
            
            // Time & Date shorthands
            var ticks = Find.TickManager.TicksAbs;
            if (context.Map != null)
            {
                var longLat = Find.WorldGrid.LongLatOf(context.Map.Tile);
                scriptObject.Add("hour", GenDate.HourOfDay(ticks, longLat.x));
                scriptObject.Add("day", GenDate.DayOfQuadrum(ticks, longLat.x) + 1);
                scriptObject.Add("quadrum", GenDate.Quadrum(ticks, longLat.x).Label());
                scriptObject.Add("year", GenDate.Year(ticks, longLat.x));
                scriptObject.Add("season", GenLocalDate.Season(context.Map).Label());
            }
            else
            {
                scriptObject.Add("hour", GenDate.HourOfDay(ticks, 0));
                scriptObject.Add("day", GenDate.DayOfQuadrum(ticks, 0) + 1);
                scriptObject.Add("quadrum", GenDate.Quadrum(ticks, 0).Label());
                scriptObject.Add("year", GenDate.Year(ticks, 0));
                scriptObject.Add("season", Season.Undefined.Label());
            }
            
            var json = new ScriptObject();
            json.Add("format", Constant.GetJsonInstruction(Settings.Get().ApplyMoodAndSocialEffects));
            scriptObject.Add("json", json);

            var chat = new ScriptObject();
            string historyText = "";
            if (context.ChatHistory != null && context.ChatHistory.Count > 0)
            {
                historyText = string.Join("\n\n", context.ChatHistory.Select(h => $"[{h.role}] {h.message}"));
            }
            else if (context.IsPreview)
            {
                historyText = "[User] (Preview) Hello!\n\n[AI] (Preview) Greetings from RimTalk. This is a placeholder for chat history.";
            }
            chat.Add("history", historyText);
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
                MemberFilter = m => !(m is MethodInfo mi && mi.ReturnType == typeof(void))
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
                if (target is Pawn p)
                {
                    if (ContextHookRegistry.TryGetPawnVariable(member, p, out var custom)) { value = custom; return true; }
                    var cat = ContextCategories.TryGetPawnCategory(member);
                    if (cat.HasValue) {
                        var raw = GetMagicPawnValue(p, member);
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
            "job" => pawn.GetActivity(),
            "role" => pawn.GetRole(),
            "mood" => pawn.needs?.mood?.MoodString ?? "",
            "personality" => Cache.Get(pawn)?.Personality ?? "",
            "social" => RelationsService.GetRelationsString(pawn),
            "location" => PromptContextProvider.GetLocationString(pawn),
            "beauty" => PromptContextProvider.GetBeautyString(pawn),
            "cleanliness" => PromptContextProvider.GetCleanlinessString(pawn),
            "surroundings" => ContextHelper.CollectNearbyContextText(pawn, 3) ?? "",
            _ => null
        };
    }

    private static string GetMagicMapValue(Map map, string member) {
        return member.ToLowerInvariant() switch {
            "weather" => map.weatherManager?.curWeather?.label ?? "",
            "temperature" => Mathf.RoundToInt(map.mapTemperature.OutdoorTemp).ToString(),
            _ => null
        };
    }
}
