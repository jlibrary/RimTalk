using System;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Prompt entry - corresponds to a single entry in SillyTavern's preset panel.
/// </summary>
public class PromptEntry : IExposable
{
    /// <summary>Unique identifier</summary>
    public string Id = Guid.NewGuid().ToString();
    
    /// <summary>Display name (e.g., "Base Instruction", "Difficulty Settings")</summary>
    public string Name = "New Prompt";
    
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
    
    /// <summary>Source mod's package ID (null means RimTalk built-in or user created)</summary>
    public string SourceModId;

    public PromptEntry()
    {
    }

    public PromptEntry(string name, string content, PromptRole role = PromptRole.System, int inChatDepth = 0)
    {
        Name = name;
        Content = content;
        Role = role;
        InChatDepth = inChatDepth;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id", Guid.NewGuid().ToString());
        Scribe_Values.Look(ref Name, "name", "New Prompt");
        Scribe_Values.Look(ref Content, "content", "");
        Scribe_Values.Look(ref Role, "role", PromptRole.System);
        Scribe_Values.Look(ref Position, "position", PromptPosition.Relative);
        Scribe_Values.Look(ref InChatDepth, "inChatDepth", 0);
        Scribe_Values.Look(ref Enabled, "enabled", true);
        Scribe_Values.Look(ref SourceModId, "sourceModId");
        
        // Ensure Id is not empty
        if (string.IsNullOrEmpty(Id))
            Id = Guid.NewGuid().ToString();
    }

    public PromptEntry Clone()
    {
        return new PromptEntry
        {
            Id = Guid.NewGuid().ToString(), // New ID for cloned entry
            Name = Name + " (Copy)",
            Content = Content,
            Role = Role,
            Position = Position,
            InChatDepth = InChatDepth,
            Enabled = Enabled,
            SourceModId = null
        };
    }

    public override string ToString()
    {
        return $"[{Role}] {Name} (Enabled: {Enabled})";
    }
}