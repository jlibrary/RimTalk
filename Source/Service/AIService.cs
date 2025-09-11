using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;

namespace RimTalk.Service
{
    public static class AIService
    {
        private static string _instruction = "";
        private static bool _busy;
        private static bool _contextUpdating;
        private static bool _firstInstruction = true;
        private static readonly List<(Role role, string message)> Messages = new List<(Role role, string message)>();
        private const int MaxMessages = 6;

        // Multi-turn conversation used for generating AI dialogue
        public static async Task<List<TalkResponse>> Chat(TalkRequest request)
        {
            EnsureMessageLimit();
            Messages.Add((Role.User, request.Prompt));
            int talkLogId = TalkLogHistory.AddRequest(request);

            var payload = await ExecuteAIRequest(_instruction, Messages);

            var talkResponses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(payload.Response);
            
            foreach (var talkResponse in talkResponses)
            {
                TalkLogHistory.AddResponse(talkLogId, talkResponse.Text, payload, talkResponse.Name);
            }

            _firstInstruction = false;
            
            CleanupLastRequest();
            AddResposne(payload.Response);
            
            return talkResponses;
        }

        // One time query - used for generating persona, etc
        public static async Task<T> Query<T>(TalkRequest request) where T : IJsonData
        {
            List<(Role role, string message)> message = new List<(Role role, string message)>
                { (Role.User, request.Prompt) };
            
            int talkLogId = TalkLogHistory.AddRequest(request);

            var payload = await ExecuteAIRequest("", message);

            var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);
            
            TalkLogHistory.AddResponse(talkLogId, jsonData.ToString(), payload);
            
            return jsonData;
        }

        private static async Task<Payload> ExecuteAIRequest(string instruction, List<(Role role, string message)> messages)
        {
            _busy = true;
            try
            {
                var payload = await AIErrorHandler.HandleWithRetry(() =>
                    AIClientFactory.GetAIClient().GetChatCompletionAsync(instruction, messages)
                );

                Stats.IncrementCalls();
                Stats.IncrementTokens(payload.TokenCount);
                payload.Response = JsonUtil.Sanitize(payload.Response);

                return payload;
            }
            finally
            {
                _busy = false;
            }
        }

        public static void UpdateContext(string context)
        {
            Logger.Message($"UpdateContext:\n{context}");
            _instruction = context;
        }

        public static bool IsFirstInstruction()
        {
            return _firstInstruction;
        }

        public static void AddResposne(string text)
        {
            Messages.Add((Role.AI, text));
        }

        public static void CleanupLastRequest()
        {
            if (Messages.Count == 0) return;

            var lastMessage = Messages[Messages.Count - 1];
            string cleanedText = lastMessage.message.Replace(Constant.Prompt, "");

            Messages[Messages.Count - 1] = (lastMessage.role, cleanedText);
        }

        public static bool IsBusy()
        {
            return _busy || _contextUpdating;
        }

        public static bool IsContextUpdating()
        {
            return _contextUpdating;
        }

        private static void EnsureMessageLimit()
        {
            // First, ensure alternating pattern by removing consecutive duplicates
            for (int i = Messages.Count - 1; i > 0; i--)
            {
                if (Messages[i].role == Messages[i - 1].role)
                {
                    // Remove the first occurrence (earlier message)
                    Messages.RemoveAt(i - 1);
                    i--; // Adjust index since we removed an element
                }
            }

            // Then, enforce the maximum message limit
            while (Messages.Count > MaxMessages)
            {
                Messages.RemoveAt(0);
            }
        }

        public static void Clear()
        {
            _busy = false;
            _contextUpdating = false;
            _firstInstruction = true;
            Messages.Clear();
            _instruction = "";
        }
    }
}