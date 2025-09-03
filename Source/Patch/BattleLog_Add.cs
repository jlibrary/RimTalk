using System.Linq;
using HarmonyLib;
using RimTalk.Service;
using RimWorld;
using Verse;

namespace RimTalk.Patch
{
    [HarmonyPatch(typeof(BattleLog), nameof(BattleLog.Add))]
    public static class BattleLog_Add
    {
        private static void Postfix(LogEntry entry)
        {
            var firstTwo = entry.GetConcerns().Take(2).ToArray();
            
            // Check if array has any elements before accessing
            if (firstTwo.Length == 0)
                return;
                
            var initiator = firstTwo[0] is Pawn p1 ? p1 : null; 
            var recipient = (firstTwo.Length > 1 && firstTwo[1] is Pawn p2) ? p2 : null;
            var text = entry.ToGameStringFromPOV(firstTwo[0]).StripTags();

            if (recipient != null && recipient.Faction.HostileTo(Faction.OfPlayer))
            {
                text = text.Replace(recipient.LabelShort, $"{recipient.KindLabel}(enemy)");
            }
            
            if (!TalkService.GenerateTalk(text, initiator, recipient))
                if (recipient != null && !TalkService.GenerateTalk(text, recipient, initiator))
                    foreach (var pawn in PawnService.GetPawnsAbleToTalk())
                        if (TalkService.GenerateTalk(text, pawn))
                            break;
        }
    }
}