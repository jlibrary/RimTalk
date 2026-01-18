using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RimTalk.API;
using RimTalk.Util;
using RimWorld;
using Scriban.Runtime;
using Verse;

namespace RimTalk.Prompt;

public static class VariableDefinitions
{
    private static readonly Dictionary<string, Type> RootTypeMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> StaticRoots = new(StringComparer.OrdinalIgnoreCase);

    static VariableDefinitions()
    {
        // 1. Explicit Core Aliases
        RootTypeMap["pawn"] = typeof(Pawn);
        RootTypeMap["recipient"] = typeof(Pawn);
        RootTypeMap["pawns"] = typeof(List<Pawn>);
        RootTypeMap["map"] = typeof(Map);
        RootTypeMap["ctx"] = typeof(PromptContext);
        
        // 2. Static Classes
        RootTypeMap["PawnsFinder"] = typeof(PawnsFinder); StaticRoots.Add("PawnsFinder");
        RootTypeMap["Find"] = typeof(Find); StaticRoots.Add("Find");
        RootTypeMap["GenDate"] = typeof(GenDate); StaticRoots.Add("GenDate");

        // 3. System Shorthands
        RootTypeMap["lang"] = typeof(string);
        RootTypeMap["prompt"] = typeof(string);
        RootTypeMap["context"] = typeof(string);
        RootTypeMap["json"] = typeof(ScriptObject);
        RootTypeMap["chat"] = typeof(ScriptObject);

        // 4. Auto-populate from PromptContext (imported properties)
        foreach (var prop in typeof(PromptContext).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!RootTypeMap.ContainsKey(prop.Name))
                RootTypeMap[prop.Name] = prop.PropertyType;
        }
    }

    public static Dictionary<string, List<(string name, string description)>> GetScribanVariables()
    {
        var dict = new Dictionary<string, List<(string name, string description)>>();

        // 1. Manual Shorthands (The "Magic" variables - now dynamic!)
        var pawnMagic = ContextCategories.Pawn.All
            .Select(c => ($"pawn.{c.Key}", c.Key.CapitalizeFirst()))
            .ToList();
        dict["RimTalk.ScribanVar.Category.PawnShorthands".Translate()] = pawnMagic;

        // 2. UTILITY METHODS
        AddStaticMethods(dict, "Utility: Pawn", typeof(PawnUtil));
        AddStaticMethods(dict, "Utility: Common", typeof(CommonUtil));

        // 3. CORE OBJECTS & CONTEXT
        dict["RimTalk.ScribanVar.Category.CoreObjects".Translate()] = new()
        {
            ("pawn", "The primary character (initiator)"),
            ("recipient", "The character being spoken to (if any)"),
            ("pawns", "List of all pawns in the dialogue"),
            ("map", "The current map object"),
            ("ctx", "The full prompt context object")
        };

        dict["RimTalk.ScribanVar.Category.Context".Translate()] = new()
        {
            ("prompt", "Full decorated prompt including time, weather, and location"),
            ("context", "Raw formatted string describing the initiator's details"),
            ("ctx.DialogueType", "Type of dialogue (monologue, conversation, etc.)"),
            ("ctx.DialogueStatus", "Current status of the dialogue"),
            ("ctx.PawnContext", "Formatted string describing the pawn"),
            ("ctx.UserPrompt", "The raw prompt from the user (if any)"),
            ("ctx.IsMonologue", "True if this is a monologue"),
        };
        
        // 3.5 Game Static Classes
        dict["RimTalk.ScribanVar.Category.GameStatic".Translate()] = new()
        {
            ("PawnsFinder", "Access to global pawn lists"),
            ("Find", "Access to current game state (Maps, TickManager)"),
            ("GenDate", "Date utilities")
        };

        // 4. System Variables
        dict["RimTalk.ScribanVar.Category.System".Translate()] = new()
        {
            ("lang", "Active native language name"),
            ("hour", "Current hour (0-23)"),
            ("day", "Day of quadrum (1-15)"),
            ("quadrum", "Current quadrum (Aprimay, Jugust, Septober, Decembary)"),
            ("year", "Current year (e.g. 5500)"),
            ("season", "Current season (Spring, Summer, Fall, Winter)"),
            ("json.format", "JSON output instructions"),
            ("chat.history", "Full conversation history (Role: Message)")
        };

        // 5. Mod-added variables from the API
        var customVars = ContextHookRegistry.GetAllCustomVariables().ToList();
        if (customVars.Any())
        {
            var modVarsList = customVars.Select(v => (v.Name, $"[{v.Type}] {v.Description} (from {v.ModId})")).ToList();
            dict["RimTalk.Settings.PromptPreset.ModVariables".Translate()] = modVarsList;
        }

        return dict;
    }

    public static Dictionary<string, List<(string name, string description)>> GetDynamicVariables(string query, string fullText = null)
    {
        var results = new Dictionary<string, List<(string, string)>>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        // 1. Identify Root and Traverse
        var (currentType, isStaticContext, currentPath) = ResolveTypePath(query, fullText);
        if (currentType == null) return results;

        // 2. Extract filter (text after last dot if query doesn't end with dot)
        string filter = "";
        int lastDotIndex = query.LastIndexOf('.');
        if (lastDotIndex >= 0 && lastDotIndex < query.Length - 1)
        {
            filter = query.Substring(lastDotIndex + 1);
        }
        else if (lastDotIndex < 0 && !query.Contains("["))
        {
            // Root level filter (rare here but possible)
            filter = query.Trim();
        }

        // 3. Generate Suggestions
        var candidates = new List<(string, string)>();
        var suggestionFlags = BindingFlags.Public | (isStaticContext ? BindingFlags.Static : BindingFlags.Instance);
        
        foreach (var prop in currentType.GetProperties(suggestionFlags))
        {
            if (prop.GetIndexParameters().Length > 0) continue;
            if (prop.IsDefined(typeof(ObsoleteAttribute), true)) continue;
            if (string.IsNullOrEmpty(filter) || prop.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                candidates.Add(($"{currentPath}.{prop.Name}", $"{prop.PropertyType.Name}"));
        }
        
        foreach (var field in currentType.GetFields(suggestionFlags))
        {
            if (field.IsDefined(typeof(ObsoleteAttribute), true)) continue;
            if (string.IsNullOrEmpty(filter) || field.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                candidates.Add(($"{currentPath}.{field.Name}", $"{field.FieldType.Name}"));
        }
        
        foreach (var method in currentType.GetMethods(suggestionFlags))
        {
            if (method.IsSpecialName || method.DeclaringType == typeof(object) || method.IsDefined(typeof(ObsoleteAttribute), true) || method.ReturnType == typeof(void)) continue;
            
            if (string.IsNullOrEmpty(filter) || method.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string paramList = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
                candidates.Add(($"{currentPath}.{method.Name}", $"{method.ReturnType.Name} ({paramList})"));
            }
        }

        if (!isStaticContext)
        {
            var utils = new[] { typeof(PawnUtil), typeof(CommonUtil), typeof(GenderUtility) };
            foreach (var util in utils)
            {
                var extensions = util.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false) 
                                && m.GetParameters().Length > 0 
                                && m.GetParameters()[0].ParameterType.IsAssignableFrom(currentType)
                                && m.ReturnType != typeof(void));

                foreach (var method in extensions)
                {
                    if (string.IsNullOrEmpty(filter) || method.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string paramList = string.Join(", ", method.GetParameters().Skip(1).Select(p => p.ParameterType.Name));
                        candidates.Add(($"{currentPath}.{method.Name}", $"{method.ReturnType.Name} ({paramList})"));
                    }
                }
            }
        }

        if (candidates.Any())
            results[$"Dynamic: {currentType.Name}"] = candidates.OrderBy(x => x.Item1).ToList();

        // 4. Add Magic Properties (The "New Fields" the user is looking for)
        if (typeof(Pawn).IsAssignableFrom(currentType))
        {
            AddPawnMagicSuggestions(results, currentPath, filter);
        }
        else if (typeof(Map).IsAssignableFrom(currentType))
        {
            AddMapMagicSuggestions(results, currentPath, filter);
        }

        return results;
    }

    private static void AddPawnMagicSuggestions(Dictionary<string, List<(string name, string description)>> results, string path, string filter)
    {
        var magic = new List<(string, string)>();
        
        // A. Predefined Magic Categories
        foreach (var cat in ContextCategories.Pawn.All)
        {
            if (string.IsNullOrEmpty(filter) || cat.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                magic.Add(($"{path}.{cat.Key}", "RimTalk Magic Property"));
        }
        
        // B. Custom Mod Variables
        foreach (var v in ContextHookRegistry.GetAllCustomVariables())
        {
            if (v.Type == "Pawn")
            {
                string name = v.Name.Contains(".") ? v.Name.Substring(v.Name.IndexOf('.') + 1) : v.Name;
                if (string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    magic.Add(($"{path}.{name}", $"Custom ({v.ModId})"));
            }
        }

        if (magic.Any())
            results["AI Context Fields"] = magic.OrderBy(x => x.Item1).ToList();
    }

    private static void AddMapMagicSuggestions(Dictionary<string, List<(string name, string description)>> results, string path, string filter)
    {
        var magic = new List<(string, string)>();
        
        foreach (var cat in ContextCategories.Environment.All)
        {
            if (string.IsNullOrEmpty(filter) || cat.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                magic.Add(($"{path}.{cat.Key}", "RimTalk Magic Property"));
        }

        foreach (var v in ContextHookRegistry.GetAllCustomVariables())
        {
            if (v.Type == "Environment")
            {
                if (string.IsNullOrEmpty(filter) || v.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    magic.Add(($"{path}.{v.Name}", $"Custom ({v.ModId})"));
            }
        }

        if (magic.Any())
            results["Environment Fields"] = magic.OrderBy(x => x.Item1).ToList();
    }

    private static (Type type, bool isStatic, string path) ResolveTypePath(string query, string fullText)
    {
        // Clean query: remove {{ and }} and whitespace
        string cleanQuery = query.Replace("{", "").Replace("}", "").Trim();
        
        // Handle path tokens (supporting properties and indexers)
        var parts = Regex.Split(cleanQuery, @"(?<=\.)|(?=\.)|(?=\[)|(?<=\])")
            .Where(p => p != "." && !string.IsNullOrEmpty(p))
            .ToList();

        if (parts.Count == 0) return (null, false, "");

        // 1. Identify Root
        Type currentType = null;
        bool isStaticContext = false;
        
        string firstPart = parts[0].TrimEnd('.');
        if (RootTypeMap.TryGetValue(firstPart, out var type))
        {
            currentType = type;
            isStaticContext = StaticRoots.Contains(firstPart);
        }
        else if (!string.IsNullOrEmpty(fullText))
        {
            currentType = InferTypeFromText(firstPart, fullText);
        }

        if (currentType == null) return (null, false, "");
        string currentPath = firstPart;

        // 2. Traverse parts
        int traversalLimit = cleanQuery.EndsWith(".") || cleanQuery.EndsWith("]") ? parts.Count : parts.Count - 1;
        
        for (int i = 1; i < traversalLimit; i++)
        {
            string part = parts[i];
            if (part.StartsWith("["))
            {
                if (currentType.IsArray)
                {
                    currentType = currentType.GetElementType();
                }
                else if (currentType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(currentType))
                {
                    currentType = currentType.GetGenericArguments().FirstOrDefault() ?? typeof(object);
                }
                else
                {
                    var itemProp = currentType.GetProperty("Item");
                    if (itemProp != null) currentType = itemProp.PropertyType;
                }
                currentPath += part;
                isStaticContext = false;
                continue;
            }

            var flags = BindingFlags.Public | BindingFlags.IgnoreCase | (isStaticContext ? BindingFlags.Static : BindingFlags.Instance);

            var prop = currentType.GetProperties(flags)
                .FirstOrDefault(p => p.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            
            var field = currentType.GetFields(flags)
                .FirstOrDefault(f => f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));

            if (prop != null)
            {
                currentType = prop.PropertyType;
                currentPath += "." + prop.Name;
                isStaticContext = false;
            }
            else if (field != null)
            {
                currentType = field.FieldType;
                currentPath += "." + field.Name;
                isStaticContext = false;
            }
            else
            {
                return (null, false, "");
            }
        }

        return (currentType, isStaticContext, currentPath);
    }

    private static Type InferTypeFromText(string varName, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // 1. Check loops: for item in collection
        var loopMatch = Regex.Match(text, $@"for\s+{varName}\s+in\s+([a-zA-Z0-9_\.\[\]]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (loopMatch.Success)
        {
            string expression = loopMatch.Groups[1].Value;
            var (type, _, _) = ResolveTypePath(expression + ".", null); // Recursive call but with fullText=null to break cycle
            if (type != null)
            {
                if (type.IsArray) return type.GetElementType();
                if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                    return type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
                
                var itemProp = type.GetProperty("Item");
                if (itemProp != null) return itemProp.PropertyType;
            }
        }

        // 2. Check 'with' blocks: {{ with object }}
        var withMatch = Regex.Match(text, $@"with\s+([a-zA-Z0-9_\.\[\]]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (withMatch.Success)
        {
            string expression = withMatch.Groups[1].Value;
            if (expression.Equals(varName, StringComparison.OrdinalIgnoreCase))
            {
                var (type, _, _) = ResolveTypePath(expression + ".", null);
                return type;
            }
        }

        // 3. Check 'capture' blocks: {{ capture varName }}
        var captureMatch = Regex.Match(text, $@"capture\s+{varName}\s*}}(.*?){{", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (captureMatch.Success)
        {
            string content = captureMatch.Groups[1].Value.Trim();
            if (Regex.IsMatch(content, @"^[a-zA-Z0-9_\.]+$"))
            {
                var (type, _, _) = ResolveTypePath(content + ".", null);
                return type;
            }
        }

        // 4. Check assignments: varName = expression
        var assignmentMatch = Regex.Match(text, $@"{varName}\s*=\s*([a-zA-Z0-9_\.\[\]]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (assignmentMatch.Success)
        {
            string expression = assignmentMatch.Groups[1].Value;
            var (type, _, _) = ResolveTypePath(expression + ".", null);
            return type;
        }

        return null;
    }

    private static void AddVanillaProperties(Dictionary<string, List<(string name, string description)>> dict, string prefix, Type type)
    {
        var list = new List<(string, string)>();
        
        // Properties
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && !p.IsDefined(typeof(ObsoleteAttribute), true))
            .OrderBy(p => p.Name);

        foreach (var prop in props)
        {
            string name = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            list.Add((name, prop.PropertyType.Name));
        }
        
        // Fields
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsDefined(typeof(ObsoleteAttribute), true))
            .OrderBy(f => f.Name);
            
        foreach (var field in fields)
        {
            string name = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
            list.Add((name, field.FieldType.Name));
        }
        
        if (list.Any())
        {
            string categoryName = string.IsNullOrEmpty(prefix) ? "Context (Raw)" : $"{type.Name} (Raw)";
            dict[$"{categoryName} ({list.Count} fields)"] = list;
        }
    }

    private static void AddStaticMethods(Dictionary<string, List<(string name, string description)>> dict, string categoryName, Type type)
    {
        var list = new List<(string, string)>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(ObsoleteAttribute), true) && m.ReturnType != typeof(void))
            .OrderBy(m => m.Name);

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            string name = method.Name;
            
            string paramList = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
            list.Add((name, $"{method.ReturnType.Name} ({paramList})"));
        }

        if (list.Any()) dict[categoryName] = list;
    }
}