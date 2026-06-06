using System.Collections.Generic;
using System.Text;

namespace RimTalk.Util;

public class JsonStreamParser<T> where T : class
{
    private readonly StringBuilder _buffer = new();

    public List<T> Parse(string textChunk)
    {
        _buffer.Append(textChunk);
        var newObjects = new List<T>();
        string text = _buffer.ToString();
        int searchStart = 0;
        int lastSuccessfulEnd = 0;

        while (searchStart < text.Length)
        {
            int objStart = text.IndexOf('{', searchStart);
            if (objStart == -1)
            {
                break;
            }

            int objEnd = FindMatchingBrace(text, objStart);
            if (objEnd == -1)
            {
                // Incomplete object
                break;
            }

            string jsonObj = text.Substring(objStart, objEnd - objStart + 1);

            if (JsonUtil.TryDeserializeFromJson<T>(jsonObj, out var parsedObject, out _))
            {
                if (parsedObject != null)
                {
                    newObjects.Add(parsedObject);
                }
            }

            searchStart = objEnd + 1;
            lastSuccessfulEnd = searchStart;
        }

        if (lastSuccessfulEnd > 0)
        {
            _buffer.Remove(0, lastSuccessfulEnd);
        }

        return newObjects;
    }

    private int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        char activeQuote = '\0';

        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                if (inString)
                    escaped = true;
                continue;
            }

            if (JsonUtil.IsJsonQuote(c))
            {
                if (!inString)
                {
                    inString = true;
                    activeQuote = c;
                    continue;
                }

                if (IsClosingQuoteForActiveString(activeQuote, c))
                {
                    if (JsonUtil.IsLikelyStringTerminator(text, i))
                    {
                        inString = false;
                        activeQuote = '\0';
                    }
                }

                continue;
            }

            if (inString) continue;

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1; // No matching brace found
    }

    private static bool IsClosingQuoteForActiveString(char activeQuote, char current)
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
