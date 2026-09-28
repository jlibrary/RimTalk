using System.Collections.Generic;

namespace RimTalk.PawnMemory;

/// <summary>
/// Thread-safe in-memory history buffer storing recent memory and directive transitions for debugging.
/// </summary>
public static class MemoryHistory
{
    private static readonly List<MemoryLogEntry> History = new();
    private static readonly object Lock = new();
    public static int MaxCount { get; set; } = 500;

    public static void Add(MemoryLogEntry entry)
    {
        if (entry == null) return;
        lock (Lock)
        {
            History.Add(entry);
            if (History.Count > MaxCount)
            {
                History.RemoveRange(0, History.Count - MaxCount);
            }
        }
    }

    public static List<MemoryLogEntry> GetAll()
    {
        lock (Lock)
        {
            return new List<MemoryLogEntry>(History);
        }
    }

    public static void Clear()
    {
        lock (Lock)
        {
            History.Clear();
        }
    }
}
