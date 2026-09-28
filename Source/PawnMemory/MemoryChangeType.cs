namespace RimTalk.PawnMemory;

/// <summary>
/// Categorizes the lifecycle transitions and modifications of pawn memories and player directives.
/// </summary>
public enum MemoryChangeType
{
    Added,
    Updated,
    DirectiveSet,
    DirectiveRemoved,
    CoreTraumaAdded,
    PurgedDecay,
    Evicted,
    Consolidated,
    Cleared,
    MilestoneRecorded
}
