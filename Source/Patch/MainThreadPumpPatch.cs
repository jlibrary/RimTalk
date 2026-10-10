using HarmonyLib;
using RimTalk.Error;
using Verse;

namespace RimTalk.Patch;

// Runs every frame in menus and in game, so work queued from background threads via
// AIErrorHandler.EnqueueMessage reaches the main thread even when the game is paused or not loaded.
// LongEventHandler.ExecuteWhenFinished is not a substitute: outside a long event it runs on the calling thread.
[HarmonyPatch(typeof(Root), nameof(Root.Update))]
internal static class MainThreadPumpPatch
{
    public static void Postfix()
    {
        AIErrorHandler.DrainPendingMessages();
    }
}
