using System.Collections.Generic;
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
        // 1. If Urgent, clear out less important active requests
        if (talkType == TalkType.Urgent)
        {
            var currentNode = TalkRequests.First;
            while (currentNode != null)
            {
                var nextNode = currentNode.Next;
                var request = currentNode.Value;
                
                // If we overwrite a request, send it to global history as expired/overwritten
                if (!request.TalkType.IsFromUser())
                {
                    TalkRequestPool.AddToHistory(request, RequestStatus.Expired);
                    TalkRequests.Remove(currentNode);
                }
                currentNode = nextNode;
            }
        }

        // 2. Create and Enqueue
        var newRequest = new TalkRequest(prompt, Pawn, recipient, talkType) { Status = RequestStatus.Pending };

        if (talkType.IsFromUser())
        {
            TalkRequests.AddFirst(newRequest);
            IgnoreAllTalkResponses();
            Cache.Get(recipient)?.IgnoreAllTalkResponses();
            UserRequestPool.Add(Pawn);
        }
        else if (talkType is TalkType.Event or TalkType.QuestOffer)
        {
            TalkRequests.AddFirst(newRequest);
        }
        else
        {
            TalkRequests.AddLast(newRequest);   
        }
    }
    
    public TalkRequest GetNextTalkRequest()
    {
        var node = TalkRequests.First;
        while (node != null)
        {
            var request = node.Value;
            var next = node.Next;
        
            if (!request.IsExpired())
                return request;
            
            TalkRequestPool.AddToHistory(request, RequestStatus.Expired);
            TalkRequests.Remove(node);
            node = next;
        }
        return null;
    }

    public void MarkRequestSpoken(TalkRequest request)
    {
        TalkRequestPool.AddToHistory(request, RequestStatus.Processed);
        TalkRequests.Remove(request);
    }

    public bool CanDisplayTalk()
    {
        if (Pawn.IsPlayer()) return true;
        
        if (WorldRendererUtility.CurrentWorldRenderMode == WorldRenderMode.Planet || Find.CurrentMap == null ||
            Pawn.Map != Find.CurrentMap || !Pawn.Spawned)
            return false;
        
        RimTalkSettings settings = Settings.Get();
        if (!settings.DisplayTalkWhenDrafted && Pawn.Drafted) return false;
        if (!settings.ContinueDialogueWhileSleeping && !Pawn.Awake()) return false;

        return !Pawn.Dead && TalkInitiationWeight > 0;
    }

    public bool CanGenerateTalk()
    {
        if (Pawn.IsPlayer()) return true;
        return !IsGeneratingTalk && CanDisplayTalk() && Pawn.Awake() && TalkResponses.Empty() 
               && CommonUtil.HasPassed(LastTalkTick, RimTalkSettings.ReplyInterval);
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