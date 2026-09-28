using HarmonyLib;
using RimTalk.PawnMemory;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

/// <summary>
/// Intercepts medical treatment (tending wounds and disease)
/// to record relational memories between doctor and patient.
/// </summary>
[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
public static class TendPatch_DoTend
{
    public static void Postfix(Pawn doctor, Pawn patient, Medicine medicine)
    {
        MemoryHookService.TryRecordTend(doctor, patient);
    }
}
