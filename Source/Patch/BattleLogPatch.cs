using System.Linq;
using HarmonyLib;
using RimTalk.Service;
using Verse;
using Cache = RimTalk.Data.Cache;

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

            if (!initiator.RaceProps.Humanlike) return;
            
            var prompt = entry.ToGameStringFromPOV(initiator).StripTags();
            if (recipient != null && PawnService.IsInvader(recipient))
            {
                string name = PawnService.GetPawnName(initiator, recipient);
                if (name != null)
                {
                    prompt = prompt.Replace(recipient.LabelShort, name);
                }
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