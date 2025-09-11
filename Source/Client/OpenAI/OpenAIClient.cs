using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Error;
using RimTalk.Util;
using UnityEngine.Networking;
using Verse;

namespace RimTalk.Client.OpenAI
{
    public class OpenAIClient : IAIClient
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenAIClient(string baseUrl, string model, string apiKey = null)
        {
            _baseUrl = baseUrl;
            _model = model;
            _apiKey = apiKey;
        }

        private string EndpointUrl => $"{_baseUrl}/v1/chat/completions";

        public async Task<Payload> GetChatCompletionAsync(string instruction,
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
                Model = _model,
                Messages = allMessages
            };

            string jsonContent = JsonUtil.SerializeToJson(request);
            var response = await GetCompletionAsync(jsonContent);
            var content = response?.Choices?[0]?.Message?.Content;
            var tokens = response?.Usage?.TotalTokens ?? 0;
            return new Payload(jsonContent, content, tokens);
        }

        private async Task<OpenAIResponse> GetCompletionAsync(string jsonContent)
        {
            if (string.IsNullOrEmpty(_baseUrl))
            {
                Logger.Error("Endpoint URL is missing.");
                return null;
            }

            try
            {
                Logger.Message($"API request: {EndpointUrl}\n{jsonContent}");

                using (var webRequest = UnityWebRequest.Post(EndpointUrl, jsonContent))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    if (!string.IsNullOrEmpty(_apiKey))
                    {
                        webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    }

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

                    return JsonUtil.DeserializeFromJson<OpenAIResponse>(webRequest.downloadHandler.text);
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
                case Role.User:
                    return "user";
                case Role.AI:
                    return "assistant";
                default:
                    throw new ArgumentException($"Unknown role: {role}");
            }
        }
    }
}