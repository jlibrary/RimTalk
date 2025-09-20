using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Service;
using RimWorld;
using Verse;
using Random = System.Random;

namespace RimTalk.Data
{
    public static class Cache
    {
        // Main data store mapping a Pawn to its current state.
        private static readonly ConcurrentDictionary<Pawn, PawnState> PawnCache =
            new ConcurrentDictionary<Pawn, PawnState>();

        private static readonly ConcurrentDictionary<string, Pawn> NameCache = new ConcurrentDictionary<string, Pawn>();

        // This Random instance is still needed for the weighted selection method.
        private static readonly Random Random = new Random();

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
            
            // Identify and remove ineligible pawns from all caches.
            foreach (Pawn pawn in PawnCache.Keys.ToList())
            {
                if (!IsEligiblePawn(pawn, settings))
                {
                    if (PawnCache.TryRemove(pawn, out var removedState))
                    {
                        NameCache.TryRemove(removedState.Pawn.Name.ToStringShort, out _);
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
                }
            }
        }

        public static IEnumerable<PawnState> GetAll()
        {
            return PawnCache.Values;
        }

        public static void Clear()
        {
            PawnCache.Clear();
            NameCache.Clear();
        }

        public static bool IsEligiblePawn(Pawn pawn, RimTalkSettings settings)
        {
            if (pawn.DestroyedOrNull() || !pawn.Spawned || pawn.Dead)
                return false;

            if (!pawn.RaceProps.Humanlike)
                return false;

            if (pawn.RaceProps.intelligence < Intelligence.Humanlike)
                return false;
            
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return false;

            if (pawn.skills?.GetSkill(SkillDefOf.Social) == null)
                return false;

            return (pawn.IsFreeColonist ||
                    (settings.AllowSlavesToTalk && pawn.IsSlave) ||
                    (settings.AllowPrisonersToTalk && pawn.IsPrisoner) ||
                    (settings.AllowOtherFactionsToTalk && PawnService.IsVisitor(pawn)) ||
                    (settings.AllowEnemiesToTalk && PawnService.IsInvader(pawn)));
        }

        /// <summary>
        /// Selects a random pawn from the provided list, with selection chance proportional to their TalkInitiationWeight.
        /// </summary>
        /// <param name="pawns">The collection of pawns to select from.</param>
        /// <returns>A single pawn, or null if the list is empty or no pawn has a weight > 0.</returns>
        public static Pawn GetRandomWeightedPawn(IEnumerable<Pawn> pawns)
        {
            var pawnList = pawns.ToList();
            if (pawnList.NullOrEmpty())
            {
                return null;
            }

            var totalWeight = pawnList.Sum(p => Get(p)?.TalkInitiationWeight ?? 0.0);
            
            // If total weight is 0, it means no pawn in the list has a positive weight (chattiness).
            // Based on the rule that weight=0 means "doesn't talk", we must return null
            // as no one is eligible to be selected.
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

            // Fallback in case of floating point inaccuracies, though it should rarely be hit.
            return pawnList.LastOrDefault(p => (Get(p)?.TalkInitiationWeight ?? 0.0) > 0);
        }
    }
}