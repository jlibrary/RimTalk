using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Data
{
    public static class TalkRequestPool
    {
        private static readonly List<TalkRequest> Requests = new List<TalkRequest>();

        public static void Add(string prompt, Pawn initiator = null, Pawn recipient = null)
        {
            var request = new TalkRequest(prompt, initiator, recipient);
            Requests.Add(request);
        }

        // Get the first request without removing it
        public static TalkRequest Peek()
        {
            return Requests.FirstOrDefault();
        }

        // Remove a specific request
        public static bool Remove(TalkRequest request)
        {
            return Requests.Remove(request);
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
}