using System;
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
        private static readonly ConcurrentDictionary<Pawn, PawnState> PawnCache =
            new ConcurrentDictionary<Pawn, PawnState>();

        private static readonly ConcurrentDictionary<string, Pawn> NameCache = new ConcurrentDictionary<string, Pawn>();

        private static readonly object WeightedSelectionLock = new object();
        private static readonly Random Random = new Random();
        private static readonly List<Pawn> WeightedPawnList = new List<Pawn>();
        private static readonly List<double> CumulativeWeights = new List<double>();
        private static double _totalWeight = 0.0;
        private static bool _weightsDirty = true;

        public static IEnumerable<Pawn> Keys => PawnCache.Keys;

        public static PawnState Get(Pawn pawn)
        {
            return pawn == null ? null : PawnCache.TryGetValue(pawn, out var state) ? state : null;
        }

        /// <summary>
        /// Gets a pawn's state using a fast dictionary lookup by name.
        /// </summary>
        public static PawnState GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return NameCache.TryGetValue(name, out var pawn) ? Get(pawn) : null;
        }

        public static void Refresh()
        {
            var settings = Settings.Get();
            var updated = false;

            // Identify and remove ineligible pawns from all caches.
            foreach (Pawn pawn in PawnCache.Keys.ToList())
            {
                if (!IsEligiblePawn(pawn, settings))
                {
                    if (PawnCache.TryRemove(pawn, out var removedState))
                    {
                        NameCache.TryRemove(removedState.Pawn.Name.ToStringShort, out _);
                        updated = true;
                    }
                }
            }

            // Add new eligible pawns to all caches.
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned)
            {
                if (IsEligiblePawn(pawn, settings) && !PawnCache.ContainsKey(pawn))
                {
                    PawnCache[pawn] = new PawnState(pawn);
                    NameCache[pawn.Name.ToStringShort] = pawn;
                    updated = true;
                }
            }

            // Mark weights as dirty when cache changes
            if (updated)
            {
                lock (WeightedSelectionLock)
                {
                    _weightsDirty = true;
                }

                RebuildWeights();
            }
        }

        public static IEnumerable<Pawn> GetWeightedPawns()
        {
            return PawnCache.Keys.Where(p => Get(p)?.TalkInitiationWeight > 0);
        }

        public static IEnumerable<(Pawn pawn, double weight)> GetPawnsWithWeights()
        {
            return PawnCache.Keys.Select(p => (p, Get(p)?.TalkInitiationWeight ?? 0.0))
                .Where(x => x.Item2 > 0);
        }

        public static IEnumerable<PawnState> GetAll()
        {
            return PawnCache.Values;
        }

        public static bool Contains(Pawn pawn)
        {
            return pawn != null && PawnCache.ContainsKey(pawn);
        }

        public static void Clear()
        {
            PawnCache.Clear();
            NameCache.Clear();
        }

        public static bool IsEligiblePawn(Pawn pawn, CurrentWorkDisplayModSettings settings)
        {
            if (pawn.DestroyedOrNull() || !pawn.Spawned)
                return false;

            if (!pawn.RaceProps.Humanlike)
                return false;

            return !pawn.Dead && (pawn.IsFreeColonist ||
                                  (settings.AllowSlavesToTalk && pawn.IsSlave) ||
                                  (settings.AllowPrisonersToTalk && pawn.IsPrisoner) ||
                                  (settings.AllowOtherFactionsToTalk && PawnService.IsVisitor(pawn)) ||
                                  (settings.AllowEnemiesToTalk && PawnService.IsInvader(pawn)));
        }

        // Build weighted when cache marked dirty
        private static void RebuildWeights()
        {
            lock (WeightedSelectionLock)
            {
                if (!_weightsDirty) return;

                WeightedPawnList.Clear();
                CumulativeWeights.Clear();
                _totalWeight = 0.0;

                foreach (var pawn in PawnCache.Keys)
                {
                    var weight = Get(pawn)?.TalkInitiationWeight ?? 0.0;
                    if (weight > 0)
                    {
                        WeightedPawnList.Add(pawn);
                        _totalWeight += weight;
                        CumulativeWeights.Add(_totalWeight);
                    }
                }

                _weightsDirty = false;
            }
        }

        public static Pawn GetRandomWeightedPawn(IEnumerable<Pawn> pawns)
        {
            var pawnList = pawns.ToList();
            if (pawnList.NullOrEmpty())
            {
                return null;
            }

            var totalWeight = pawnList.Sum(p => Get(p)?.TalkInitiationWeight ?? 0.0);
            if (totalWeight <= 0)
            {
                return null;
            }

            var randomWeight = Random.NextDouble() * totalWeight;
            var cumulativeWeight = 0.0;

            foreach (var pawn in pawnList)
            {
                cumulativeWeight += Get(pawn)?.TalkInitiationWeight ?? 0.0;
                if (randomWeight < cumulativeWeight)
                {
                    return pawn;
                }
            }

            return pawnList.LastOrDefault();
        }
    }
}