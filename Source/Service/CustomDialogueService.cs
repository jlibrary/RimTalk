using System.Collections.Generic;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.UI;
using RimTalk.Util;
using Verse;
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

            ExecuteDialogue(initiator, dialogue.Recipient, dialogue.Message);
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
        // Player talking to a pawn is always allowed
        if (initiator.IsPlayer()) return true;

        float distance = initiator.Position.DistanceTo(recipient.Position);
        return distance <= TalkDistance && InSameRoom(initiator, recipient);
    }

    public static void ExecuteDialogue(Pawn initiator, Pawn recipient, string message)
    {
        PawnState initiatorState = Cache.Get(initiator);
        if (initiatorState == null || !initiatorState.CanDisplayTalk())
            return;

        PawnState recipientState = Cache.Get(recipient);
        if (recipientState != null && recipientState.CanDisplayTalk())
            recipientState.AddTalkRequest(message, initiator, TalkType.User);

        ApiLog apiLog = ApiHistory.AddUserHistory(initiator, recipient, message);
        
        if (initiator.IsPlayer())
        {
            apiLog.SpokenTick = GenTicks.TicksGame;
            Overlay.NotifyLogUpdated();
        }
        else
        {
            TalkResponse talkResponse = new(TalkType.User, initiator.LabelShort, message)
            {
                Id = apiLog.Id
            };
            Cache.Get(initiator).TalkResponses.Insert(0, talkResponse);
        }
    }

    public class PendingDialogue(Pawn recipient, string message)
    {
        public readonly Pawn Recipient = recipient;
        public readonly string Message = message;
    }
}