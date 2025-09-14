using System.Linq;
using HarmonyLib;
using RimTalk.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.Patches
{
    [HarmonyPatch(typeof(BattleLog), nameof(BattleLog.Add))]
    public static class BattleLogPatch
    {
        private static void Postfix(LogEntry entry)
        {
            var firstTwo = entry.GetConcerns().Take(2).ToArray();

            Logger.Message(
                $"BattleLog.Add triggered! Entry: {entry.ToGameStringFromPOV(null).StripTags()} ::: {firstTwo.Length} ");

            if (firstTwo.Length == 0) return;

            var initiator = firstTwo[0] is Pawn p1 ? p1 : null;
            var recipient = (firstTwo.Length > 1 && firstTwo[1] is Pawn p2) ? p2 : null;

            var prompt = entry.ToGameStringFromPOV(firstTwo[0]).StripTags();
            if (recipient != null && PawnService.IsInvader(recipient))
            {
                prompt = prompt.Replace(recipient.LabelShort, PawnService.GetPawnName(initiator, recipient));
            }

            Logger.Message(prompt);
            if (!TalkService.GenerateTalk(prompt, initiator, recipient))
            {
                if (recipient != null && !TalkService.GenerateTalk(prompt, recipient, initiator))
                {
                    // Find talking pawns that are close to the event
                    var nearbyTalkers =
                        PawnSelector.GetNearByTalkablePawns(initiator, recipient, PawnSelector.DetectionType.Viewing);
                    foreach (var pawn in nearbyTalkers)
                        if (TalkService.GenerateTalk(prompt, pawn))
                            break;
                }
            }
        }
    }
}