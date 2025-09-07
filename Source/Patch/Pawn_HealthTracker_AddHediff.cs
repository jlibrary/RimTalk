using HarmonyLib;
using Verse;
using RimTalk.Service;
using System;
using RimTalk.Data;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(Pawn_HealthTracker))]
    [HarmonyPatch(nameof(Pawn_HealthTracker.AddHediff), new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo), typeof(DamageWorker.DamageResult) })]
    public static class Pawn_HealthTracker_AddHediff
    {
        public static void Postfix(Pawn_HealthTracker __instance, Pawn ___pawn, Hediff hediff)
        {
            var pawnState = Cache.Get(___pawn);
            if (pawnState != null && hediff.Visible && !pawnState.Hediffs.Contains(hediff))
            {
                pawnState.Hediffs = PawnService.GetHediffs(___pawn);
                
                var prompt = $"{hediff.Part?.Label}-{hediff.LabelCap}";
                pawnState.AddTalkRequest(prompt);
            }
        }
    }
}