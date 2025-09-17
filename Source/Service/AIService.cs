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

        // Multi-turn conversation used for generating AI dialogue
        public static async Task<(List<TalkResponse> Responses, string RawResponse)> Chat(TalkRequest request,
            List<(Role role, string message)> messages)
        {
            var currentMessages = new List<(Role role, string message)>(messages);

            currentMessages.Add((Role.User, request.Prompt));
            int talkLogId = ApiHistory.AddRequest(request);

            var payload = await ExecuteAIRequest(_instruction, currentMessages);

            if (payload == null)
            {
                ApiHistory.RemoveRequest(talkLogId);
                return (null, null);
            }

            var talkResponses = JsonUtil.DeserializeFromJson<List<TalkResponse>>(payload.Response);

            if (talkResponses != null)
            {
                foreach (var talkResponse in talkResponses)
                {
                    talkResponse.ResponsePayload = payload.Response;
                    ApiHistory.AddResponse(talkLogId, talkResponse.Text, payload, talkResponse.Name);
                }
            }

            _firstInstruction = false;

            return (talkResponses, payload.Response);
        }

        // One time query - used for generating persona, etc
        public static async Task<T> Query<T>(TalkRequest request) where T : class, IJsonData
        {
            List<(Role role, string message)> message = new List<(Role role, string message)>
                { (Role.User, request.Prompt) };

            int talkLogId = ApiHistory.AddRequest(request);

            var payload = await ExecuteAIRequest("", message);

            if (payload == null)
            {
                ApiHistory.RemoveRequest(talkLogId);
                return null;
            }

            var jsonData = JsonUtil.DeserializeFromJson<T>(payload.Response);

            ApiHistory.AddResponse(talkLogId, jsonData.ToString(), payload);

            return jsonData;
        }

        private static async Task<Payload> ExecuteAIRequest(string instruction,
            List<(Role role, string message)> messages)
        {
            _busy = true;
            try
            {
                var payload = await AIErrorHandler.HandleWithRetry(() =>
                    AIClientFactory.GetAIClient().GetChatCompletionAsync(instruction, messages)
                );

                if (payload == null)
                    return null;

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
            Logger.Debug($"UpdateContext:\n{context}");
            _instruction = context;
        }

        public static bool IsFirstInstruction()
        {
            return _firstInstruction;
        }

        public static bool IsBusy()
        {
            return _busy || _contextUpdating;
        }

        public static bool IsContextUpdating()
        {
            return _contextUpdating;
        }

        public static void Clear()
        {
            _busy = false;
            _contextUpdating = false;
            _firstInstruction = true;
            _instruction = "";
        }
    }
}