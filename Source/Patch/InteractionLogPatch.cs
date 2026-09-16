using System.Reflection;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Patches;
using RimTalk.Service;
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

    public static void Postfix(LogEntry entry)
    {
        // 1. Fast-path: Skip combat logs and non-interaction entries immediately (< 1ns)
        if (entry is not PlayLogEntry_Interaction interaction) return;

        RimTalkSettings settings = Settings.Get();
        if (settings == null) return;

        // 2. Check if this is a RimTalk interaction
        if (IsRimTalkInteraction(interaction))
        {
            if (settings.BubbleMode == RimTalkSettings.BubbleDisplayMode.Native)
            {
                SpeechBubbleDrawer.AddBubble(interaction);
            }
            return;
        }

        // 3. Process non-RimTalk vanilla interactions
        if (!settings.IsEnabled || !settings.ProcessNonRimTalkInteractions)
        {
            return;
        }

        InteractionDef interactionDef = GetInteractionDef(interaction);
        if (interactionDef == null) return;

        bool isFastTrack = settings.IsFastTrackInteraction(interactionDef.defName);
        bool isChitchat = interactionDef == InteractionDefOf.Chitchat ||
                          interactionDef == InteractionDefOf.DeepTalk;

        Pawn initiator = InitiatorField?.GetValue(interaction) as Pawn;
        if (initiator == null || initiator.Map != Find.CurrentMap) return;

        // Fast-path: Skip immediately if initiator is not cached/eligible or already has queued requests
        var pawnState = Cache.Get(initiator);
        if (pawnState == null || (!isFastTrack && isChitchat && pawnState.TalkRequests.Count > 0))
            return;

        Pawn recipient = RecipientField?.GetValue(interaction) as Pawn;

        // If in danger then stop chitchat
        if (!isFastTrack && isChitchat
            && (initiator.IsInDanger()
                || initiator.GetHostilePawnNearBy() != null
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

        string prompt = interaction.ToGameStringFromPOV(initiator).StripTags();
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

        return initiator.GetRoom() == recipient.GetRoom();
    }

    public static bool IsRimTalkInteraction(LogEntry entry)
    {
        return entry is PlayLogEntry_RimTalkInteraction ||
               (entry is PlayLogEntry_Interaction interaction &&
                InteractionTextPatch.IsRimTalkInteraction(interaction));
    }

    public static InteractionDef GetInteractionDef(LogEntry entry)
    {
        if (entry is PlayLogEntry_Interaction)
        {
            return IntDefField?.GetValue(entry) as InteractionDef;
        }
        if (entry != null)
        {
            return AccessTools.Field(entry.GetType(), "intDef")?.GetValue(entry) as InteractionDef;
        }
        return null;
    }
}
