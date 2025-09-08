using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.Service
{
    public class PawnSelector
    {
        public const float HearingRange = 8f;
        private const float ViewingRange = 20f;
        private static readonly Random _random = new Random();

        public enum DetectionType
        {
            Hearing,
            Viewing,
        }
        
        public static List<Pawn> GetNearByTalkablePawns(Pawn pawn1, Pawn pawn2 = null,
            DetectionType detectionType = DetectionType.Hearing)
        {
            float baseRange = detectionType == DetectionType.Hearing ? HearingRange : ViewingRange;
            PawnCapacityDef capacityDef = detectionType == DetectionType.Hearing
                ? PawnCapacityDefOf.Hearing
                : PawnCapacityDefOf.Sight;

            return Cache.Keys
                .Where(nearbyPawn => nearbyPawn != pawn1 && nearbyPawn != pawn2)
                .Where(nearbyPawn => Cache.Get(nearbyPawn).CanGenerateTalk())
                .Where(nearbyPawn => nearbyPawn.health.capacities.GetLevel(capacityDef) > 0.0)
                .Where(nearbyPawn =>
                {
                    var room = nearbyPawn.GetRoom();
                    var capacityLevel = nearbyPawn.health.capacities.GetLevel(capacityDef);
                    var detectionDistance = baseRange * capacityLevel;

                    // Check if nearby pawn is in range of pawn1
                    bool nearPawn1 = room == pawn1.GetRoom() &&
                                     nearbyPawn.Position.InHorDistOf(pawn1.Position, detectionDistance);

                    // If pawn2 is null, only check pawn1
                    if (pawn2 == null)
                        return nearPawn1;

                    // Check if nearby pawn is in range of pawn2
                    bool nearPawn2 = room == pawn2.GetRoom() &&
                                     nearbyPawn.Position.InHorDistOf(pawn2.Position, detectionDistance);

                    return nearPawn1 || nearPawn2;
                })
                .OrderBy(nearbyPawn => pawn2 == null
                    ? pawn1.Position.DistanceTo(nearbyPawn.Position)
                    : Math.Min(pawn1.Position.DistanceTo(nearbyPawn.Position),
                        pawn2.Position.DistanceTo(nearbyPawn.Position)))
                .Take(10)
                .ToList();
        }
        
        public static List<Pawn> GetAllNearByPawns(Pawn pawn)
        {
            float baseRange = HearingRange;
            PawnCapacityDef capacityDef = PawnCapacityDefOf.Sight;
            
            return Find.CurrentMap.mapPawns.AllPawnsSpawned
                .Where(nearbyPawn => nearbyPawn != pawn)
                .Where(nearbyPawn =>
                {
                    var room = nearbyPawn.GetRoom();
                    var capacityLevel = nearbyPawn.health.capacities.GetLevel(capacityDef);
                    var detectionDistance = baseRange * capacityLevel;

                    // Check if nearby pawn is in range of pawn1
                    bool nearPawn = room == pawn.GetRoom() &&
                                     nearbyPawn.Position.InHorDistOf(pawn.Position, detectionDistance);
                    
                    return nearPawn;
                })
                .OrderBy(nearbyPawn => pawn.Position.DistanceTo(nearbyPawn.Position))
                .Take(10)
                .ToList();
        }

        public static Pawn SelectAvailablePawnByWeight(bool noInvader = false)
        {
            var pawnsWithWeights = Cache.GetPawnsWithWeights().ToList();

            if (!pawnsWithWeights.Any())
                return null;

            // Build cumulative weights
            var cumulativeWeights = new List<double>();
            var totalWeight = 0.0;

            foreach (var (pawn, weight) in pawnsWithWeights)
            {
                totalWeight += weight;
                cumulativeWeights.Add(totalWeight);
            }

            if (totalWeight == 0)
                return pawnsWithWeights.FirstOrDefault(x => Cache.Get(x.pawn).CanGenerateTalk(noInvader)).pawn;

            // Selection with linear probing
            for (int attempts = 0; attempts < 10; attempts++)
            {
                var randomWeight = _random.NextDouble() * totalWeight;
                var index = cumulativeWeights.BinarySearch(randomWeight);
                if (index < 0) index = ~index;
                if (index >= pawnsWithWeights.Count) index = pawnsWithWeights.Count - 1;

                // Linear probe for available pawn
                for (int i = 0; i < pawnsWithWeights.Count; i++)
                {
                    var currentIndex = (index + i) % pawnsWithWeights.Count;
                    var pawn = pawnsWithWeights[currentIndex].pawn;

                    if (Cache.Get(pawn).CanGenerateTalk(noInvader))
                        return pawn;
                }
            }

            return null;
        }
    }
}