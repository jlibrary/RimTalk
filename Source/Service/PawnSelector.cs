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
        DetectionType detectionType = DetectionType.Hearing, bool onlyTalkable = false, int maxResults = 10, bool isAnnouncement = false)
    {
        float baseRange = detectionType == DetectionType.Hearing 
            ? (isAnnouncement ? AnnouncementHearingRange : HearingRange) 
            : ViewingRange;
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

    public static List<Pawn> GetAllNearByPawns(Pawn pawn1, Pawn pawn2 = null, bool isAnnouncement = false)
    {
        return GetNearbyPawnsInternal(pawn1, pawn2, DetectionType.Hearing, onlyTalkable: false, isAnnouncement: isAnnouncement);
    }

    public static Pawn SelectNextAvailablePawn()
    {
        Pawn pawnWithOldestUserRequest = null;
        Pawn pawnWithSpecialRequest = null;
        int oldestTick = int.MaxValue;
        var talkReadyPawns = new List<Pawn>();

        // Find the pawn with the highest priority task:
        // 1. The oldest user-initiated talk request (absolute priority).
        // 2. Pawns with pending special talk requests (second priority).
        // 3. Pawns that can talk normally (for fallback).
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
                if (req.TalkType.IsFromUser())
                {
                    if (req.CreatedTick < oldestTick)
                    {
                        oldestTick = req.CreatedTick;
                        pawnWithOldestUserRequest = pawn;
                    }
                }
                else if (canTalk && pawnWithSpecialRequest == null &&
                         req.TalkType is TalkType.Interaction or TalkType.Other or TalkType.Urgent or TalkType.Event or TalkType.QuestOffer)
                {
                    pawnWithSpecialRequest = pawn;
                }
            }
        }

        // Return the highest priority pawn found, or null if none are available.
        return pawnWithOldestUserRequest ?? 
               pawnWithSpecialRequest ?? 
               (talkReadyPawns.Count > 0 ? Cache.GetRandomWeightedPawn(talkReadyPawns) : null);
    }
}