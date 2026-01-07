using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimTalk.Prompt;

/// <summary>
/// Prompt preset - a collection of prompt entries.
/// </summary>
public class PromptPreset : IExposable
{
    /// <summary>Unique identifier</summary>
    public string Id = Guid.NewGuid().ToString();
    
    /// <summary>Preset name</summary>
    public string Name = "Default Preset";
    
    /// <summary>Preset description</summary>
    public string Description = "";
    
    /// <summary>List of prompt entries</summary>
    public List<PromptEntry> Entries = new();
    
    /// <summary>Whether this is the currently active preset</summary>
    public bool IsActive;

    public PromptPreset()
    {
    }

    public PromptPreset(string name, string description = "")
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Gets all enabled Relative position entries (in list order).
    /// </summary>
    public IEnumerable<PromptEntry> GetRelativeEntries()
    {
        return Entries
            .Where(e => e.Enabled && e.Position == PromptPosition.Relative);
    }

    /// <summary>
    /// Gets all enabled InChat position entries (ordered by InChatDepth descending).
    /// </summary>
    public IEnumerable<PromptEntry> GetInChatEntries()
    {
        return Entries
            .Where(e => e.Enabled && e.Position == PromptPosition.InChat)
            .OrderByDescending(e => e.InChatDepth);
    }

    /// <summary>
    /// Adds an entry.
    /// </summary>
    public void AddEntry(PromptEntry entry)
    {
        Entries.Add(entry);
    }

    /// <summary>
    /// Removes an entry.
    /// </summary>
    public bool RemoveEntry(string entryId)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry != null)
        {
            Entries.Remove(entry);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets an entry by ID.
    /// </summary>
    public PromptEntry GetEntry(string entryId)
    {
        return Entries.FirstOrDefault(e => e.Id == entryId);
    }

    /// <summary>
    /// Moves entry order (direction=-1 moves up, direction=1 moves down).
    /// </summary>
    public void MoveEntry(string entryId, int direction)
    {
        var index = Entries.FindIndex(e => e.Id == entryId);
        if (index < 0) return;

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= Entries.Count) return;

        var entry = Entries[index];
        Entries.RemoveAt(index);
        Entries.Insert(newIndex, entry);
        // Order is determined by list position, no recalculation needed
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id", Guid.NewGuid().ToString());
        Scribe_Values.Look(ref Name, "name", "Default Preset");
        Scribe_Values.Look(ref Description, "description", "");
        Scribe_Collections.Look(ref Entries, "entries", LookMode.Deep);
        Scribe_Values.Look(ref IsActive, "isActive", false);
        
        // Ensure collection is not null
        Entries ??= new List<PromptEntry>();
        
        // Ensure Id is not empty
        if (string.IsNullOrEmpty(Id))
            Id = Guid.NewGuid().ToString();
    }

    public PromptPreset Clone()
    {
        var clone = new PromptPreset
        {
            Id = Guid.NewGuid().ToString(),
            Name = Name + " (Copy)",
            Description = Description,
            IsActive = false,
            Entries = new List<PromptEntry>()
        };

        foreach (var entry in Entries)
        {
            clone.Entries.Add(entry.Clone());
        }

        return clone;
    }

    public override string ToString()
    {
        return $"{Name} ({Entries.Count} entries, Active: {IsActive})";
    }
}