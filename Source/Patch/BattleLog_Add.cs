using System.Linq;
using RimTalk.Service;
using RimWorld;
using Verse;

public static class BattleLog_Add
{
    private static void Postfix(LogEntry entry)
    {
        var firstTwo = entry.GetConcerns().Take(2).ToArray();
        if (firstTwo.Length == 0) return;
            
        var initiator = firstTwo[0] is Pawn p1 ? p1 : null; 
        var recipient = (firstTwo.Length > 1 && firstTwo[1] is Pawn p2) ? p2 : null;
        
        // Find talking pawns that are close to the event
        float maxDistance = 10f;
        var nearbyTalkers = PawnService.GetPawnsAbleToTalk().Where(talker =>
        {
            if (initiator?.MapHeld == talker.MapHeld && 
                talker.Position.DistanceTo(initiator.Position) <= maxDistance)
                return true;
            if (recipient?.MapHeld == talker.MapHeld && 
                talker.Position.DistanceTo(recipient.Position) <= maxDistance)
                return true;
            return false;
        }).ToList();
        
        if (!nearbyTalkers.Any()) return;

        var text = entry.ToGameStringFromPOV(firstTwo[0]).StripTags();
        if (recipient != null && recipient.Faction.HostileTo(Faction.OfPlayer))
        {
            text = text.Replace(recipient.LabelShort, $"{recipient.KindLabel}(enemy)");
        }
        
        if (!TalkService.GenerateTalk(text, initiator, recipient))
            if (recipient != null && !TalkService.GenerateTalk(text, recipient, initiator))
                foreach (var pawn in nearbyTalkers)
                    if (TalkService.GenerateTalk(text, pawn))
                        break;
    }
}