using System.Collections.Generic;
using System.Linq;
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
    public readonly List<TalkResponse> TalkResponses = [];
    public bool IsGeneratingTalk { get; set; }
    public readonly LinkedList<TalkRequest> TalkRequests = [];
    public HashSet<Hediff> Hediffs { get; set; } = pawn.GetHediffs();

    public string Personality => PersonaService.GetPersonality(Pawn);
    public double TalkInitiationWeight => PersonaService.GetTalkInitiationWeight(Pawn);

    public void AddTalkRequest(string prompt, Pawn recipient = null, TalkType talkType = TalkType.Other)
    {
        if (talkType == TalkType.Urgent)
        {
            var currentNode = TalkRequests.First;
            while (currentNode != null)
            {
                var nextNode = currentNode.Next;
                var request = currentNode.Value;
                if (request.TalkType != TalkType.User)
                {
                    TalkRequests.Remove(currentNode);
                }
                currentNode = nextNode;
            }
        }

        if (talkType == TalkType.User)
        {
            TalkRequests.AddFirst(new TalkRequest(prompt, Pawn, recipient, talkType));
            IgnoreAllTalkResponses();
            Cache.Get(recipient)?.IgnoreAllTalkResponses();
        }
        else if (talkType is TalkType.Event or TalkType.QuestOffer)
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
        if (Pawn.IsPlayer()) return true;
        
        if (WorldRendererUtility.CurrentWorldRenderMode == WorldRenderMode.Planet || Find.CurrentMap == null ||
            Pawn.Map != Find.CurrentMap || !Pawn.Spawned)
        {
            return false;
        }
        
        RimTalkSettings settings = Settings.Get();

        if (!settings.DisplayTalkWhenDrafted && Pawn.Drafted)
            return false;

        if (!settings.ContinueDialogueWhileSleeping && !Pawn.Awake())
            return false;

        return !Pawn.Dead && TalkInitiationWeight > 0;
    }

    public bool CanGenerateTalk()
    {
        if (Pawn.IsPlayer()) return true;
        
        return !IsGeneratingTalk && CanDisplayTalk() && Pawn.Awake() && TalkResponses.Empty() 
               && CommonUtil.HasPassed(LastTalkTick, Settings.Get().TalkInterval);;
    }
    
    public void IgnoreTalkResponse()
    {
        if (TalkResponses.Count == 0) return;
        var talkResponse = TalkResponses[0];
        TalkHistory.AddIgnored(talkResponse.Id);
        TalkResponses.Remove(talkResponse);
        
        var log = ApiHistory.GetApiLog(talkResponse.Id);
        if (log != null) log.SpokenTick = -1;
    }

    public void IgnoreAllTalkResponses(List<TalkType> keepTypes = null)
    {
        if (keepTypes == null)
            while (TalkResponses.Count > 0)
                IgnoreTalkResponse();
        else
            TalkResponses.RemoveAll(response =>
            {
                if (keepTypes.Contains(response.TalkType)) return false;
                TalkHistory.AddIgnored(response.Id);
                return true;
            });
    }
}