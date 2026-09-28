namespace RimTalk.PawnMemory;

/// <summary>
/// Defines the relational perspective of an episodic memory entry.
/// </summary>
public enum MemoryPerspective
{
    /// <summary>
    /// Personal experience with no specific counterpart (TargetPawnId = -1).
    /// </summary>
    None = 0,

    /// <summary>
    /// The owning pawn was the initiator or actor of the event toward the target pawn.
    /// </summary>
    Actor = 1,

    /// <summary>
    /// The owning pawn was the recipient or victim of the event from the target pawn.
    /// </summary>
    Target = 2
}
