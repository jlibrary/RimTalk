using HarmonyLib;
using RimTalk.PawnMemory;
using RimWorld;

namespace RimTalk.Patch;

/// <summary>
/// Intercepts major colony milestones and personal tales (rescue, fights, nursing)
/// to record episodic memories.
/// </summary>
[HarmonyPatch(typeof(TaleManager), nameof(TaleManager.Add))]
public static class TalePatch_Add
{
    public static void Postfix(Tale tale)
    {
        MemoryHookService.TryRecordTale(tale);
    }
}
