using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using Verse;

namespace RimTalk.Patches
{
    [HarmonyPatch(typeof(BattleLog), nameof(BattleLog.Add))]
    public static class BattleLogPatch
    {
        private static void Postfix(LogEntry entry)
        {
            var pawnsInvolved = entry.GetConcerns().OfType<Pawn>().ToList();

            if (pawnsInvolved.Count < 2) return;

            var initiator = pawnsInvolved[0];
            var recipient = pawnsInvolved[1];

            var prompt = entry.ToGameStringFromPOV(initiator).StripTags();
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