using System.Collections.Generic;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.UI;
using RimTalk.Util;
using Verse;
using Verse.AI;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Service;

public static class CustomDialogueService
{
    private const float TalkDistance = 20f;
    public static readonly Dictionary<Pawn, PendingDialogue> PendingDialogues = new();

    public static void Tick()
    {
        List<Pawn> toRemove = [];

        foreach (var (initiator, dialogue) in PendingDialogues)
        {
            // Check if pawn is still valid
            if (initiator == null || initiator.Destroyed || dialogue.Recipient == null || dialogue.Recipient.Destroyed)
            {
                toRemove.Add(initiator);
                continue;
            }

            if (!CanTalk(initiator, dialogue.Recipient)) continue;

            ExecuteDialogue(initiator, dialogue.Recipient, dialogue.Message, dialogue.IsAnnouncement, dialogue.ImageBase64);
            toRemove.Add(initiator);
        }

        foreach (Pawn pawn in toRemove)
        {
            PendingDialogues.Remove(pawn);
        }
    }

    private static bool InSameRoom(Pawn pawn1, Pawn pawn2)
    {
        Room room1 = pawn1.GetRoom();
        Room room2 = pawn2.GetRoom();
        return (room1 != null && room2 != null && room1 == room2) ||
               (room1 == null && room2 == null); // Both outdoors
    }

    public static bool CanTalk(Pawn initiator, Pawn recipient)
    {
        if (initiator == null || recipient == null) return false;

        // Player talking to a pawn is always allowed
        if (initiator.IsPlayer()) return true;

        float distance = initiator.Position.DistanceTo(recipient.Position);
        return distance <= TalkDistance && InSameRoom(initiator, recipient);
    }

    public static void DispatchDialogue(Pawn initiator, Pawn recipient, string message, bool isAnnouncement = false, string imageBase64 = null)
    {
        if (initiator == null) return;

        if (CanTalk(initiator, recipient))
        {
            ExecuteDialogue(initiator, recipient, message, isAnnouncement, imageBase64);
        }
        else
        {
            PendingDialogues[initiator] = new PendingDialogue(recipient, message, isAnnouncement, imageBase64);

            if (recipient != null && initiator.jobs != null)
            {
                Job job = JobMaker.MakeJob(RimWorld.JobDefOf.Goto, recipient);
                job.playerForced = true;
                job.collideWithPawns = false;
                job.locomotionUrgency = LocomotionUrgency.Jog;
                initiator.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }
    }

    public static void ExecuteDialogue(Pawn initiator, Pawn recipient, string message, bool isAnnouncement = false)
    {
        ExecuteDialogue(initiator, recipient, message, isAnnouncement, null);
    }

    public static void ExecuteDialogue(Pawn initiator, Pawn recipient, string message, bool isAnnouncement, string imageBase64)
    {
        PawnState initiatorState = Cache.Get(initiator);
        if (initiatorState == null || !initiatorState.CanDisplayTalk())
            return;

        TalkType talkType = isAnnouncement ? TalkType.Announcement : TalkType.User;
        int conversationId = isAnnouncement && recipient == null ? -1 : ApiHistory.NextConversationId();

        if (isAnnouncement)
        {
            Pawn primaryPawn = initiator.IsPlayer() ? recipient : initiator;
            Pawn otherPawn = initiator.IsPlayer() ? initiator : recipient;

            PawnState primaryState = Cache.Get(primaryPawn);
            if (primaryState != null && primaryState.CanDisplayTalk())
            {
                var request = new TalkRequest(message, primaryPawn, otherPawn, talkType)
                {
                    ConversationId = conversationId,
                    ImageBase64 = imageBase64
                };
                primaryState.TalkRequests.AddFirst(request);
                primaryState.IgnoreAllTalkResponses();
                UserRequestPool.Add(primaryPawn);
            }
        }
        else
        {
            PawnState recipientState = Cache.Get(recipient);
            if (recipientState != null && recipientState.CanDisplayTalk())
            {
                recipientState.AddTalkRequest(message, initiator, talkType, imageBase64);
                if (recipientState.TalkRequests.First != null)
                    recipientState.TalkRequests.First.Value.ConversationId = conversationId;
            }
        }

        if (AIService.IsBusy())
        {
            AIService.CancelCurrent();
        }

        ApiLog apiLog = ApiHistory.AddUserHistory(initiator, recipient, message, talkType, imageBase64, conversationId);
        
        if (initiator.IsPlayer())
        {
            apiLog.SpokenTick = GenTicks.TicksGame;
            Overlay.NotifyLogUpdated();
        }
        else
        {
            TalkResponse talkResponse = new(talkType, initiator.LabelShort, message)
            {
                Id = apiLog.Id
            };
            Cache.Get(initiator).TalkResponses.Insert(0, talkResponse);
        }
    }

    public class PendingDialogue(Pawn recipient, string message, bool isAnnouncement = false, string imageBase64 = null)
    {
        public readonly Pawn Recipient = recipient;
        public readonly string Message = message;
        public readonly bool IsAnnouncement = isAnnouncement;
        public readonly string ImageBase64 = imageBase64;
    }
}