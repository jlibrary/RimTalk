using System.Collections.Generic;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimTalk.Data;

public class PawnState(Pawn pawn)
{
    public readonly Pawn Pawn = pawn;
    public string Context { get; set; }
    public int LastTalkTick { get; set; } = 0;
    public string LastStatus { get; set; } = "";
    public int RejectCount { get; set; }
    public readonly Queue<TalkResponse> TalkResponses = new();
    public bool IsGeneratingTalk { get; set; }
    public readonly LinkedList<TalkRequest> TalkRequests = new();
    public HashSet<Hediff> Hediffs { get; set; } = PawnService.GetHediffs(pawn);

    public string Personality => PersonaService.GetPersonality(Pawn);
    public double TalkInitiationWeight => PersonaService.GetTalkInitiationWeight(Pawn);

    public void AddTalkRequest(string prompt, Pawn recipient = null, TalkType talkType = TalkType.Other)
    {
        if (talkType == TalkType.Urgent)
        {
            TalkRequests.Clear();
        }

        if (talkType == TalkType.Event)
        {
            TalkRequests.AddFirst(new TalkRequest(prompt, Pawn, recipient, talkType));
        }
        else
        {
            TalkRequests.AddLast(new TalkRequest(prompt, Pawn, recipient, talkType));    
        }
    }
    
    public TalkRequest GetNextTalkRequest()
    {
        while (TalkRequests.Count > 0)
        {
            var request = TalkRequests.First.Value;
            if (request.IsExpired())
            {
                TalkRequests.RemoveFirst();
                continue;
            }
            return request;
        }
        return null;
    }

    public bool CanDisplayTalk()
    {
        if (WorldRendererUtility.CurrentWorldRenderMode == WorldRenderMode.Planet || Find.CurrentMap == null ||
            Pawn.Map != Find.CurrentMap || !Pawn.Spawned)
        {
            return false;
        }

        if (!Settings.Get().DisplayTalkWhenDrafted && Pawn.Drafted)
            return false;

        return Pawn.Awake()
               && !Pawn.Dead
               && Pawn.CurJobDef != JobDefOf.LayDown
               && Pawn.CurJobDef != JobDefOf.LayDownAwake
               && Pawn.CurJobDef != JobDefOf.LayDownResting
               && TalkInitiationWeight > 0;
    }

    public bool CanGenerateTalk()
    {
        return !IsGeneratingTalk && CanDisplayTalk() && TalkResponses.Empty() 
               && CommonUtil.HasPassed(LastTalkTick, Settings.Get().TalkInterval);;
    }
}