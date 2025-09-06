using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Service;
using Verse;

namespace RimTalk.Data
{
    public static class Cache
    {
        // Main data store mapping a Pawn to its current state.
        private static readonly ConcurrentDictionary<Pawn, PawnState> _cache = new ConcurrentDictionary<Pawn, PawnState>();
        
        // --- Optimizations ---
        // Provides fast O(1) lookup of a pawn by their short name string.
        private static readonly ConcurrentDictionary<string, Pawn> _nameCache = new ConcurrentDictionary<string, Pawn>();
        // A list of all pawns, kept permanently sorted by their LastTalkTick to avoid re-sorting.
        private static List<Pawn> _talkersSortedByTick = new List<Pawn>();

        public static IEnumerable<Pawn> Keys => _cache.Keys;
        public static IReadOnlyList<Pawn> TalkersSortedByTick => _talkersSortedByTick;

        public static PawnState Get(Pawn pawn)
        {
            return pawn == null ? null : _cache.TryGetValue(pawn, out var state) ? state : null;
        }

        public static List<Pawn> GetList()
        {
            return _talkersSortedByTick;
        }

        /// <summary>
        /// Gets a pawn's state using a fast dictionary lookup by name.
        /// </summary>
        public static PawnState GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _nameCache.TryGetValue(name, out var pawn) ? Get(pawn) : null;
        }
        
        /// <summary>
        /// Efficiently updates a pawn's position in the sorted list after its LastTalkTick changes.
        /// </summary>
        public static void UpdatePawnSortPosition(Pawn pawn)
        {
            // O(N) removal, but only for a single element.
            _talkersSortedByTick.Remove(pawn);

            var pawnState = Get(pawn);
            if (pawnState == null) return;

            // Use a binary search (O(log N)) to find the correct insertion point.
            var comparer = Comparer<Pawn>.Create((p1, p2) => Get(p1).LastTalkTick.CompareTo(Get(p2).LastTalkTick));
            int index = _talkersSortedByTick.BinarySearch(pawn, comparer);
            
            if (index < 0)
            {
                // If not found, the bitwise complement gives the correct sorted index.
                index = ~index;
            }

            // O(N) insertion as elements are shifted.
            _talkersSortedByTick.Insert(index, pawn);
        }

        /// <summary>
        /// Refreshes the cache, removing ineligible pawns and adding new ones.
        /// </summary>
        public static void Refresh()
        {
            var settings = Settings.Get();
            var pawnsToRemove = new List<Pawn>();

            // Identify and remove ineligible pawns from all caches.
            foreach (Pawn pawn in _cache.Keys.ToList())
            {
                if (!IsEligiblePawn(pawn, settings))
                {
                    if (_cache.TryRemove(pawn, out var removedState))
                    {
                        _nameCache.TryRemove(removedState.pawn.Name.ToStringShort, out _);
                        pawnsToRemove.Add(pawn);
                    }
                }
            }
            
            if (pawnsToRemove.Any())
            {
                _talkersSortedByTick.RemoveAll(p => pawnsToRemove.Contains(p));
            }

            // Add new eligible pawns to all caches.
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned)
            {
                if (IsEligiblePawn(pawn, settings) && !_cache.ContainsKey(pawn))
                {
                    _cache[pawn] = new PawnState(pawn);
                    _nameCache[pawn.Name.ToStringShort] = pawn;
                    // New pawns haven't talked, so insert at the beginning of the sorted list.
                    _talkersSortedByTick.Insert(0, pawn);
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
            _nameCache.Clear();
            _talkersSortedByTick.Clear();
        }

        public static bool IsEligiblePawn(Pawn pawn, CurrentWorkDisplayModSettings settings)
        {
            if (!pawn.RaceProps.Humanlike)
                return false;
            
            return !pawn.Dead && (pawn.IsFreeColonist ||
                         (settings.allowSlavesToTalk && pawn.IsSlave) ||
                         (settings.allowPrisonersToTalk && pawn.IsPrisoner) ||
                         (settings.allowOtherFactionsToTalk && PawnService.IsVisitor(pawn)) ||
                         (settings.allowEnemiesToTalk && PawnService.IsInvader(pawn)));
        }
    }
}