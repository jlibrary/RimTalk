using System.Collections.Generic;
using System.Linq;
using RimTalk.Source.Data;
using Verse;

namespace RimTalk.Data;

public static class TalkRequestPool
{
    private static readonly List<TalkRequest> Requests = [];
    private static readonly List<TalkRequest> History = [];
    private const int MaxHistorySize = 500;

    public static void Add(string prompt, Pawn initiator = null, Pawn recipient = null, int mapId = 0)
    {
        var request = new TalkRequest(prompt, initiator, recipient, TalkType.Event)
        {
            MapId = mapId,
            Status = RequestStatus.Pending
        };
        Requests.Add(request);
    }

    public static void Add(string prompt, Pawn initiator, Pawn recipient, int mapId, TalkType talkType)
    {
        var request = new TalkRequest(prompt, initiator, recipient, talkType)
        {
            MapId = mapId,
            Status = RequestStatus.Pending
        };
        Requests.Add(request);
    }

    public static TalkRequest GetRequestFromPool(Pawn pawn)
    {
        for (int i = Requests.Count - 1; i >= 0; i--)
        {
            var request = Requests[i];
            if (request.MapId != pawn.Map.uniqueID) continue;

            if (request.IsExpired())
            {
                AddToHistory(request, RequestStatus.Expired);
                Requests.RemoveAt(i);
                continue;
            }
            
            request.Initiator = pawn;
            return request;
        }
        return null;
    }

    // Called by PawnState to dump finished requests here
    public static void AddToHistory(TalkRequest request, RequestStatus status)
    {
        request.Status = status;
        
        if (request.FinishedTick == -1) 
            request.FinishedTick = GenTicks.TicksGame;

        if (!History.Contains(request))
        {
            History.Add(request);
            if (History.Count > MaxHistorySize) History.RemoveAt(0);
        }
    }

    public static bool Remove(TalkRequest request)
    {
        if (Requests.Contains(request))
        {
            AddToHistory(request, RequestStatus.Processed);
            Requests.Remove(request);
            return true;
        }
        return false;
    }

    public static IEnumerable<TalkRequest> GetAllActive() => Requests.ToList();
    public static IEnumerable<TalkRequest> GetHistory() => History.ToList();
    
    public static void Clear()
    {
        Requests.Clear();
        History.Clear();
    }
    
    public static void ClearHistory()
    {
        History.Clear();
    }

    public static int Count => Requests.Count;
    public static bool IsEmpty => Requests.Count == 0;
}