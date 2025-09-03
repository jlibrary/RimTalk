using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace RimTalk.Data
{
    public static class TalkHistory
    {
        private static readonly ConcurrentDictionary<Guid, int> _cache;
            
        static TalkHistory()
        {
            _cache = new ConcurrentDictionary<Guid, int>(new Dictionary<Guid, int>
            {
                { Guid.Empty, 0 }
            });
        }        
        // Add a new talk with the current game tick
        public static void AddTalk(Guid id)
        {
            _cache.TryAdd(id, Find.TickManager.TicksGame);
        }
        
        public static int Get(Guid id)
        {
            return _cache.TryGetValue(id, -1);
        }
        
        public static void Clear()
        {
            _cache.Clear();
            _cache.TryAdd(Guid.Empty, 0);
        }
    }
}