using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTalk.AI.Local;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.AI.OpenAI
{
    public abstract class OpenAICompatibleClient : IAIClient
    {
        protected abstract string BaseUrl { get; }
        private string EndpointUrl => $"{BaseUrl}/v1/chat/completions";
        private string CurrentApiKey => Settings.Get().GetActiveConfig()?.ApiKey;
        private string CurrentModel => Settings.Get().GetCurrentModel();

        public async Task<string> GetChatCompletionAsync(string instruction,
            List<(Role role, string message)> messages)
        {
            var allMessages = new List<Message>();

            if (!string.IsNullOrEmpty(instruction))
            {
                allMessages.Add(new Message
                {
                    Role = "system",
                    Content = instruction
                });
            }

            allMessages.AddRange(messages.Select(m => new Message
            {
                Role = ConvertRole(m.role),
                Content = m.message
            }));

            var request = new OpenAIRequest()
            {
                Model = CurrentModel,
                Messages = allMessages
            };

            return await GetResponseFromApiAsync(request);
        }

        private async Task<string> GetResponseFromApiAsync(OpenAIRequest request)
        {
            if (string.IsNullOrEmpty(CurrentApiKey))
            {
                Logger.Error("API key is missing.");
                return null;
            }

            try
            {
                string jsonContent = JsonUtil.SerializeToJson(request);
                Logger.Message($"API request: {EndpointUrl}\n{jsonContent}");

                using (var webRequest = UnityWebRequest.Post(EndpointUrl, jsonContent))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {CurrentApiKey}");

                    var asyncOperation = webRequest.SendWebRequest();

                    while (!asyncOperation.isDone)
                    {
                        if (Current.Game == null) return null;
                        await Task.Delay(100);
                    }

                    Logger.Message($"API response: \n{webRequest.downloadHandler.text}");

                    if (webRequest.responseCode == 429)
                        throw new QuotaExceededException("Quota exceeded");

                    if (webRequest.isNetworkError || webRequest.isHttpError)
                    {
                        Logger.Error($"Request failed: {webRequest.responseCode} - {webRequest.error}");
                        throw new Exception();
                    }

                    var response = JsonUtil.DeserializeFromJson<OpenAIResponse>(webRequest.downloadHandler.text);
                    return response?.Choices?[0]?.Message?.Content;
                }
            }
            catch (QuotaExceededException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception in API request: {ex.Message}");
                throw;
            }
        }

        private string ConvertRole(Role role)
        {
            switch (role)
            {
                case Role.USER:
                    return "user";
                case Role.AI:
                    return "assistant";
                default:
                    throw new ArgumentException($"Unknown role: {role}");
            }
        }
    }
}