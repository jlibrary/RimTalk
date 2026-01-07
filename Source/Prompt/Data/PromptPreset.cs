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
    /// Adds an entry to the end of the list.
    /// </summary>
    public void AddEntry(PromptEntry entry)
    {
        Entries.Add(entry);
    }

    /// <summary>
    /// Inserts an entry at a specific index.
    /// </summary>
    /// <param name="entry">The entry to insert</param>
    /// <param name="index">The index to insert at (0 = beginning, -1 or >= Count = end)</param>
    public void InsertEntry(PromptEntry entry, int index)
    {
        if (index < 0 || index >= Entries.Count)
        {
            Entries.Add(entry);
        }
        else
        {
            Entries.Insert(index, entry);
        }
    }

    /// <summary>
    /// Inserts an entry after a specific entry.
    /// </summary>
    /// <param name="entry">The entry to insert</param>
    /// <param name="afterEntryId">The ID of the entry to insert after</param>
    /// <returns>True if successful, false if afterEntryId was not found</returns>
    public bool InsertEntryAfter(PromptEntry entry, string afterEntryId)
    {
        var index = Entries.FindIndex(e => e.Id == afterEntryId);
        if (index < 0)
        {
            Entries.Add(entry); // Fall back to adding at end
            return false;
        }
        Entries.Insert(index + 1, entry);
        return true;
    }

    /// <summary>
    /// Inserts an entry before a specific entry.
    /// </summary>
    /// <param name="entry">The entry to insert</param>
    /// <param name="beforeEntryId">The ID of the entry to insert before</param>
    /// <returns>True if successful, false if beforeEntryId was not found</returns>
    public bool InsertEntryBefore(PromptEntry entry, string beforeEntryId)
    {
        var index = Entries.FindIndex(e => e.Id == beforeEntryId);
        if (index < 0)
        {
            Entries.Add(entry); // Fall back to adding at end
            return false;
        }
        Entries.Insert(index, entry);
        return true;
    }

    /// <summary>
    /// Finds entry index by name (for mods that don't have entry IDs).
    /// </summary>
    /// <param name="entryName">The name of the entry to find</param>
    /// <returns>The entry ID if found, null otherwise</returns>
    public string FindEntryIdByName(string entryName)
    {
        return Entries.FirstOrDefault(e => e.Name == entryName)?.Id;
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