using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace RimTalk.Util;

public static class JsonUtil
{
    public static string SerializeToJson<T>(T obj)
    {
        // Create a memory stream for serialization
        using var stream = new MemoryStream();
        // Create a DataContractJsonSerializer
        var serializer = new DataContractJsonSerializer(typeof(T));

        // Serialize the ApiRequest object
        serializer.WriteObject(stream, obj);

        // Convert the memory stream to a string
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static T DeserializeFromJson<T>(string json)
    {
        if (!TryDeserializeFromJson<T>(json, out var result, out var ex))
        {
            Logger.Error($"Json deserialization failed for {typeof(T).Name}\n{json}\nException: {ex.Message}");
            throw ex;
        }
        return result;
    }

    public static bool TryDeserializeFromJson<T>(string json, out T result, out Exception exception)
    {
        result = default;
        exception = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        string sanitizedJson = Sanitize(json, typeof(T));

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedJson));
            var serializer = new DataContractJsonSerializer(typeof(T));
            result = (T)serializer.ReadObject(stream);
            return true;
        }
        catch (Exception ex)
        {
            exception = ex;
            return false;
        }
    }

    /// <summary>
    /// The definitive sanitizer that fixes structural, syntax, and formatting errors from LLM-generated JSON.
    /// </summary>
    /// <param name="text">The raw string from the LLM.</param>
    /// <param name="targetType">The C# type we are trying to deserialize into.</param>
    /// <returns>A cleaned and likely valid JSON string.</returns>
    public static string Sanitize(string text, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string sanitized = text.Replace("```json", "").Replace("```", "").Trim();

        int startIndex = sanitized.IndexOfAny(['{', '[']);
        int endIndex = sanitized.LastIndexOfAny(['}', ']']);

        if (startIndex >= 0 && endIndex > startIndex)
        {
            sanitized = sanitized.Substring(startIndex, endIndex - startIndex + 1).Trim();
        }
        else
        {
            return string.Empty;
        }

        sanitized = Regex.Replace(
            sanitized, 
            @"""([^""]+)""\s*:\s*([,}])", 
            @"""$1"":null$2"
        );

        if (sanitized.Contains("]["))
        {
            sanitized = sanitized.Replace("][", ",");
        }
        if (sanitized.Contains("}{"))
        {
            sanitized = sanitized.Replace("}{", "},{");
        }
    
        if (sanitized.StartsWith("{") && sanitized.EndsWith("}"))
        {
            string innerContent = sanitized.Substring(1, sanitized.Length - 2).Trim();
            if (innerContent.StartsWith("[") && innerContent.EndsWith("]"))
            {
                sanitized = innerContent;
            }
        }

        sanitized = ProtectMalformedQuotes(sanitized);

        bool isEnumerable = typeof(IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string);
        if (isEnumerable && sanitized.StartsWith("{"))
        {
            sanitized = $"[{sanitized}]";
        }

        return sanitized;
    }

    internal static bool IsJsonQuote(char c)
    {
        return c == '"' || c == '“' || c == '”';
    }

    internal static bool IsLikelyStringTerminator(string text, int quoteIndex, bool inValue)
    {
        // Find the first non-whitespace character after the quote
        int nextCharIndex = -1;
        char nextChar = '\0';
        for (int i = quoteIndex + 1; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                nextCharIndex = i;
                nextChar = text[i];
                break;
            }
        }

        if (nextCharIndex == -1)
            return true; // End of text is a terminator

        if (nextChar == ':')
        {
            // Colon can only terminate a string if we are currently parsing a Key, not a Value
            return !inValue;
        }

        if (nextChar == '}' || nextChar == ']')
        {
            // A quote followed by } or ] is always a valid string terminator
            return true;
        }

        if (nextChar == ',')
        {
            // Comma separator
            // Let's find the next non-whitespace character after ','
            int postCommaIndex = -1;
            for (int i = nextCharIndex + 1; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    postCommaIndex = i;
                    break;
                }
            }

            if (postCommaIndex == -1)
                return false; // Trailing comma with no following content is not a terminator context

            char first = text[postCommaIndex];

            // In an object context, a comma separator must be followed by a new key ("key":)
            if (inValue)
            {
                if (IsJsonQuote(first))
                {
                    for (int i = postCommaIndex + 1; i < text.Length; i++)
                    {
                        if (IsJsonQuote(text[i]))
                        {
                            for (int j = i + 1; j < text.Length; j++)
                            {
                                char next = text[j];
                                if (char.IsWhiteSpace(next))
                                    continue;
                                if (next == ':')
                                    return true;
                                break;
                            }
                        }
                    }
                }
                return false;
            }
            else
            {
                // In an array context, comma is followed by another array element (string, number, bool, null, object, array)
                if (IsJsonQuote(first))
                {
                    for (int i = postCommaIndex + 1; i < text.Length; i++)
                    {
                        if (IsJsonQuote(text[i]))
                        {
                            for (int j = i + 1; j < text.Length; j++)
                            {
                                char next = text[j];
                                if (char.IsWhiteSpace(next))
                                    continue;
                                if (next == ',' || next == ']')
                                    return true;
                                break;
                            }
                        }
                    }
                }
                if (char.IsDigit(first) || first == '-' || first == '{' || first == '[')
                    return true;

                string remaining = text.Substring(postCommaIndex);
                if (remaining.StartsWith("true") || remaining.StartsWith("false") || remaining.StartsWith("null"))
                    return true;
            }

            return false;
        }

        return false;
    }

    private static string ProtectMalformedQuotes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var sb = new StringBuilder(text.Length + 16);
        bool inString = false;
        bool escaped = false;
        char activeQuote = '\0';
        bool inValue = false; // State machine to track key vs value parsing

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (!inString)
            {
                if (IsJsonQuote(c))
                {
                    inString = true;
                    activeQuote = c;
                    sb.Append('"');
                }
                else
                {
                    if (c == ':')
                        inValue = true;
                    else if (c == ',')
                        inValue = false;
                    else if (c == '{')
                        inValue = false;
                    else if (c == '[')
                        inValue = true;

                    sb.Append(c);
                }

                continue;
            }

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (IsClosingQuoteForActiveString(activeQuote, c))
            {
                if (IsLikelyStringTerminator(text, i, inValue))
                {
                    sb.Append('"');
                    inString = false;
                    activeQuote = '\0';
                }
                else
                {
                    sb.Append("\\\"");
                }

                continue;
            }

            sb.Append(c);
        }

        if (inString)
            sb.Append('"');

        return sb.ToString();
    }

    internal static bool IsClosingQuoteForActiveString(char activeQuote, char current)
    {
        if (activeQuote == '"')
            return current == '"';

        if (activeQuote == '“')
            return current == '”' || current == '“' || current == '"';

        if (activeQuote == '”')
            return current == '”' || current == '“' || current == '"';

        return current == '"';
    }

    #region Dynamic JSON Parsing, Formatting & Merging

    public static bool IsValidJson(string json, out string errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            ParseJsonValue(json.Trim(), out _);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        try
        {
            var parsed = ParseJsonValue(json.Trim(), out _);
            return SerializeJsonValue(parsed, indent: true);
        }
        catch
        {
            return json;
        }
    }

    public static string MergeJson(string baseJson, string overrideJson)
    {
        if (string.IsNullOrWhiteSpace(overrideJson))
            return baseJson;

        if (string.IsNullOrWhiteSpace(baseJson))
            return overrideJson;

        try
        {
            var baseParsed = ParseJsonValue(baseJson.Trim(), out _) as Dictionary<string, object>;
            var overrideParsed = ParseJsonValue(overrideJson.Trim(), out _) as Dictionary<string, object>;

            if (baseParsed == null || overrideParsed == null)
                return baseJson;

            MergeDictionaries(baseParsed, overrideParsed);
            return SerializeJsonValue(baseParsed, indent: false);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to merge custom JSON override: {ex.Message}. Using base request JSON.");
            return baseJson;
        }
    }

    private static void MergeDictionaries(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        foreach (var kvp in source)
        {
            // If the override contains "messages" and it's null or empty array, preserve target's messages
            if (kvp.Key == "messages" && (kvp.Value == null || (kvp.Value is List<object> list && list.Count == 0)))
                continue;

            if (target.TryGetValue(kvp.Key, out var targetVal) &&
                targetVal is Dictionary<string, object> targetChild &&
                kvp.Value is Dictionary<string, object> sourceChild)
            {
                MergeDictionaries(targetChild, sourceChild);
            }
            else
            {
                target[kvp.Key] = kvp.Value;
            }
        }
    }

    public static object ParseJsonValue(string json, out int endIndex)
    {
        var parser = new SimpleJsonParser(json);
        var result = parser.ParseValue();
        endIndex = parser.Index;
        return result;
    }

    public static string SerializeJsonValue(object value, bool indent = false)
    {
        var sb = new StringBuilder();
        SerializeValue(value, sb, indent, 0);
        return sb.ToString();
    }

    private static void SerializeValue(object val, StringBuilder sb, bool indent, int depth)
    {
        if (val == null)
        {
            sb.Append("null");
        }
        else if (val is bool b)
        {
            sb.Append(b ? "true" : "false");
        }
        else if (val is string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append($"\\u{(int)c:x4}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
        else if (val is double d)
        {
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (val is float f)
        {
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (val is long l)
        {
            sb.Append(l.ToString(CultureInfo.InvariantCulture));
        }
        else if (val is int i)
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture));
        }
        else if (val is Dictionary<string, object> dict)
        {
            if (dict.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            if (indent) sb.Append('\n');

            int index = 0;
            foreach (var kvp in dict)
            {
                if (indent) sb.Append(new string(' ', (depth + 1) * 2));
                sb.Append('"').Append(kvp.Key).Append("\":");
                if (indent) sb.Append(' ');
                SerializeValue(kvp.Value, sb, indent, depth + 1);

                if (++index < dict.Count)
                    sb.Append(',');

                if (indent) sb.Append('\n');
            }

            if (indent) sb.Append(new string(' ', depth * 2));
            sb.Append('}');
        }
        else if (val is IList list)
        {
            if (list.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append('[');
            if (indent) sb.Append('\n');

            for (int j = 0; j < list.Count; j++)
            {
                if (indent) sb.Append(new string(' ', (depth + 1) * 2));
                SerializeValue(list[j], sb, indent, depth + 1);

                if (j < list.Count - 1)
                    sb.Append(',');

                if (indent) sb.Append('\n');
            }

            if (indent) sb.Append(new string(' ', depth * 2));
            sb.Append(']');
        }
        else
        {
            sb.Append(Convert.ToString(val, CultureInfo.InvariantCulture));
        }
    }

    private class SimpleJsonParser
    {
        private readonly string _json;
        public int Index { get; private set; }

        public SimpleJsonParser(string json)
        {
            _json = json ?? "";
            Index = 0;
        }

        private void SkipWhitespace()
        {
            while (Index < _json.Length && char.IsWhiteSpace(_json[Index]))
                Index++;
        }

        public object ParseValue()
        {
            SkipWhitespace();
            if (Index >= _json.Length)
                throw new FormatException("Unexpected end of JSON input");

            char c = _json[Index];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"' || c == '“' || c == '”') return ParseString();
            if (c == 't' || c == 'f') return ParseBool();
            if (c == 'n') return ParseNull();
            if (c == '-' || char.IsDigit(c)) return ParseNumber();

            throw new FormatException($"Unexpected character '{c}' at position {Index}");
        }

        private Dictionary<string, object> ParseObject()
        {
            var dict = new Dictionary<string, object>();
            Index++; // Skip '{'

            while (true)
            {
                SkipWhitespace();
                if (Index >= _json.Length)
                    throw new FormatException("Unterminated object: missing '}'");

                if (_json[Index] == '}')
                {
                    Index++;
                    return dict;
                }

                if (dict.Count > 0)
                {
                    if (_json[Index] != ',')
                        throw new FormatException($"Expected ',' in object at position {Index}");
                    Index++;
                    SkipWhitespace();
                    if (Index < _json.Length && _json[Index] == '}')
                    {
                        Index++; // Allow trailing comma
                        return dict;
                    }
                }

                string key = ParseString();
                SkipWhitespace();

                if (Index >= _json.Length || _json[Index] != ':')
                    throw new FormatException($"Expected ':' after property name at position {Index}");

                Index++; // Skip ':'
                object val = ParseValue();
                dict[key] = val;
            }
        }

        private List<object> ParseArray()
        {
            var list = new List<object>();
            Index++; // Skip '['

            while (true)
            {
                SkipWhitespace();
                if (Index >= _json.Length)
                    throw new FormatException("Unterminated array: missing ']'");

                if (_json[Index] == ']')
                {
                    Index++;
                    return list;
                }

                if (list.Count > 0)
                {
                    if (_json[Index] != ',')
                        throw new FormatException($"Expected ',' in array at position {Index}");
                    Index++;
                    SkipWhitespace();
                    if (Index < _json.Length && _json[Index] == ']')
                    {
                        Index++; // Allow trailing comma
                        return list;
                    }
                }

                list.Add(ParseValue());
            }
        }

        private string ParseString()
        {
            char quoteChar = _json[Index];
            Index++; // Skip quote

            var sb = new StringBuilder();
            while (Index < _json.Length)
            {
                char c = _json[Index++];
                if (c == quoteChar || (quoteChar == '“' && c == '”'))
                    return sb.ToString();

                if (c == '\\' && Index < _json.Length)
                {
                    char esc = _json[Index++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Index + 4 <= _json.Length)
                            {
                                string hex = _json.Substring(Index, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                Index += 4;
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            throw new FormatException("Unterminated string");
        }

        private bool ParseBool()
        {
            if (_json.Substring(Index).StartsWith("true"))
            {
                Index += 4;
                return true;
            }
            if (_json.Substring(Index).StartsWith("false"))
            {
                Index += 5;
                return false;
            }
            throw new FormatException($"Expected boolean at position {Index}");
        }

        private object ParseNull()
        {
            if (_json.Substring(Index).StartsWith("null"))
            {
                Index += 4;
                return null;
            }
            throw new FormatException($"Expected null at position {Index}");
        }

        private object ParseNumber()
        {
            int start = Index;
            if (_json[Index] == '-') Index++;

            while (Index < _json.Length && (char.IsDigit(_json[Index]) || _json[Index] == '.' || _json[Index] == 'e' || _json[Index] == 'E' || _json[Index] == '+' || _json[Index] == '-'))
            {
                if ((_json[Index] == '+' || _json[Index] == '-') && (_json[Index - 1] != 'e' && _json[Index - 1] != 'E'))
                    break;
                Index++;
            }

            string numStr = _json.Substring(start, Index - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
            {
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
            }

            throw new FormatException($"Invalid number '{numStr}' at position {start}");
        }
    }

    #endregion
}
