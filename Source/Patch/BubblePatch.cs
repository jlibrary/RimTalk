using System;
using System.Linq;
using Bubbles.Core;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(Bubbler), nameof(Bubbler.Add))]
    public static class Bubbler_Add
    {
        private static bool _originalDraftedValue;

        public static bool Prefix(LogEntry entry)
        {
            CurrentWorkDisplayModSettings settings = Settings.Get();
            
            Pawn initiator = (Pawn)entry.GetConcerns().First();
            Pawn recipient = GetRecipient(entry);
            var prompt = entry.ToGameStringFromPOV(initiator).StripTags();
            
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
            
            if (!settings.ProcessNonRimTalkInteractions)
            {
                return true;
            }
            
            if (entry is PlayLogEntry_Interaction &&
                (PawnService.IsPawnInDanger(initiator) 
                || PawnService.HostilePawnNearBy(initiator) != null
                || !PawnSelector.GetNearByTalkablePawns(initiator).Contains(recipient)))
            {
                return false;
            }
            
            Cache.Get(initiator)?.AddTalkRequest(prompt, recipient);
            
            return false;
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
            return entry.GetConcerns().Skip(1).OfType<Pawn>().FirstOrDefault();
        }

        private static bool IsRimTalkInteraction(LogEntry entry)
        {
            return entry is PlayLogEntry_Interaction interaction &&
                InteractionTextPatch.IsRimTalkInteraction(interaction);
        }
    }
}