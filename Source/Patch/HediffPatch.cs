using System;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Patch;

[HarmonyPatch(typeof(Pawn_HealthTracker))]
[HarmonyPatch(nameof(Pawn_HealthTracker.AddHediff), new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo), typeof(DamageWorker.DamageResult) })]
public static class HediffPatch
{
    public static void Postfix(Pawn_HealthTracker __instance, Pawn ___pawn, Hediff hediff)
    {
        var pawnState = Cache.Get(___pawn);
        if (pawnState != null && hediff.Visible && !pawnState.Hediffs.Contains(hediff))
        {
            pawnState.Hediffs = ___pawn.GetHediffs();
                
            var prompt = $"{hediff.Part?.Label}-{hediff.LabelCap}";
            pawnState.AddTalkRequest(prompt, talkType: TalkType.Hediff);
        }
    }
}