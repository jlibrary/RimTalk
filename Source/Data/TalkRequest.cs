using RimTalk.Patch;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data;

public class TalkRequest
{
    public TalkType TalkType { get; set; }
    public string Prompt { get; set; }
    public Pawn Initiator { get; set; }
    public Pawn Recipient { get; set; }
    public int MapId { get; set; }
    public int CreatedTick { get; set; }

    public TalkRequest(string prompt, Pawn initiator, Pawn recipient = null, TalkType talkType = TalkType.Other)
    {
        TalkType = talkType;
        Prompt = prompt;
        Initiator = initiator;
        Recipient = recipient;
        CreatedTick = GenTicks.TicksGame;
    }

    public bool IsExpired()
    {
        int duration = 10;
        if (TalkType == TalkType.Urgent)
        {
            duration = 5;
            if (!PawnService.IsPawnInDanger(Initiator))
            {
                return true;
            }
        } else if (TalkType == TalkType.Thought)
        {
            return !ThoughtTracker.IsThoughtStillActive(Initiator, Prompt);
        }
        return GenTicks.TicksGame - CreatedTick > CommonUtil.GetTicksForDuration(duration);
    }
}