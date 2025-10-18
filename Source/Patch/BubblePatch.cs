using System;
using System.Linq;
using Bubbles.Core;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Patches;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(Bubbler), nameof(Bubbler.Add))]
public static class Bubbler_Add
{
    private static bool _originalDraftedValue;

    public static bool Prefix(LogEntry entry)
    {
        RimTalkSettings settings = Settings.Get();

        Pawn initiator = (Pawn)entry.GetConcerns().First();
        Pawn recipient = GetRecipient(entry);
        var prompt = entry.ToGameStringFromPOV(initiator).StripTags();

        // For RimTalk interaction, display normal bubble
        if (IsRimTalkInteraction(entry))
        {
            if (settings.DisplayTalkWhenDrafted)
                try
                {
                    _originalDraftedValue = Bubbles.Settings.DoDrafted.Value;
                    Bubbles.Settings.DoDrafted.Value = true;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to override bubble drafted setting: {ex.Message}");
                }

            return true;
        }

        // If Rimtalk disabled or  non-RimTalk interactions is disabled, show the original bubble.
        if (!settings.IsEnabled || !settings.ProcessNonRimTalkInteractions)
        {
            return true;
        }
            
        InteractionDef interactionDef = GetInteractionDef(entry);
        if (interactionDef == null) return true;
        bool isChitchat = interactionDef == InteractionDefOf.Chitchat ||
                          interactionDef == InteractionDefOf.DeepTalk;

        // if in danger then stop chitchat
        if (isChitchat
            && (initiator.IsInDanger()
                || initiator.GetHostilePawnNearBy() != null
                || !PawnSelector.GetNearByTalkablePawns(initiator).Contains(recipient)))
        {
            return false;
        }

        PawnState pawnState = Cache.Get(initiator);

        // chitchat is ignored if talkRequest exists
        if (pawnState == null || (isChitchat && pawnState.TalkRequests.Count > 0))
            return false;

        // Otherwise, block normal bubble and generate talk
        prompt = $"{prompt} ({interactionDef.label})";
        pawnState.AddTalkRequest(prompt, recipient, TalkType.Chitchat);
        return false;
    }

    public static void Postfix()
    {
        // Roll back original bubble settings for drafted
        if (Settings.Get().DisplayTalkWhenDrafted)
        {
            try
            {
                Bubbles.Settings.DoDrafted.Value = _originalDraftedValue;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to restore bubble drafted setting: {ex.Message}");
            }
        }
    }

    private static Pawn GetRecipient(LogEntry entry)
    {
        return entry.GetConcerns().Skip(1).OfType<Pawn>().FirstOrDefault();
    }

    private static bool IsRimTalkInteraction(LogEntry entry)
    {
        return entry is PlayLogEntry_RimTalkInteraction ||
               (entry is PlayLogEntry_Interaction interaction &&
                InteractionTextPatch.IsRimTalkInteraction(interaction));
    }

    private static InteractionDef GetInteractionDef(LogEntry entry)
    {
        var field = AccessTools.Field(entry.GetType(), "intDef");
        return field?.GetValue(entry) as InteractionDef;
    }
}