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

namespace RimTalk.AI.Gemini
{
    public class GeminiClient : IAIClient
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
        private readonly Random _random = new Random();
        private string CurrentApiKey => Settings.Get().GetActiveConfig()?.ApiKey;
        private string CurrentModel => Settings.Get().GetCurrentModel();
        private string EndpointUrl => $"{BaseUrl}/models/{CurrentModel}:generateContent?key={CurrentApiKey}";

        private async Task<string> GetCompletionAsync(GeminiRequest request)
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
                    if (webRequest.responseCode == 503)
                        throw new QuotaExceededException("Model overloaded");

                    if (webRequest.isNetworkError || webRequest.isHttpError)
                    {
                        Logger.Error($"Request failed: {webRequest.responseCode} - {webRequest.error}");
                        throw new Exception();
                    }

                    GeminiResponse response = JsonUtil.DeserializeFromJson<GeminiResponse>(webRequest.downloadHandler.text);
                    
                    if (response.Candidates?[0]?.FinishReason == "MAX_TOKENS")
                        throw new QuotaExceededException("Quota exceeded");
                    
                    return response?.Candidates?[0]?.Content?.Parts?[0]?.Text;
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

        // Helper method for chat-style completion
        public async Task<string> GetChatCompletionAsync(string instruction,
            List<(Role role, string message)> messages)
        {
            // Handle system instruction based on model type
            SystemInstruction systemInstruction = null;
            List<(Role role, string message)> allMessages = new List<(Role role, string message)>();

            if (CurrentModel.Contains("gemma"))
            {
                // For Gemma models, add instruction as the first user message with random prefix
                allMessages.Add((Role.USER, _random.Next() + " " + instruction));
            }
            else
            {
                // For other models, use system_instruction field
                systemInstruction = new SystemInstruction
                {
                    Parts = new List<Part> { new Part { Text = instruction } }
                };
            }

            // Add the rest of the messages
            allMessages.AddRange(messages);

            var generationConfig = new GenerationConfig();

            // Handle thinkingBudget for flash models
            if (CurrentModel.Contains("flash"))
            {
                generationConfig.ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 };
            }

            var request = new GeminiRequest()
            {
                SystemInstruction = systemInstruction,
                Contents = allMessages.Select(m => new Content
                {
                    Role = ConvertRole(m.role),
                    Parts = new List<Part> { new Part { Text = m.message } }
                }).ToList(),
                GenerationConfig = generationConfig
            };

            return await GetCompletionAsync(request);
        }
        
        private string ConvertRole(Role role)
        {
            switch (role)
            {
                case Role.USER:
                    return "user";
                case Role.AI:
                    return "model"; 
                default:
                    throw new ArgumentException($"Unknown role: {role}");
            }
        }
    }
}