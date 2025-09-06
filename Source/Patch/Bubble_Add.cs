using System;
using System.Linq;
using Bubbles.Core;
using HarmonyLib;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(Bubbler), nameof(Bubbler.Add))]
    public static class Bubbler_Add
    {
        private static bool originalDraftedValue;

        public static bool Prefix(LogEntry entry)
        {
            // Store original value and override if needed
            CurrentWorkDisplayModSettings settings = Settings.Get();
            
            // For RimTalk interaction, display normal bubble
            if (entry is PlayLogEntry_RimTalkInteraction)
            {
                if (settings.displayTalkWhenDrafted)
                    try
                    {
                        originalDraftedValue = Bubbles.Settings.DoDrafted.Value;
                        Bubbles.Settings.DoDrafted.Value = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to override bubble drafted setting: {ex.Message}");
                    }
                return true;
            }

            Pawn initiator = (Pawn)entry.GetConcerns().First();

            // If the setting to process non-RimTalk interactions is disabled, show the original bubble.
            if (!settings.processNonRimTalkInteractions)
            {
                return true;
            }
            if (IsHostilesOnMapForPawn(initiator))
            {
                return false;
            }
            
            // Otherwise, block normal bubble and generate talk
            // if generation fails, fall back to normal bubble or suppress based on setting
            var text = entry.ToGameStringFromPOV(initiator).StripTags();
            bool processed = TalkService.GenerateTalk(text, initiator, GetRecipient(entry));
            
            // If not processed and user wants to suppress unprocessed messages, block the bubble
            if (!processed && settings.suppressUnprocessedMessages)
            {
                return false; // Suppress the message completely
            }
            
            return !processed; // Show original bubble only if not processed and suppression is disabled
        }

        public static void Postfix()
        {
            if (Settings.Get().displayTalkWhenDrafted)
            {
                try
                {
                    Bubbles.Settings.DoDrafted.Value = originalDraftedValue;
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
        
        private static bool IsHostilesOnMapForPawn(Pawn pawn)
        {
            if (pawn?.Map == null) return false;

            return pawn.Map.mapPawns.AllPawnsSpawned
                .Any(other => other != pawn && other.HostileTo(pawn));
        }
    }
}