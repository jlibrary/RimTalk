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
                        NameCache.TryRemove(removedState.Pawn.LabelShort, out _);
                    }
                }
            }

            // Add new eligible pawns to all caches.
            foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawnsSpawned)
            {
                if (IsEligiblePawn(pawn, settings) && !PawnCache.ContainsKey(pawn))
                {
                    PawnCache[pawn] = new PawnState(pawn);
                    NameCache[pawn.LabelShort] = pawn;
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
        
        private static double GetScaleFactor(double groupWeight, double colonistWeight)
        {
            if (colonistWeight <= 0) return 0.0; // If colonists can't talk, no one else should.
            if (groupWeight > colonistWeight) return colonistWeight / groupWeight;
            return 1.0;
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
            
            // 1. Categorize pawns and calculate the total weight for each group.
            double totalColonistWeight = 0.0;
            double totalVisitorWeight = 0.0;
            double totalEnemyWeight = 0.0;
            double totalSlaveWeight = 0.0;
            double totalPrisonerWeight = 0.0;

            foreach (var p in pawnList)
            {
                var weight = Get(p)?.TalkInitiationWeight ?? 0.0;
                if (p.IsFreeColonist) totalColonistWeight += weight;
                else if (p.IsSlave) totalSlaveWeight += weight;
                else if (p.IsPrisoner) totalPrisonerWeight += weight;
                else if (PawnService.IsVisitor(p)) totalVisitorWeight += weight;
                else if (PawnService.IsInvader(p)) totalEnemyWeight += weight;
            }
            
            // 2. Determine scaling factors using the private helper method.
            var visitorScaleFactor = GetScaleFactor(totalVisitorWeight, totalColonistWeight);
            var enemyScaleFactor = GetScaleFactor(totalEnemyWeight, totalColonistWeight);
            var slaveScaleFactor = GetScaleFactor(totalSlaveWeight, totalColonistWeight);
            var prisonerScaleFactor = GetScaleFactor(totalPrisonerWeight, totalColonistWeight);

            // 3. Calculate the new, effective total weight for the weighted random selection.
            var effectiveTotalWeight = pawnList.Sum(p =>
            {
                var weight = Get(p)?.TalkInitiationWeight ?? 0.0;
                if (p.IsFreeColonist) return weight;
                if (p.IsSlave) return weight * slaveScaleFactor;
                if (p.IsPrisoner) return weight * prisonerScaleFactor;
                if (PawnService.IsVisitor(p)) return weight * visitorScaleFactor;
                if (PawnService.IsInvader(p)) return weight * enemyScaleFactor;
                return 0; // Should not be reached, but safe.
            });

            // If total weight is 0, it means no pawn is eligible to be selected.
            if (effectiveTotalWeight <= 0)
            {
                return null;
            }
            
            var randomWeight = Random.NextDouble() * effectiveTotalWeight;
            var cumulativeWeight = 0.0;

            foreach (var pawn in pawnList)
            {
                // Apply the scaling factor during the selection loop
                var currentPawnWeight = Get(pawn)?.TalkInitiationWeight ?? 0.0;
                
                if (pawn.IsFreeColonist) cumulativeWeight += currentPawnWeight;
                else if (pawn.IsSlave) cumulativeWeight += currentPawnWeight * slaveScaleFactor;
                else if (pawn.IsPrisoner) cumulativeWeight += currentPawnWeight * prisonerScaleFactor;
                else if (PawnService.IsVisitor(pawn)) cumulativeWeight += currentPawnWeight * visitorScaleFactor;
                else if (PawnService.IsInvader(pawn)) cumulativeWeight += currentPawnWeight * enemyScaleFactor;
                
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