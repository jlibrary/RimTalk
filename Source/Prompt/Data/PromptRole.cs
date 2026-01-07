namespace RimTalk.Prompt;

/// <summary>
/// Prompt role - corresponds to OpenAI/Gemini API message roles.
/// </summary>
public enum PromptRole
{
    /// <summary>System instruction (system)</summary>
    System,
    /// <summary>User message (user)</summary>
    User,
    /// <summary>AI assistant message (assistant)</summary>
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