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

namespace RimTalk.AI.Local
{
    public class LocalClient : IAIClient
    {
        private static string BaseUrl => Settings.Get().GetActiveConfig()?.BaseUrl;
        private static string EndpointUrl => BaseUrl + "/v1/chat/completions";

        public async Task<string> GetChatCompletionAsync(string instruction,
            List<(Role role, string message)> messages)
        {
            List<Message> allMessages = new List<Message>();

            // Add system instruction as first message
            if (!string.IsNullOrEmpty(instruction))
            {
                allMessages.Add(new Message
                {
                    Role = "system",
                    Content = instruction
                });
            }

            // Add the rest of the messages, converting Role enum to string
            allMessages.AddRange(messages.Select(m => new Message
            {
                Role = ConvertRole(m.role),
                Content = m.message
            }));
            
            var request = new OpenAIRequest()
            {
                Model = Settings.Get().GetActiveConfig()?.CustomModelName,
                Messages = allMessages
            };

            return await GetResponseFromApiAsync(request);
        }

        public async Task<string> GetResponseFromApiAsync(OpenAIRequest request)
        {
            if (string.IsNullOrEmpty(BaseUrl))
            {
                Logger.Error("Endpoint URL is missing.");
                return null;
            }

            try
            {
                string jsonContent = JsonUtil.SerializeToJson(request);
                Logger.Message($"API request: {EndpointUrl}\n{jsonContent}");

                using (UnityWebRequest webRequest = UnityWebRequest.Post(EndpointUrl, jsonContent))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

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
                        return null;
                    }

                    OpenAIResponse response = JsonUtil.DeserializeFromJson<OpenAIResponse>(webRequest.downloadHandler.text);
                    
                    // Extract response text from OpenAI format
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
                return null;
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