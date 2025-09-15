using System.Linq;
using HarmonyLib;
using RimTalk.Data;
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

            if (firstTwo.Length == 0) return;

            var initiator = firstTwo[0] is Pawn p1 ? p1 : null;
            var recipient = (firstTwo.Length > 1 && firstTwo[1] is Pawn p2) ? p2 : null;

            var prompt = entry.ToGameStringFromPOV(firstTwo[0]).StripTags();
            if (recipient != null && PawnService.IsInvader(recipient))
            {
                prompt = prompt.Replace(recipient.LabelShort, PawnService.GetPawnName(initiator, recipient));
            }
            
            Cache.Get(initiator)?.AddTalkRequest(prompt, recipient);
            Cache.Get(recipient)?.AddTalkRequest(prompt, initiator);
            
            var pawns = PawnSelector.GetNearByTalkablePawns(initiator, recipient, PawnSelector.DetectionType.Viewing);
            foreach (var pawn in pawns.Take(2))
            {
                Cache.Get(pawn)?.AddTalkRequest(prompt, initiator);
            }
        }
    }
}