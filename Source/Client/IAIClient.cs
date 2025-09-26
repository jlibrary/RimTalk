using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.Client;
using RimTalk.Data;

namespace RimTalk.Client;
public interface IAIClient
{
    /// <summary>
    /// Gets a chat completion from the AI model
    /// </summary>
    /// <param name="instruction">System instruction or prompt</param>
    /// <param name="messages">List of conversation messages with roles</param>
    /// <returns>AI response text and token usage</returns>
    Task<Payload> GetChatCompletionAsync(string instruction, List<(Role role, string message)> messages);
}