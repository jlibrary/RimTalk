using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimTalk.Util;
using RimWorld.Planet;
using Verse;

namespace RimTalk.Data;

public class RimTalkWorldComponent(World world) : WorldComponent(world)
{
    private const int MaxLogEntries = 1000;

    public Dictionary<string, string> RimTalkInteractionTexts = new();
    private Queue<string> _keyInsertionOrder = new();

    public override void ExposeData()
    {
        base.ExposeData();

        try 
        {
            Scribe_Collections.Look(ref RimTalkInteractionTexts, "rimtalkInteractionTexts", LookMode.Value, LookMode.Value);
        }
        catch (System.Exception ex)
        {
            Logger.Error($"Failed to save/load interaction texts. Resetting data to prevent save corruption. Error: {ex.Message}");
            RimTalkInteractionTexts = new Dictionary<string, string>();
            _keyInsertionOrder = new Queue<string>();
        }

        List<string> keyOrderList = null;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            keyOrderList = _keyInsertionOrder.ToList();
        }

        Scribe_Collections.Look(ref keyOrderList, "rimtalkKeyOrder");

        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        RimTalkInteractionTexts ??= new Dictionary<string, string>();
            
        _keyInsertionOrder = keyOrderList != null ? new Queue<string>(keyOrderList) : new Queue<string>();
    }

    public void SetTextFor(LogEntry entry, string text)
    {
        if (entry == null || text == null) return;

        string cleanText = SanitizeXmlString(text);
        string key = entry.GetUniqueLoadID();

        if (RimTalkInteractionTexts.ContainsKey(key))
        {
            RimTalkInteractionTexts[key] = cleanText;
            return;
        }

        while (_keyInsertionOrder.Count >= MaxLogEntries)
        {
            string oldestKey = _keyInsertionOrder.Dequeue();
            RimTalkInteractionTexts.Remove(oldestKey);
        }

        _keyInsertionOrder.Enqueue(key);
        RimTalkInteractionTexts[key] = cleanText;
    }

    public bool TryGetTextFor(LogEntry entry, out string text)
    {
        text = null;
        return entry != null && RimTalkInteractionTexts.TryGetValue(entry.GetUniqueLoadID(), out text);
    }

    private static string SanitizeXmlString(string invalidXml)
    {
        if (string.IsNullOrEmpty(invalidXml)) return invalidXml;

        StringBuilder stringBuilder = new StringBuilder(invalidXml.Length);
        foreach (char c in invalidXml)
        {
            // XML 1.0 allows:
            // #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
            if ((c == 0x9) || (c == 0xA) || (c == 0xD) ||
                ((c >= 0x20) && (c <= 0xD7FF)) ||
                ((c >= 0xE000) && (c <= 0xFFFD)))
            {
                stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString();
    }
}