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
        private static readonly ConcurrentDictionary<string, Pawn> _nameCache = new ConcurrentDictionary<string, Pawn>();

        private static readonly object _weightedSelectionLock = new object();
        private static List<Pawn> _weightedPawnList = new List<Pawn>();
        private static List<double> _cumulativeWeights = new List<double>();
        private static double _totalWeight = 0.0;
        private static bool _weightsDirty = true;

        public static IEnumerable<Pawn> Keys => _cache.Keys;

        public static PawnState Get(Pawn pawn)
        {
            return pawn == null ? null : _cache.TryGetValue(pawn, out var state) ? state : null;
        }

        /// <summary>
        /// Gets a pawn's state using a fast dictionary lookup by name.
        /// </summary>
        public static PawnState GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _nameCache.TryGetValue(name, out var pawn) ? Get(pawn) : null;
        }

        public static void Refresh()
        {
            var settings = Settings.Get();
            var updated = false;

            // Identify and remove ineligible pawns from all caches.
            foreach (Pawn pawn in _cache.Keys.ToList())
            {
                if (!IsEligiblePawn(pawn, settings))
                {
                    if (_cache.TryRemove(pawn, out var removedState))
                    {
                        _nameCache.TryRemove(removedState.Pawn.Name.ToStringShort, out _);
                        updated = true;
                    }
                }
            }

            // Add new eligible pawns to all caches.
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned)
            {
                if (IsEligiblePawn(pawn, settings) && !_cache.ContainsKey(pawn))
                {
                    _cache[pawn] = new PawnState(pawn);
                    _nameCache[pawn.Name.ToStringShort] = pawn;
                    updated = true;
                }
            }

            // Mark weights as dirty when cache changes
            if (updated)
            {
                lock (_weightedSelectionLock)
                {
                    _weightsDirty = true;
                }

                RebuildWeights();
            }
        }
            
        public static IEnumerable<Pawn> GetWeightedPawns()
        {
            return _cache.Keys.Where(p => Get(p)?.TalkInitiationWeight > 0);
        }
    
        public static IEnumerable<(Pawn pawn, double weight)> GetPawnsWithWeights()
        {
            return _cache.Keys.Select(p => (p, Get(p)?.TalkInitiationWeight ?? 0.0))
                .Where(x => x.Item2 > 0);
        }

        public static bool Contains(Pawn pawn)
        {
            return pawn != null && _cache.ContainsKey(pawn);
        }

        public static void Clear()
        {
            _cache.Clear();
            _nameCache.Clear();
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

        // Build weighted when cache marked dirty
        private static void RebuildWeights()
        {
            lock (_weightedSelectionLock)
            {
                if (!_weightsDirty) return;

                _weightedPawnList.Clear();
                _cumulativeWeights.Clear();
                _totalWeight = 0.0;

                foreach (var pawn in _cache.Keys)
                {
                    var weight = Get(pawn)?.TalkInitiationWeight ?? 0.0;
                    if (weight > 0)
                    {
                        _weightedPawnList.Add(pawn);
                        _totalWeight += weight;
                        _cumulativeWeights.Add(_totalWeight);
                    }
                }

                _weightsDirty = false;
            }
        }
    }
}