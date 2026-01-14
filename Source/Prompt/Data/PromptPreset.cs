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
    
    /// <summary>
    /// Set of deleted mod entry IDs. Entries with these IDs will not be re-added by mods.
    /// Uses the deterministic ID (e.g., "mod_mymod_myentry") directly.
    /// </summary>
    public HashSet<string> DeletedModEntryIds = new();

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
    /// Checks if a mod entry should be skipped (blacklisted or duplicate).
    /// </summary>
    private bool ShouldSkipModEntry(PromptEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SourceModId)) return false;
        
        // PromptEntry automatically generates deterministic ID when SourceModId is set
        // Check blacklist
        if (DeletedModEntryIds.Contains(entry.Id))
            return true;
        
        // Check duplicates by ID
        if (Entries.Any(e => e.Id == entry.Id))
            return true;
        
        return false;
    }

    /// <summary>
    /// Adds an entry to the end of the list.
    /// For mod entries, returns false if blacklisted or already exists.
    /// </summary>
    public bool AddEntry(PromptEntry entry)
    {
        if (ShouldSkipModEntry(entry)) return false;
        Entries.Add(entry);
        return true;
    }

    /// <summary>
    /// Inserts an entry at a specific index.
    /// </summary>
    public bool InsertEntry(PromptEntry entry, int index)
    {
        if (ShouldSkipModEntry(entry)) return false;
        
        if (index < 0 || index >= Entries.Count)
        {
            Entries.Add(entry);
        }
        else
        {
            Entries.Insert(index, entry);
        }
        return true;
    }

    /// <summary>
    /// Inserts an entry after a specific entry.
    /// </summary>
    public bool InsertEntryAfter(PromptEntry entry, string afterEntryId)
    {
        if (ShouldSkipModEntry(entry)) return false;
        
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
    public bool InsertEntryBefore(PromptEntry entry, string beforeEntryId)
    {
        if (ShouldSkipModEntry(entry)) return false;
        
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
    /// Finds entry ID by name.
    /// </summary>
    public string FindEntryIdByName(string entryName)
    {
        return Entries.FirstOrDefault(e => e.Name == entryName)?.Id;
    }

    /// <summary>
    /// Removes an entry. If it's a mod entry, adds ID to blacklist.
    /// </summary>
    public bool RemoveEntry(string entryId)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) return false;
        
        // Add to blacklist if it's a mod entry
        if (!string.IsNullOrEmpty(entry.SourceModId))
        {
            DeletedModEntryIds.Add(entry.Id);
        }
        Entries.Remove(entry);
        return true;
    }
    
    /// <summary>
    /// Clears the blacklist. Called when resetting to defaults.
    /// </summary>
    public void ClearBlacklist()
    {
        DeletedModEntryIds.Clear();
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
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Id, "id", Guid.NewGuid().ToString());
        Scribe_Values.Look(ref Name, "name", "Default Preset");
        Scribe_Values.Look(ref Description, "description", "");
        Scribe_Collections.Look(ref Entries, "entries", LookMode.Deep);
        Scribe_Values.Look(ref IsActive, "isActive", false);
        
        // Serialize blacklist as List<string> for compatibility
        List<string> deletedList = DeletedModEntryIds?.ToList() ?? new List<string>();
        Scribe_Collections.Look(ref deletedList, "deletedModEntryIds", LookMode.Value);
        DeletedModEntryIds = deletedList?.ToHashSet() ?? new HashSet<string>();
        
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
            Name = Name,
            Description = Description,
            IsActive = false,
            Entries = []
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