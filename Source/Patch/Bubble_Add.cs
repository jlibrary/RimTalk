using System;
using System.Linq;
using Bubbles.Core;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(Bubbler), nameof(Bubbler.Add))]
    public static class Bubbler_Add
    {
        private static bool _originalDraftedValue;

        public static bool Prefix(LogEntry entry)
        {
            // Store original value and override if needed
            CurrentWorkDisplayModSettings settings = Settings.Get();
            
            // For RimTalk interaction, display normal bubble
            if (entry is PlayLogEntry_RimTalkInteraction)
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

            Pawn initiator = (Pawn)entry.GetConcerns().First();
            Pawn recipient = GetRecipient(entry);

            // If the setting to process non-RimTalk interactions is disabled, show the original bubble.
            if (!settings.ProcessNonRimTalkInteractions)
            {
                return true;
            }
            
            // if in danger then stop chitchat
            if (entry is PlayLogEntry_Interaction &&
                (PawnService.IsPawnInDanger(initiator) 
                || PawnService.HostilePawnNearBy(initiator) != null
                || !PawnSelector.GetNearByTalkablePawns(initiator).Contains(recipient)))
            {
                return false;
            }
            
            // Otherwise, block normal bubble and generate talk
            var prompt = entry.ToGameStringFromPOV(initiator).StripTags();
            Cache.Get(initiator)?.AddTalkRequest(prompt, recipient);
            
            return false; // Show original bubble
        }

        public static void Postfix()
        {
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
            Pawn recipient = null;
            foreach (Thing thing in entry.GetConcerns().Skip(1))
            { 
                if (thing is Pawn) {
                    recipient = thing as Pawn;
                    break;
                }
            }
            return recipient;
        }
    }
}