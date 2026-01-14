using System;
using System.Text.RegularExpressions;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Prompt entry - corresponds to a single entry in SillyTavern's preset panel.
/// </summary>
public class PromptEntry : IExposable
{
    private string _id = Guid.NewGuid().ToString();
    private string _sourceModId;
    private string _name = "New Prompt";
    
    /// <summary>
    /// Unique identifier. For mod entries, this is deterministically generated from SourceModId and Name.
    /// </summary>
    public string Id
    {
        get => _id;
        set => _id = value;
    }
    
    /// <summary>Display name (e.g., "Base Instruction", "Difficulty Settings")</summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            UpdateIdIfModEntry();
        }
    }
    
    /// <summary>Prompt content (supports mustache syntax)</summary>
    public string Content = "";
    
    /// <summary>Message role</summary>
    public PromptRole Role = PromptRole.System;
    
    /// <summary>Position type</summary>
    public PromptPosition Position = PromptPosition.Relative;
    
    /// <summary>
    /// InChat depth (only valid for InChat position).
    /// Determines the insertion depth in chat history (0 = after latest message).
    /// For Relative position, entry order is determined by list position.
    /// </summary>
    public int InChatDepth = 0;
    
    /// <summary>Whether enabled</summary>
    public bool Enabled = true;
    
    /// <summary>
    /// Source mod's package ID (null means RimTalk built-in or user created).
    /// Setting this will automatically generate a deterministic ID based on SourceModId and Name.
    /// </summary>
    public string SourceModId
    {
        get => _sourceModId;
        set
        {
            _sourceModId = value;
            UpdateIdIfModEntry();
        }
    }

    /// <summary>
    /// Whether this entry is the designated main chat history marker.
    /// If true, this entry is used to insert the chat history and is locked in the UI.
    /// </summary>
    public bool IsMainChatHistory = false;
    
    /// <summary>
    /// Updates the ID to be deterministic if this is a mod entry.
    /// </summary>
    private void UpdateIdIfModEntry()
    {
        if (!string.IsNullOrEmpty(_sourceModId) && !string.IsNullOrEmpty(_name))
        {
            _id = GenerateDeterministicId(_sourceModId, _name);
        }
    }
    
    /// <summary>
    /// Generates a deterministic ID from modId and name.
    /// </summary>
    public static string GenerateDeterministicId(string modId, string name)
    {
        var sanitizedModId = SanitizeForId(modId);
        var sanitizedName = SanitizeForId(name);
        return $"mod_{sanitizedModId}_{sanitizedName}";
    }
    
    private static string SanitizeForId(string input)
    {
        if (string.IsNullOrEmpty(input)) return "unknown";
        return Regex.Replace(input.ToLowerInvariant(), @"[^a-z0-9]", "");
    }

    public PromptEntry()
    {
    }

    public PromptEntry(string name, string content, PromptRole role = PromptRole.System, int inChatDepth = 0)
    {
        _name = name;
        Content = content;
        Role = role;
        InChatDepth = inChatDepth;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref _id, "id", Guid.NewGuid().ToString());
        Scribe_Values.Look(ref _name, "name", "New Prompt");
        Scribe_Values.Look(ref Content, "content", "");
        Scribe_Values.Look(ref Role, "role", PromptRole.System);
        Scribe_Values.Look(ref Position, "position", PromptPosition.Relative);
        Scribe_Values.Look(ref InChatDepth, "inChatDepth", 0);
        Scribe_Values.Look(ref Enabled, "enabled", true);
        Scribe_Values.Look(ref _sourceModId, "sourceModId");
        Scribe_Values.Look(ref IsMainChatHistory, "isMainChatHistory", false);
        
        // Ensure Id is not empty
        if (string.IsNullOrEmpty(_id))
            _id = Guid.NewGuid().ToString();
    }

    public PromptEntry Clone()
    {
        return new PromptEntry
        {
            Id = Guid.NewGuid().ToString(), // New ID for cloned entry
            Name = Name,
            Content = Content,
            Role = Role,
            Position = Position,
            InChatDepth = InChatDepth,
            Enabled = Enabled,
            IsMainChatHistory = IsMainChatHistory,
            SourceModId = null
        };
    }

    public override string ToString()
    {
        return $"[{Role}] {Name} (Enabled: {Enabled})";
    }
}