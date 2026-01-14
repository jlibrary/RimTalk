using RimTalk.Data;

namespace RimTalk.Prompt;

/// <summary>
/// Prompt role for preset entries - separate from <see cref="Role"/> for:
/// 1. Serialization stability (preset JSON uses "Assistant" while legacy uses "AI")
/// 2. UI consistency (settings panel shows "Assistant" like OpenAI/Claude terminology)
/// Use <see cref="PromptManager.ConvertToRole"/> to convert to <see cref="Role"/> for API calls.
/// </summary>
public enum PromptRole
{
    /// <summary>System instruction (system)</summary>
    System,
    /// <summary>User message (user)</summary>
    User,
    /// <summary>AI assistant message (assistant) - maps to Role.AI</summary>
    Assistant
}

/// <summary>
/// Prompt position type.
/// </summary>
public enum PromptPosition
{
    /// <summary>Relative position - concatenated in order by list position</summary>
    Relative,
    /// <summary>In-chat - inserted at specified depth in chat history</summary>
    InChat
}