using System.Collections.Generic;
using System.Linq;

namespace RimTalk.Client;

public class ChatMessage
{
    public string Role { get; set; }
    public string Text { get; set; }
    public string ImageBase64 { get; set; }

    public ChatMessage(string role, string text, string imageBase64 = null)
    {
        Role = role;
        Text = text ?? "";
        ImageBase64 = imageBase64;
    }

    public Dictionary<string, object> ToPayload()
    {
        if (string.IsNullOrEmpty(ImageBase64))
        {
            return new Dictionary<string, object>
            {
                ["role"] = Role,
                ["content"] = Text
            };
        }

        return new Dictionary<string, object>
        {
            ["role"] = Role,
            ["content"] = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "text", ["text"] = Text },
                new Dictionary<string, object>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, object>
                    {
                        ["url"] = $"data:image/jpeg;base64,{ImageBase64}",
                        ["detail"] = "auto"
                    }
                }
            }
        };
    }
}

public class ChatRequest
{
    public string Model { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];
    public bool Stream { get; set; }
    public string ReasoningEffort { get; set; }

    public Dictionary<string, object> ToPayload()
    {
        var dict = new Dictionary<string, object>
        {
            ["messages"] = Messages.Select(m => (object)m.ToPayload()).ToList(),
            ["stream"] = Stream
        };

        if (!string.IsNullOrEmpty(Model))
            dict["model"] = Model;

        if (Stream)
            dict["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true };

        if (!string.IsNullOrEmpty(ReasoningEffort))
            dict["reasoning_effort"] = ReasoningEffort;

        return dict;
    }
}
