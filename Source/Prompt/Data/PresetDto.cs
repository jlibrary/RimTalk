using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimTalk.Prompt;

/// <summary>
/// Data Transfer Object for preset JSON serialization.
/// Uses DataContract for compatibility with JsonUtil (DataContractJsonSerializer).
/// </summary>
[DataContract]
public class PresetDto
{
    [DataMember(Name = "version")]
    public int Version { get; set; } = 1;
    
    [DataMember(Name = "name")]
    public string Name { get; set; }
    
    [DataMember(Name = "description")]
    public string Description { get; set; }
    
    [DataMember(Name = "entries")]
    public List<EntryDto> Entries { get; set; } = new();
    
    /// <summary>
    /// Converts from PromptPreset to DTO for serialization.
    /// </summary>
    public static PresetDto FromPreset(PromptPreset preset)
    {
        if (preset == null) return null;
        
        var dto = new PresetDto
        {
            Version = 1,
            Name = preset.Name,
            Description = preset.Description,
            Entries = new List<EntryDto>()
        };
        
        foreach (var entry in preset.Entries)
        {
            dto.Entries.Add(EntryDto.FromEntry(entry));
        }
        
        return dto;
    }
    
    /// <summary>
    /// Converts from DTO to PromptPreset after deserialization.
    /// </summary>
    public PromptPreset ToPreset()
    {
        var preset = new PromptPreset
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name ?? "Imported Preset",
            Description = Description ?? "",
            IsActive = false,
            Entries = new List<PromptEntry>()
        };
        
        if (Entries != null)
        {
            foreach (var entryDto in Entries)
            {
                var entry = entryDto.ToEntry();
                if (entry != null)
                {
                    if (!entry.IsMainChatHistory && entry.Content?.Trim() == "{{chat.history}}")
                    {
                        entry.IsMainChatHistory = true;
                    }
                    preset.Entries.Add(entry);
                }
            }
        }
        
        return preset;
    }
}

/// <summary>
/// Data Transfer Object for prompt entry JSON serialization.
/// </summary>
[DataContract]
public class EntryDto
{
    [DataMember(Name = "name")]
    public string Name { get; set; }
    
    [DataMember(Name = "content")]
    public string Content { get; set; }
    
    [DataMember(Name = "role")]
    public string Role { get; set; }
    
    [DataMember(Name = "position")]
    public string Position { get; set; }
    
    [DataMember(Name = "inChatDepth")]
    public int InChatDepth { get; set; }
    
    [DataMember(Name = "enabled")]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "isMainChatHistory")]
    public bool IsMainChatHistory { get; set; }
    
    /// <summary>
    /// Converts from PromptEntry to DTO for serialization.
    /// </summary>
    public static EntryDto FromEntry(PromptEntry entry)
    {
        if (entry == null) return null;
        
        return new EntryDto
        {
            Name = entry.Name,
            Content = entry.Content,
            Role = entry.Role.ToString(),
            Position = entry.Position.ToString(),
            InChatDepth = entry.InChatDepth,
            Enabled = entry.Enabled,
            IsMainChatHistory = entry.IsMainChatHistory
        };
    }
    
    /// <summary>
    /// Converts from DTO to PromptEntry after deserialization.
    /// </summary>
    public PromptEntry ToEntry()
    {
        var entry = new PromptEntry
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name ?? "New Prompt",
            Content = Content ?? "",
            InChatDepth = InChatDepth,
            Enabled = Enabled,
            IsMainChatHistory = IsMainChatHistory,
            SourceModId = null
        };
        
        // Parse Role enum
        if (!string.IsNullOrEmpty(Role) && Enum.TryParse<PromptRole>(Role, true, out var role))
        {
            entry.Role = role;
        }
        
        // Parse Position enum
        if (!string.IsNullOrEmpty(Position) && Enum.TryParse<PromptPosition>(Position, true, out var pos))
        {
            entry.Position = pos;
        }
        
        return entry;
    }
}