using System;
using RimTalk.Patch;
using RimTalk.Source.Data;
using RimTalk.Util;
using Verse;

namespace RimTalk.Data;

public enum RequestStatus
{
    Pending,
    Processed,
    Expired
}

public class TalkRequest(string prompt, Pawn initiator, Pawn recipient = null, TalkType talkType = TalkType.Other)
{
    public TalkType TalkType { get; set; } = talkType;
    public string Context { get; set; }
    public string Prompt { get; set; } = prompt;
    public Pawn Initiator { get; set; } = initiator;
    public Pawn Recipient { get; set; } = recipient;
    public int MapId { get; set; }
    public int CreatedTick { get; set; } = GenTicks.TicksGame;
    public DateTime CreatedTime { get; set; } = DateTime.Now; 
    public int FinishedTick { get; set; } = -1; 
    public RequestStatus Status { get; set; } = RequestStatus.Pending; 
    public bool IsMonologue;

    public bool IsExpired()
    {
        int duration = 20;
        if (TalkType.IsFromUser()) return false;
        if (TalkType == TalkType.Urgent)
        {
            duration = 5;
            if (!Initiator.IsInDanger())
            {
                return true;
            }
        } else if (TalkType == TalkType.Thought)
        {
            return !ThoughtTracker.IsThoughtStillActive(Initiator, Prompt);
        }
        return GenTicks.TicksGame - CreatedTick > CommonUtil.GetTicksForDuration(duration);
    }
    
    public TalkRequest Clone()
    {
        return (TalkRequest) MemberwiseClone();
    }
}