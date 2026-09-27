using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

namespace RimTalk.Service;

public class PawnSelector
{
    private const float HearingRange = 10f;
    private const float AnnouncementHearingRange = 30f;
    private const float ViewingRange = 20f;

    public enum DetectionType
    {
        Hearing,
        Viewing,
    }

    private static List<Pawn> GetNearbyPawnsInternal(Pawn pawn1, Pawn pawn2 = null,
        DetectionType detectionType = DetectionType.Hearing, bool onlyTalkable = false, bool isAnnouncement = false)
    {
        if (pawn1 == null || !pawn1.Spawned) return [];

        int configuredCount = Settings.Get()?.Context?.MaxPawnContextCount ?? 3;
        // Keep candidate pool generous (floor of 10) so downstream callers have enough candidates before final Take(MaxPawnContextCount)
        int effectiveMaxResults = Math.Max(10, configuredCount);

        float baseRange = detectionType == DetectionType.Hearing 
            ? isAnnouncement ? AnnouncementHearingRange : HearingRange 
            : ViewingRange;
        PawnCapacityDef capacityDef = detectionType == DetectionType.Hearing
            ? PawnCapacityDefOf.Hearing
            : PawnCapacityDefOf.Sight;

        var room1 = pawn1.GetRoom();
        var room2 = pawn2?.GetRoom();
        var pos1 = pawn1.Position;
        var pos2 = pawn2?.Position ?? IntVec3.Invalid;
        bool hasPawn2 = pawn2 != null;

        return Cache.Keys
            .Where(p => p != pawn1 && p != pawn2)
            .Where(p => !onlyTalkable || Cache.Get(p)?.CanGenerateTalk() == true)
            .Where(p =>
            {
                var capacityLevel = p.health?.capacities?.GetLevel(capacityDef) ?? 0f;
                if (capacityLevel <= 0.0f) return false;

                var detectionDistance = baseRange * capacityLevel;
                var room = p.GetRoom();

                bool nearPawn1 = room == room1 &&
                                 p.Position.InHorDistOf(pos1, detectionDistance);

                if (!hasPawn2) return nearPawn1;

                bool nearPawn2 = room == room2 &&
                                 p.Position.InHorDistOf(pos2, detectionDistance);

                return nearPawn1 || nearPawn2;
            })
            .OrderBy(p => !hasPawn2
                ? pos1.DistanceToSquared(p.Position)
                : Math.Min(pos1.DistanceToSquared(p.Position), pos2.DistanceToSquared(p.Position)))
            .Take(effectiveMaxResults)
            .ToList();
    }

    public static List<Pawn> GetNearByTalkablePawns(Pawn pawn1, Pawn pawn2 = null,
        DetectionType detectionType = DetectionType.Hearing)
    {
        return GetNearbyPawnsInternal(pawn1, pawn2, detectionType, onlyTalkable: true);
    }

    public static List<Pawn> GetAllNearByPawns(Pawn pawn1, Pawn pawn2 = null, bool isAnnouncement = false)
    {
        return GetNearbyPawnsInternal(pawn1, pawn2, DetectionType.Hearing, onlyTalkable: false, isAnnouncement: isAnnouncement);
    }

    public static Pawn SelectNextAvailablePawn()
    {
        Pawn pawnWithOldestUserRequest = null;
        int oldestTick = int.MaxValue;
        var talkReadyPawns = new List<Pawn>();

        // Find the pawn with the highest priority task:
        // 1. The oldest user-initiated talk request (absolute priority).
        // 2. Pawns that can talk normally (weighted random selection based on TalkInitiationWeight).
        foreach (var pawn in Cache.Keys)
        {
            var pawnState = Cache.Get(pawn);
            if (pawnState == null) continue;

            bool canTalk = pawnState.CanGenerateTalk();
            if (canTalk)
            {
                talkReadyPawns.Add(pawn);
            }

            for (var node = pawnState.TalkRequests.First; node != null; node = node.Next)
            {
                var req = node.Value;
                if (req.TalkType.IsFromUser() && req.CreatedTick < oldestTick)
                {
                    oldestTick = req.CreatedTick;
                    pawnWithOldestUserRequest = pawn;
                }
            }
        }

        // Return the highest priority pawn found, or null if none are available.
        return pawnWithOldestUserRequest ?? 
               (talkReadyPawns.Count > 0 ? Cache.GetRandomWeightedPawn(talkReadyPawns) : null);
    }
}