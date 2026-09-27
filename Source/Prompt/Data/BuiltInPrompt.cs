namespace RimTalk.Prompt;

/// <summary>
/// Canonical identifiers, display names, and template tokens for RimTalk built-in prompt components.
/// Separating identity from user-facing names ensures logic remains intact even if display names are localized or modified.
/// </summary>
public static class BuiltInPromptIds
{
    public const string BaseInstruction = "base-instruction";
    public const string JsonFormat = "json-format";
    public const string Context = "context";
    public const string RecentEvents = "recent-events";
    public const string ChatHistory = "chat-history";
    public const string DialoguePrompt = "dialogue-prompt";
    public const string FormatReminder = "format-reminder";
}

public static class BuiltInPromptNames
{
    public const string BaseInstruction = "Base Instruction";
    public const string JsonFormat = "JSON Format";
    public const string Context = "Context";
    public const string PawnProfiles = "Pawn Profiles"; // Legacy alias for Context
    public const string RecentEvents = "Recent Events";
    public const string ChatHistory = "Chat History";
    public const string DialoguePrompt = "Dialogue Prompt";
    public const string JsonFormatReminder = "JSON Format Reminder";
}

public static class BuiltInPromptTokens
{
    public const string JsonFormat = "{{ json.format }}";
    public const string JsonAnchor = "{{ json.anchor }}";
    public const string Context = "{{context}}";
    public const string ChatHistory = "{{chat.history}}";
    public const string Prompt = "{{prompt}}";
}
