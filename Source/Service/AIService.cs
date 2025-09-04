using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Data;
using RimTalk.Util;

namespace RimTalk.Service
{
    public static class AIService
    {
        private static string instruction = "";
        private static string instruction_updated = "!instruction updated!";
        private static bool busy;
        private static bool contextUpdating;
        private static bool firstInstruction = true;
        private static List<(Role role, string message)> messages = new List<(Role role, string message)>();
        private static readonly int maxMessages = 6;
        
        // Multi-turn conversation used for generating AI dialogue
        public static async Task<string> Chat(string message)
        {
            busy = true;
            EnsureMessageLimit();
            messages.Add((Role.USER, message));
            string response = "";
            try
            {
                response = await AIClientFactory.GetAIClient().GetChatCompletionAsync(instruction, messages);
            }
            finally
            {
                busy = false;
                firstInstruction = false;
            }
            
            return response;
        }
        
        // One time query - used for generating persona, etc
        public static async Task<string> Query(string query)
        {
            List<(Role role, string message)> message = new List<(Role role, string message)>();
            message.Add((Role.USER, query));
            
            busy = true;
            string response = "";
            try
            {
                response = await AIClientFactory.GetAIClient().GetChatCompletionAsync("", message);
            }
            finally
            {
                busy = false;
            }
            return response;
        }

        public static void UpdateContext(string context)
        {
            Logger.Message($"UpdateContext: {context}");
            instruction = context;
        }

        public static bool IsFirstInstruction()
        {
            return firstInstruction;
        }

        public static void AddResposne(string text)
        {
            messages.Add((Role.AI, text));
        }

        public static bool IsBusy()
        {
            return busy || contextUpdating;
        }

        public static bool IsContextUpdating()
        {
            return contextUpdating;
        }
        
        private static void EnsureMessageLimit()
        {
            // First, ensure alternating pattern by removing consecutive duplicates
            for (int i = messages.Count - 1; i > 0; i--)
            {
                if (messages[i].role == messages[i - 1].role)
                {
                    // Remove the first occurrence (earlier message)
                    messages.RemoveAt(i - 1);
                    i--; // Adjust index since we removed an element
                }
            }
            
            // Then, enforce the maximum message limit
            while (messages.Count > maxMessages)
            {
                messages.RemoveAt(0);
            }
        }

        public static void Clear()
        {
            busy = false;
            contextUpdating = false;
            firstInstruction = true;
            messages.Clear();
            instruction = "";
        }
    }
}