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

            if (dialogue.IsBroadcast)
            {
                ExecuteBroadcast(initiator, dialogue.Recipient, dialogue.Message);
            }
            else
            {
                ExecuteDialogue(initiator, dialogue.Recipient, dialogue.Message);
            }

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
        if (recipient == null) return;
        ExecuteDialogueInternal(initiator, new[] { recipient }, message);
    }

    private static void ExecuteDialogueInternal(Pawn initiator, IEnumerable<Pawn> recipients, string message)
    {
        PawnState initiatorState = Cache.Get(initiator);
        if (initiatorState == null || !initiatorState.CanDisplayTalk())
            return;

        var recipientStates = new List<PawnState>();

        foreach (var pawn in recipients)
        {
            if (pawn == null || pawn.Destroyed) continue;

            PawnState recipientState = Cache.Get(pawn);
            if (recipientState != null && recipientState.CanDisplayTalk())
            {
                recipientStates.Add(recipientState);
            }
        }

        if (recipientStates.Count == 0)
            return;

        bool isBroadcast = recipientStates.Count > 1;

        ApiLog apiLog;
        if (initiator.IsPlayer())
        {
            apiLog = ApiHistory.AddUserHistory(Settings.Get().PlayerName, message);
            apiLog.SpokenTick = GenTicks.TicksGame;
            Overlay.NotifyLogUpdated();
        }
        else
        {
            apiLog = ApiHistory.AddUserHistory(initiator.LabelShort, message);
        }
        foreach (var recipientState in recipientStates)
        {
            recipientState.AddTalkRequest(message, initiator, TalkType.User);
        }
        if (!initiator.IsPlayer())
        {
            TalkResponse selfResponse = new(TalkType.User, initiator.LabelShort, message)
            {
                Id = apiLog.Id
            };
            initiatorState.TalkResponses.Insert(0, selfResponse);
        }
    }

    public static void ExecuteBroadcast(Pawn initiator, Pawn origin, string message)
    {
        if (initiator == null || initiator.Destroyed) return;
        if (origin == null || origin.Destroyed) origin = initiator;

        var audience = PawnSelector.GetBroadcastTargets(origin,  Settings.Get().Context.MaxPawnContextCount);
        if (audience.NullOrEmpty())
            return;

        ExecuteDialogueInternal(initiator, audience, message);
    }

    public class PendingDialogue(Pawn recipient, string message, bool isBroadcast = false)
    {
        public readonly Pawn Recipient = recipient;
        public readonly string Message = message;
        public readonly bool IsBroadcast = isBroadcast;
    }
}