using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Client;

namespace RimTalk.Data
{
    public class TalkLog
    {
        public int Id { get; }
        public string Name { get; set; }
        public string Prompt { get; set; }
        public string Response { get; set; }
        public string RequestPayload { get; set; }
        public string ResponsePayload { get; set; }
        public int TokenCount { get; set; }
        public DateTime Timestamp { get; }
        public int ElapsedMs;
        
        public TalkLog(int id, string name, string prompt, string response, Payload payload, DateTime timestamp)
        {
            Id = id;
            Name = name;
            Prompt = prompt;
            Response = response;
            RequestPayload = payload?.Request;
            ResponsePayload = payload?.Response;
            TokenCount = payload?.TokenCount ?? 0;
            Timestamp = timestamp;
        }
    }

    public static class TalkLogHistory
    {
        private static readonly SortedDictionary<int, TalkLog> _history = new SortedDictionary<int, TalkLog>();
        private static int _nextId = 1;

        public static int AddRequest(TalkRequest request)
        {
            var log = new TalkLog(_nextId++, request.Initiator.Name.ToStringShort, request.Prompt, null, null, DateTime.Now);
            _history[log.Id] = log;
            return log.Id;
        }

        public static void AddResponse(int id, string response, Payload payload, string name = null)
        {
            if (_history.TryGetValue(id, out var log))
            {
                // first message
                if (log.Response == null)
                {
                    log.Response = response;
                    log.RequestPayload = payload?.Request;
                    log.ResponsePayload = payload?.Response;
                    log.TokenCount = payload?.TokenCount ?? 0;
                    log.ElapsedMs = (int) (DateTime.Now - log.Timestamp).TotalMilliseconds;
                }
                // rest of multi-turn messages
                else
                {
                    log = new TalkLog(_nextId++, name, log.Prompt, response, payload, log.Timestamp);
                    _history[log.Id] = log;
                    log.TokenCount = 0;
                    log.ElapsedMs = 0;
                }
            }
        }

        public static IEnumerable<TalkLog> GetAll()
        {
            // LIFO: newest first
            foreach (var log in _history.Reverse())
            {
                yield return log.Value;
            }
        }

        public static void Clear()
        {
            _history.Clear();
            _nextId = 1;
        }
    }
}
