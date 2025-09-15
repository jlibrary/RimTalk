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
        private const float HearingRange = 10f;
        private const float ViewingRange = 20f;

        public enum DetectionType
        {
            Hearing,
            Viewing,
        }

        private static List<Pawn> GetNearbyPawnsInternal(Pawn pawn1, Pawn pawn2 = null,
            DetectionType detectionType = DetectionType.Hearing, bool onlyTalkable = false, int maxResults = 10)
        {
            float baseRange = detectionType == DetectionType.Hearing ? HearingRange : ViewingRange;
            PawnCapacityDef capacityDef = detectionType == DetectionType.Hearing
                ? PawnCapacityDefOf.Hearing
                : PawnCapacityDefOf.Sight;

            return Cache.Keys
                .Where(p => p != pawn1 && p != pawn2)
                .Where(p => !onlyTalkable || Cache.Get(p).CanGenerateTalk())
                .Where(p => p.health.capacities.GetLevel(capacityDef) > 0.0)
                .Where(p =>
                {
                    var room = p.GetRoom();
                    var capacityLevel = p.health.capacities.GetLevel(capacityDef);
                    var detectionDistance = baseRange * capacityLevel;

                    bool nearPawn1 = room == pawn1.GetRoom() &&
                                     p.Position.InHorDistOf(pawn1.Position, detectionDistance);

                    if (pawn2 == null) return nearPawn1;

                    bool nearPawn2 = room == pawn2.GetRoom() &&
                                     p.Position.InHorDistOf(pawn2.Position, detectionDistance);

                    return nearPawn1 || nearPawn2;
                })
                .OrderBy(p => pawn2 == null
                    ? pawn1.Position.DistanceTo(p.Position)
                    : Math.Min(pawn1.Position.DistanceTo(p.Position),
                        pawn2.Position.DistanceTo(p.Position)))
                .Take(maxResults)
                .ToList();
        }

        public static List<Pawn> GetNearByTalkablePawns(Pawn pawn1, Pawn pawn2 = null,
            DetectionType detectionType = DetectionType.Hearing)
        {
            return GetNearbyPawnsInternal(pawn1, pawn2, detectionType, onlyTalkable: true);
        }

        public static List<Pawn> GetAllNearByPawns(Pawn pawn1, Pawn pawn2 = null)
        {
            return GetNearbyPawnsInternal(pawn1, pawn2, DetectionType.Hearing, onlyTalkable: false);
        }

        public static Pawn SelectAvailablePawnByWeight(bool noInvader = false)
        {
            var availablePawns = Cache.Keys.Where(p => Cache.Get(p).CanGenerateTalk(noInvader));
            return Cache.GetRandomWeightedPawn(availablePawns);
        }
    }
}