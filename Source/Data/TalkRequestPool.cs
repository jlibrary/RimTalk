using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Data
{
    public class TalkRequest
    {
        public string Prompt { get; set; }
        public Pawn Initiator { get; set; }
        public Pawn Recipient { get; set; }
        public DateTime CreatedAt { get; } = DateTime.UtcNow; // for sorting

        public TalkRequest(string prompt, Pawn initiator = null, Pawn recipient = null)
        {
            Prompt = prompt.RemoveLineBreaks();
            Initiator = initiator;
            Recipient = recipient;
        }
    }

    public static class TalkRequestPool
    {
        private static readonly List<TalkRequest> Requests = new List<TalkRequest>();

        public static void Add(string prompt, Pawn initiator = null, Pawn recipient = null)
        {
            var request = new TalkRequest(prompt, initiator, recipient);
            Requests.Add(request);

            // Keep sorted by CreatedAt
            Requests.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
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