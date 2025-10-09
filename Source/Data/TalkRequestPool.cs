using System.Collections.Generic;
using System.Linq;
using RimTalk.Source.Data;
using Verse;

namespace RimTalk.Data;

public static class TalkRequestPool
{
    private static readonly List<TalkRequest> Requests = [];

    public static void Add(string prompt, Pawn initiator = null, Pawn recipient = null, int mapId = 0)
    {
        var request = new TalkRequest(prompt, initiator, recipient, TalkType.Event)
        {
            MapId = mapId,
        };

        Requests.Add(request);
    }

    public static void Add(string prompt, Pawn initiator, Pawn recipient, int mapId, TalkType talkType)
    {
        var request = new TalkRequest(prompt, initiator, recipient, talkType)
        {
            MapId = mapId,
        };
        Requests.Add(request);
    }

    public static TalkRequest GetRequestFromPool(Pawn pawn)
    {
        var requests = Requests
            .Where(r => r.MapId == pawn.Map.uniqueID)
            .OrderBy(r => r.CreatedTick)
            .ToList();

        foreach (var request in requests)
        {
            if (request.IsExpired())
            {
                Requests.Remove(request);
            }
            else
            {
                request.Initiator = pawn;
                return request;
            }
        }

        return null;
    }

    // Get the first request without removing it
    public static TalkRequest Peek()
    {
        return Requests.FirstOrDefault();
    }

    // Remove a specific request
    public static bool Remove(TalkRequest request)
    {            return Requests.Remove(request);
    }

    public static IEnumerable<TalkRequest> GetAll()
    {
        return Requests.ToList();
    }

    public static void Clear()
    {
        Requests.Clear();
    }

    public static int Count => Requests.Count;
    public static bool IsEmpty => Requests.Count == 0;
}