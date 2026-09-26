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
    private static readonly System.Reflection.FieldInfo WeaponDefField = AccessTools.Field(typeof(BattleLogEntry_RangedImpact), "weaponDef");
    private static readonly System.Reflection.FieldInfo ProjectileDefField = AccessTools.Field(typeof(BattleLogEntry_RangedImpact), "projectileDef");
    private static readonly System.Reflection.FieldInfo DeflectedField = AccessTools.Field(typeof(BattleLogEntry_RangedImpact), "deflected");
    private static readonly System.Reflection.FieldInfo DamagedPartsField = AccessTools.Field(typeof(BattleLogEntry_RangedImpact), "damagedParts");

    private static readonly System.Reflection.FieldInfo RuleDefField = AccessTools.Field(typeof(BattleLogEntry_MeleeCombat), "ruleDef");
    private static readonly System.Reflection.FieldInfo ToolLabelField = AccessTools.Field(typeof(BattleLogEntry_MeleeCombat), "toolLabel");

    private static int _lastBattleTalkTick = -9999;
    private const int MinBattleTalkIntervalTicks = 180; // 3 seconds cooldown between queuing combat talk bursts

    private static void Postfix(LogEntry entry)
    {
        if (entry == null || !Settings.Get().IsEnabled) return;

        int currentTick = GenTicks.TicksGame;
        if (currentTick - _lastBattleTalkTick < MinBattleTalkIntervalTicks) return;

        Pawn initiator = null;
        Pawn recipient = null;
        var concerns = entry.GetConcerns();
        if (concerns != null)
        {
            foreach (var thing in concerns)
            {
                if (thing is Pawn p)
                {
                    if (initiator == null) initiator = p;
                    else { recipient = p; break; }
                }
            }
        }
        if (initiator == null || recipient == null) return;
            
        var initiatorState = Cache.Get(initiator);
        var recipientState = Cache.Get(recipient);
        if (initiatorState == null && recipientState == null) return; 
            
        string prompt = GenerateDirectPrompt(entry, initiator, recipient);
        if (string.IsNullOrEmpty(prompt)) return;

        _lastBattleTalkTick = currentTick;
            
        initiatorState?.AddTalkRequest(prompt, recipient, TalkType.Urgent);
        recipientState?.AddTalkRequest(prompt, initiator, TalkType.Urgent);
            
        var pawns = PawnSelector.GetNearByTalkablePawns(initiator, recipient, PawnSelector.DetectionType.Viewing);
        for (int i = 0; i < pawns.Count && i < 2; i++)
        {
            Cache.Get(pawns[i])?.AddTalkRequest(prompt, initiator, TalkType.Urgent);
        }
    }

    /// <summary>
    /// Generates a prompt for the LLM using direct cached reflection.
    /// Falls back to vanilla ToGameStringFromPOV if reflection yields null.
    /// </summary>
    private static string GenerateDirectPrompt(LogEntry entry, Pawn initiator, Pawn recipient)
    {
        try
        {
            string initiatorLabel = $"{initiator.LabelShort}({initiator.GetRole()})";
            string recipientLabel = $"{recipient.LabelShort}({recipient.GetRole()})";
                
            if (entry is BattleLogEntry_RangedImpact impactEntry)
            {
                var weaponDef = WeaponDefField?.GetValue(impactEntry) as ThingDef;
                var projectileDef = ProjectileDefField?.GetValue(impactEntry) as ThingDef;
                string weaponLabel = weaponDef?.label ?? projectileDef?.label ?? "a projectile";

                bool deflected = DeflectedField != null && (bool)DeflectedField.GetValue(impactEntry);
                var damagedParts = DamagedPartsField?.GetValue(impactEntry) as List<BodyPartRecord>;

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
                var ruleDef = RuleDefField?.GetValue(meleeEntry) as RulePackDef;
                if (ruleDef == null) return null;

                string ruleDefName = ruleDef.defName;
                string toolLabel = ToolLabelField?.GetValue(meleeEntry) as string;

                if (ruleDefName == "Combat_MeleeBite") return $"{initiatorLabel} bit {recipientLabel}.";
                if (ruleDefName == "Combat_MeleeScratch") return $"{initiatorLabel} scratched {recipientLabel}.";
                if (!string.IsNullOrEmpty(toolLabel)) return $"{initiatorLabel} hit {recipientLabel} with their {toolLabel}.";

                return $"{initiatorLabel} attacked {recipientLabel} in melee.";
            }
        }
        catch (Exception ex)
        {
            Logger.ErrorOnce($"Battle prompt generation failed.\n {ex.Message}", entry.GetHashCode());
            return entry.ToGameStringFromPOV(initiator).StripTags();
        }

        return null;
    }
}