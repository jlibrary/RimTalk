using System.Linq;
using RimTalk.Service;
using Verse;

public static class BattleLog_Add
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
            prompt = prompt.Replace(recipient.LabelShort, PawnService.GetPawnName(initiator, recipient));;
        }
        
        if (!TalkService.GenerateTalk(prompt, initiator, recipient))
            if (recipient != null && !TalkService.GenerateTalk(prompt, recipient, initiator))
            {
                // Find talking pawns that are close to the event
                var nearbyTalkers = PawnSelector.GetNearByTalkablePawns(initiator, recipient, PawnSelector.DetectionType.Viewing);
                foreach (var pawn in nearbyTalkers)
                    if (TalkService.GenerateTalk(prompt, pawn))
                        break;
            }
    }
}