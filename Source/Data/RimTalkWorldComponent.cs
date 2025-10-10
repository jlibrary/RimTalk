using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace RimTalk.Data;

public class RimTalkWorldComponent(World world) : WorldComponent(world)
{
    private const int MaxLogEntries = 10000;

    public Dictionary<string, string> RimTalkInteractionTexts = new();
    private Queue<string> _keyInsertionOrder = new();

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Collections.Look(ref RimTalkInteractionTexts, "rimtalkInteractionTexts", LookMode.Value, LookMode.Value);

        List<string> keyOrderList = null;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            keyOrderList = _keyInsertionOrder.ToList();
        }

        Scribe_Collections.Look(ref keyOrderList, "rimtalkKeyOrder");

        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        RimTalkInteractionTexts ??= new Dictionary<string, string>();
            
        if (keyOrderList != null)
        {
            _keyInsertionOrder = new Queue<string>(keyOrderList);
        }
        else
        {
            _keyInsertionOrder = new Queue<string>();
        }
    }

    public void SetTextFor(LogEntry entry, string text)
    {
        if (entry == null || text == null) return;

        string key = entry.GetUniqueLoadID();

        if (RimTalkInteractionTexts.ContainsKey(key))
        {
            RimTalkInteractionTexts[key] = text;
            return;
        }

        while (_keyInsertionOrder.Count >= MaxLogEntries)
        {
            string oldestKey = _keyInsertionOrder.Dequeue();
            RimTalkInteractionTexts.Remove(oldestKey);
        }

        _keyInsertionOrder.Enqueue(key);
        RimTalkInteractionTexts[key] = text;
    }

    public bool TryGetTextFor(LogEntry entry, out string text)
    {
        text = null;
        return entry != null && RimTalkInteractionTexts.TryGetValue(entry.GetUniqueLoadID(), out text);
    }
}