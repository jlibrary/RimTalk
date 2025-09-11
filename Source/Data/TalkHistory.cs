using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace RimTalk.Data
{
    public static class TalkHistory
    {
        private static readonly ConcurrentDictionary<Guid, int> Cache;
            
        static TalkHistory()
        {
            Cache = new ConcurrentDictionary<Guid, int>(new Dictionary<Guid, int>
            {
                { Guid.Empty, 0 }
            });
        }        
        // Add a new talk with the current game tick
        public static void AddTalk(Guid id)
        {
            Cache.TryAdd(id, Find.TickManager.TicksGame);
        }
        
        public static int Get(Guid id)
        {
            return Cache.TryGetValue(id, -1);
        }
        
        public static void Clear()
        {
            Cache.Clear();
            Cache.TryAdd(Guid.Empty, 0);
        }
    }
}