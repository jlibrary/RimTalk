using System.Collections.Concurrent;
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
    private readonly ConcurrentQueue<TalkResponse> _incomingTalkResponses = new();
    // Same hazard as AIService._busy: set on the main thread, cleared in a finally on a
    // threadpool thread. An auto-property gives no memory barrier, so a stale `true` here
    // would silence this pawn for good.
    private volatile bool _isGeneratingTalk;
    public bool IsGeneratingTalk
    {
        get => _isGeneratingTalk;
        set => _isGeneratingTalk = value;
    }
    public readonly LinkedList<TalkRequest> TalkRequests = [];
    
    public HashSet<Hediff> Hediffs { get; set; } = pawn.GetHediffs();

    public string Personality => PersonaService.GetPersonality(Pawn);
    public double TalkInitiationWeight => PersonaService.GetTalkInitiationWeight(Pawn);

    public void AddTalkRequest(string prompt, Pawn recipient = null, TalkType talkType = TalkType.Other)
    {
        AddTalkRequest(prompt, recipient, talkType, null);
    }

    public void AddTalkRequest(string prompt, Pawn recipient, TalkType talkType, string imageBase64)
    {
        // Enemies should never receive colony incident, quest, or thought prompts
        if (Pawn.IsEnemy() && talkType is TalkType.Event or TalkType.QuestOffer or TalkType.QuestEnd or TalkType.Thought)
        {
            return;
        }

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
        var newRequest = new TalkRequest(prompt, Pawn, recipient, talkType)
        {
            Status = RequestStatus.Pending,
            IsMonologue = recipient == null,
            ImageBase64 = imageBase64
        };

        if (talkType.IsFromUser())
        {
            TalkRequests.AddFirst(newRequest);
            IgnoreAllTalkResponses();
            Cache.Get(recipient)?.IgnoreAllTalkResponses();
            UserRequestPool.Add(Pawn, priority: true);
        }
        else if (talkType == TalkType.Interaction)
        {
            TalkRequests.AddFirst(newRequest);
            IgnoreAllTalkResponses();
            Cache.Get(recipient)?.IgnoreAllTalkResponses();
            LastTalkTick = 0;
            UserRequestPool.Add(Pawn, priority: false);
        }
        else if (talkType == TalkType.Sleep)
        {
            TalkRequests.AddFirst(newRequest);
            LastTalkTick = 0;
            UserRequestPool.Add(Pawn, priority: false);
        }
        else if (talkType is TalkType.Event or TalkType.QuestOffer or TalkType.Other)
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

    /// <summary>
    /// Thread-safe hand-off for background threads to submit a talk response. Call
    /// <see cref="DrainIncomingTalkResponses"/> from the main thread before reading
    /// <see cref="TalkResponses"/> to move queued entries in.
    /// </summary>
    public void QueueIncomingResponse(TalkResponse talkResponse)
    {
        _incomingTalkResponses.Enqueue(talkResponse);
    }

    /// <summary>
    /// Moves any responses queued from background threads into <see cref="TalkResponses"/>.
    /// Must only be called from the main thread.
    /// </summary>
    public void DrainIncomingTalkResponses()
    {
        while (_incomingTalkResponses.TryDequeue(out var talkResponse))
            TalkResponses.Add(talkResponse);
    }

    public bool CanDisplayTalk()
    {
        if (Pawn.IsPlayer()) return true;
        
        if (WorldRendererUtility.CurrentWorldRenderMode == WorldRenderMode.Planet || Find.CurrentMap == null ||
            Pawn.Map != Find.CurrentMap || !Pawn.Spawned)
            return false;
        
        RimTalkSettings settings = Settings.Get();
        if (!settings.DisplayTalkWhenDrafted && Pawn.Drafted) return false;
        bool allowSleeping = TalkResponses.Count > 0 && TalkResponses[0].TalkType == TalkType.Sleep;
        if (!settings.ContinueDialogueWhileSleeping && !Pawn.Awake() && !allowSleeping) return false;

        return !Pawn.Dead && TalkInitiationWeight > 0;
    }

    public bool CanGenerateTalk()
    {
        if (Pawn.IsPlayer()) return true;
        DrainIncomingTalkResponses();
        return !IsGeneratingTalk && CanDisplayTalk() && Pawn.Awake() && TalkResponses.Empty()
               && CommonUtil.HasPassed(LastTalkTick, Settings.Get().ReplyInterval);
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
        DrainIncomingTalkResponses();
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