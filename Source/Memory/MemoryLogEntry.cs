using System;

namespace RimTalk.Memory;

/// <summary>
/// Represents an event log entry tracking a memory or directive modification.
/// </summary>
public class MemoryLogEntry
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.Now;
    public int Tick { get; set; }
    public string SourcePawnName { get; set; } = string.Empty;
    public int SourcePawnId { get; set; } = -1;
    public string TargetPawnName { get; set; } = string.Empty;
    public int TargetPawnId { get; set; } = -1;
    public MemoryChangeType ChangeType { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public float OldWeight { get; set; }
    public float NewWeight { get; set; }
    public bool IsDirective { get; set; }
    public bool IsCoreTrauma { get; set; }
    public string Details { get; set; } = string.Empty;
}
