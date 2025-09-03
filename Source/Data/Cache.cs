using Verse;
using System.Collections.Concurrent;
using System.Linq;
using RimWorld;
using System.Collections.Generic;

namespace RimTalk.Data
{
    public static class Cache
    {
        private static readonly ConcurrentDictionary<Pawn, PawnState> _cache =
            new ConcurrentDictionary<Pawn, PawnState>();

        public static IEnumerable<Pawn> Keys => _cache.Keys;

        public static PawnState Get(Pawn pawn)
        {
            return pawn == null ? null : _cache.TryGetValue(pawn);
        }

        public static PawnState GetByName(string name)
        {
            return _cache.FirstOrDefault(pair => name.Contains(pair.Key.Name.ToStringShort)).Value;
        }

        public static void Refresh()
        {
            foreach (Pawn pawn in _cache.Keys.ToList())
            {
                if (!pawn.IsFreeColonist || pawn.Dead)
                {
                    _cache.TryRemove(pawn, out _);
                }
            }

            foreach (Pawn pawn in Find.CurrentMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (pawn.IsFreeColonist && !_cache.ContainsKey(pawn))
                {
                    _cache[pawn] = new PawnState(pawn);
                }
            }
        }

        public static bool Contains(Pawn pawn)
        {
            return pawn != null && _cache.ContainsKey(pawn);
        }

        public static void Clear()
        {
            _cache.Clear();
        }

        public static bool IsEmpty()
        {
            return _cache.IsEmpty;
        }
    }
}