using System;
using System.Collections;
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
}
