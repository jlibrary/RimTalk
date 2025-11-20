using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Patches;

[HarmonyPatch(typeof(BattleLog), nameof(BattleLog.Add))]
public static class BattleLogPatch
{
    private static void Postfix(LogEntry entry)
    {
        var pawnsInvolved = entry.GetConcerns().OfType<Pawn>().ToList();
        if (pawnsInvolved.Count < 2) return;
            
        var initiator = pawnsInvolved[0];
        var recipient = pawnsInvolved[1];
            
        if (Cache.Get(initiator) == null && Cache.Get(recipient) == null) return; 
            
        string prompt = GenerateDirectPrompt(entry, initiator, recipient);

        if (string.IsNullOrEmpty(prompt)) return;
            
        Cache.Get(initiator)?.AddTalkRequest(prompt, recipient, TalkType.Urgent);
        Cache.Get(recipient)?.AddTalkRequest(prompt, initiator, TalkType.Urgent);
            
        var pawns = PawnSelector.GetNearByTalkablePawns(initiator, recipient, PawnSelector.DetectionType.Viewing);
        foreach (var pawn in pawns.Take(2))
        {
            Cache.Get(pawn)?.AddTalkRequest(prompt, initiator, TalkType.Urgent);
        }
    }

    /// <summary>
    /// Generates a prompt for the LLM. It first tries a high-performance, direct-access method.
    /// If that fails (e.g., due to a game update), it logs the error and falls back to the
    /// slower, more stable vanilla game method.
    /// </summary>
    private static string GenerateDirectPrompt(LogEntry entry, Pawn initiator, Pawn recipient)
    {
        try
        {
            string initiatorLabel = $"{initiator.LabelShort}({initiator.GetRole()})";
            string recipientLabel = $"{recipient.LabelShort}({recipient.GetRole()})";
                
            if (entry is BattleLogEntry_RangedImpact impactEntry)
            {
                var traverse = Traverse.Create(impactEntry);
                var weaponDef = traverse.Field<ThingDef>("weaponDef").Value;
                var projectileDef = traverse.Field<ThingDef>("projectileDef").Value;
                string weaponLabel = weaponDef?.label ?? projectileDef?.label ?? "a projectile";

                var deflected = traverse.Field<bool>("deflected").Value;
                var damagedParts = traverse.Field<List<BodyPartRecord>>("damagedParts").Value;

                if (deflected)
                {
                    return $"{initiatorLabel}'s shot with {weaponLabel} at {recipientLabel} was deflected.";
                }
                if (damagedParts == null || damagedParts.Count == 0)
                {
                    return $"{initiatorLabel} missed {recipientLabel} with {weaponLabel}.";
                }
                return $"{initiatorLabel} hit {recipientLabel} with {weaponLabel}.";
            }

            if (entry is BattleLogEntry_MeleeCombat meleeEntry)
            {
                var traverse = Traverse.Create(meleeEntry);
                var ruleDef = traverse.Field<RulePackDef>("ruleDef").Value;
                if (ruleDef == null) return null;

                string ruleDefName = ruleDef.defName;
                string toolLabel = traverse.Field<string>("toolLabel").Value;

                if (ruleDefName == "Combat_MeleeBite") return $"{initiatorLabel} bit {recipientLabel}.";
                if (ruleDefName == "Combat_MeleeScratch") return $"{initiatorLabel} scratched {recipientLabel}.";
                if (!string.IsNullOrEmpty(toolLabel)) return $"{initiatorLabel} hit {recipientLabel} with their {toolLabel}.";

                return $"{initiatorLabel} attacked {recipientLabel} in melee.";
            }
        }
        catch (Exception ex)
        {
            // --- Fallback Path ---
            // The fast path failed, likely due to a game update. 
            Logger.ErrorOnce($"Battle prompt generation failed.\n {ex.Message}", entry.GetHashCode());
                
            // Use the original slow method and strip out any rich text tags.
            return entry.ToGameStringFromPOV(initiator).StripTags();
        }

        return null;
    }
}