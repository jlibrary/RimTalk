using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Patches;
using RimTalk.Source.Data;
using RimTalk.UI;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
public static class InteractionLogPatch
{
    private static readonly FieldInfo IntDefField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "intDef");
    private static readonly FieldInfo InitiatorField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "initiator");
    private static readonly FieldInfo RecipientField = AccessTools.Field(typeof(PlayLogEntry_Interaction), "recipient");

    private static readonly FieldInfo SingleIntDefField = AccessTools.Field(typeof(PlayLogEntry_InteractionSinglePawn), "intDef");
    private static readonly FieldInfo SingleInitiatorField = AccessTools.Field(typeof(PlayLogEntry_InteractionSinglePawn), "initiator");

    public static void Postfix(LogEntry entry)
    {
        // 1. Fast-path: Skip combat logs and non-interaction entries immediately (< 1ns)
        if (entry is not PlayLogEntry_Interaction &&
            entry is not PlayLogEntry_InteractionSinglePawn)
        {
            return;
        }

        RimTalkSettings settings = Settings.Get();
        if (settings == null) return;

        // 2. Check if this is a RimTalk interaction
        if (IsRimTalkInteraction(entry))
        {
            if (settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.Native)
            {
                SpeechBubbleDrawer.AddBubble(entry);
            }
            return;
        }

        // 3. Process non-RimTalk vanilla interactions
        if (!settings.IsEnabled || !settings.ProcessNonRimTalkInteractions)
        {
            return;
        }

        InteractionDef interactionDef = GetInteractionDef(entry);
        if (interactionDef == null) return;

        bool isFastTrack = settings.IsFastTrackInteraction(interactionDef.defName);

        // Skip vanilla procedural chitchat and deep talk unless explicitly enabled as fast-track
        if (!isFastTrack && (interactionDef == InteractionDefOf.Chitchat || interactionDef == InteractionDefOf.DeepTalk))
            return;

        Pawn initiator;
        Pawn recipient = null;

        if (entry is PlayLogEntry_Interaction)
        {
            initiator = InitiatorField?.GetValue(entry) as Pawn;
            recipient = RecipientField?.GetValue(entry) as Pawn;
        }
        else
        {
            initiator = SingleInitiatorField?.GetValue(entry) as Pawn;
        }

        if (initiator == null || initiator.Map != Find.CurrentMap) return;

        // Fast-path: Skip immediately if initiator is not cached/eligible or already has queued requests
        var pawnState = Cache.Get(initiator);
        if (pawnState == null || (!isFastTrack && pawnState.TalkRequests.Count > 0))
            return;

        // If in danger then stop non-fast-track interactions
        if (!isFastTrack
            && (initiator.IsInDanger()
                || (recipient != null && !IsRecipientNearbyAndTalkable(initiator, recipient))))
        {
            return;
        }
        if (isFastTrack)
        {
            pawnState.DrainIncomingTalkResponses();
            if (pawnState.IsGeneratingTalk || pawnState.TalkResponses.Count > 0)
                return;

            if (recipient != null)
            {
                PawnState recipientState = Cache.Get(recipient);
                recipientState?.DrainIncomingTalkResponses();
                if (recipientState != null && (recipientState.IsGeneratingTalk || recipientState.TalkResponses.Count > 0))
                    return;
            }
        }

        string prompt = entry.ToGameStringFromPOV(initiator)?.StripTags();
        if (string.IsNullOrWhiteSpace(prompt)) return;
        prompt = $"{prompt} ({interactionDef.label})";
        pawnState.AddTalkRequest(prompt, recipient, isFastTrack ? TalkType.Interaction : TalkType.Chitchat);
    }

    private static bool IsRecipientNearbyAndTalkable(Pawn initiator, Pawn recipient)
    {
        if (recipient == null || recipient == initiator || recipient.Map != initiator.Map) return false;

        var recipientState = Cache.Get(recipient);
        if (recipientState == null || !recipientState.CanGenerateTalk()) return false;

        float hearingLevel = (float)recipient.health.capacities.GetLevel(PawnCapacityDefOf.Hearing);
        if (hearingLevel <= 0f) return false;

        float detectionDistance = 10f * hearingLevel;
        if (!initiator.Position.InHorDistOf(recipient.Position, detectionDistance)) return false;

        return initiator.GetRoom() == recipient.GetRoom() ||
               GenSight.LineOfSight(initiator.Position, recipient.Position, initiator.Map);
    }

    public static bool IsRimTalkInteraction(LogEntry entry)
    {
        return entry is PlayLogEntry_RimTalkInteraction ||
               (entry is PlayLogEntry_Interaction or PlayLogEntry_InteractionSinglePawn &&
                InteractionTextPatch.IsRimTalkInteraction(entry));
    }

    public static InteractionDef GetInteractionDef(LogEntry entry)
    {
        return entry switch
        {
            PlayLogEntry_Interaction => IntDefField?.GetValue(entry) as InteractionDef,
            PlayLogEntry_InteractionSinglePawn => SingleIntDefField?.GetValue(entry) as InteractionDef,
            _ => null
        };
    }
}
