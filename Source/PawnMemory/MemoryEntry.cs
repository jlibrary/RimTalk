using UnityEngine;
using Verse;

namespace RimTalk.PawnMemory;

/// <summary>
/// Represents a single persistent episodic memory or impression held by a pawn.
/// Implements IExposable for seamless integration with RimWorld's save system.
/// </summary>
public class MemoryEntry : IExposable
{
    public int TargetPawnId = -1;
    public string TargetPawnName = string.Empty;
    public string EventKey = string.Empty;
    public float BaseWeight;
    public int CreatedTick;
    public int LastRecalledTick = -1;
    public string Note = string.Empty;
    public bool IsDirective;
    public bool IsCoreTrauma;
    public bool IsMilestone;
    public MemoryPerspective Perspective = MemoryPerspective.None;
    public int Count = 1;
    public int LastTick = -1;

    public MemoryEntry()
    {
    }

    public MemoryEntry(int targetPawnId, string targetPawnName, string eventKey, float baseWeight, int createdTick, string note, bool isDirective = false, bool isCoreTrauma = false)
        : this(targetPawnId, targetPawnName, eventKey, baseWeight, createdTick, note, isDirective, isCoreTrauma, false)
    {
    }

    public MemoryEntry(int targetPawnId, string targetPawnName, string eventKey, float baseWeight, int createdTick, string note, bool isDirective, bool isCoreTrauma, bool isMilestone)
        : this(targetPawnId, targetPawnName, eventKey, baseWeight, createdTick, note, isDirective, isCoreTrauma, isMilestone, MemoryPerspective.None, 1, createdTick)
    {
    }

    public MemoryEntry(int targetPawnId, string targetPawnName, string eventKey, float baseWeight, int createdTick, string note, bool isDirective, bool isCoreTrauma, bool isMilestone, MemoryPerspective perspective, int count = 1, int lastTick = -1)
    {
        TargetPawnId = targetPawnId;
        TargetPawnName = targetPawnName ?? string.Empty;
        EventKey = eventKey ?? string.Empty;
        BaseWeight = Mathf.Clamp(baseWeight, -100f, 100f);
        CreatedTick = createdTick;
        LastRecalledTick = -1;
        Note = note ?? string.Empty;
        IsDirective = isDirective;
        IsCoreTrauma = isCoreTrauma;
        IsMilestone = isMilestone;
        Perspective = perspective;
        Count = Mathf.Max(1, count);
        LastTick = lastTick > 0 ? lastTick : createdTick;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref TargetPawnId, "targetPawnId", -1);
        Scribe_Values.Look(ref TargetPawnName, "targetPawnName", string.Empty);
        Scribe_Values.Look(ref EventKey, "eventKey", string.Empty);
        Scribe_Values.Look(ref BaseWeight, "baseWeight", 0f);
        Scribe_Values.Look(ref CreatedTick, "createdTick", 0);
        Scribe_Values.Look(ref LastRecalledTick, "lastRecalledTick", -1);
        Scribe_Values.Look(ref Note, "note", string.Empty);
        Scribe_Values.Look(ref IsDirective, "isDirective", false);
        Scribe_Values.Look(ref IsCoreTrauma, "isCoreTrauma", false);
        Scribe_Values.Look(ref IsMilestone, "isMilestone", false);
        Scribe_Values.Look(ref Perspective, "perspective", MemoryPerspective.None);
        Scribe_Values.Look(ref Count, "count", 1);
        Scribe_Values.Look(ref LastTick, "lastTick", -1);

        if (Scribe.mode == LoadSaveMode.PostLoadInit && LastTick <= 0)
        {
            LastTick = CreatedTick;
        }
    }

    /// <summary>
    /// Calculates the calibrated half-life in days based on emotional weight and trauma flags.
    /// Mild episodic events (<= 30f) fade in 4 days.
    /// Moderate events (31-60f) persist across seasons (8 days).
    /// Major momentous events (> 60f) leave lasting impressions across quadrums (14 days).
    /// Core traumas use 7 days.
    /// </summary>
    public float GetDynamicHalfLifeDays()
    {
        if (IsCoreTrauma) return 7f;
        float abs = Mathf.Abs(BaseWeight);
        if (abs > 60f) return 14f;
        if (abs > 30f) return 8f;
        return 4f;
    }

    /// <summary>
    /// Calculates exponential time-decayed emotional weight using weight-calibrated dynamic half-life.
    /// Permanent milestones do not experience passive time decay.
    /// </summary>
    public float GetDecayedWeight(int currentTick)
    {
        return GetDecayedWeight(currentTick, GetDynamicHalfLifeDays());
    }

    /// <summary>
    /// Calculates exponential time-decayed emotional weight using half-life decay.
    /// W(t) = W0 * 0.5^(delta_ticks / half_life_ticks)
    /// Permanent milestones do not experience passive time decay.
    /// </summary>
    public float GetDecayedWeight(int currentTick, float halfLifeDays = 4f)
    {
        if (BaseWeight == 0f) return 0f;
        if (IsMilestone) return BaseWeight;
        int elapsedTicks = Mathf.Max(0, currentTick - CreatedTick);
        float halfLifeTicks = Mathf.Max(60000f, halfLifeDays * 60000f);
        float decayFactor = Mathf.Pow(0.5f, elapsedTicks / halfLifeTicks);
        return BaseWeight * decayFactor;
    }

    /// <summary>
    /// Calculates recall priority score applying recency fatigue penalty with weight-calibrated dynamic half-life.
    /// </summary>
    public float GetRecallScore(int currentTick)
    {
        return GetRecallScore(currentTick, GetDynamicHalfLifeDays());
    }

    /// <summary>
    /// Calculates recall priority score applying recency fatigue penalty
    /// to avoid repetitive LLM echoing of the same memory in consecutive dialogues.
    /// </summary>
    public float GetRecallScore(int currentTick, float halfLifeDays = 4f)
    {
        float decayedWeight = GetDecayedWeight(currentTick, halfLifeDays);
        if (Mathf.Abs(decayedWeight) < 0.1f) return 0f;

        if (LastRecalledTick > 0)
        {
            int elapsedSinceRecall = currentTick - LastRecalledTick;
            if (elapsedSinceRecall < 2500) // Within 1 in-game hour: heavily suppressed
                return decayedWeight * 0.2f;
            if (elapsedSinceRecall < 15000) // Within 6 in-game hours: moderately suppressed
                return decayedWeight * 0.5f;
        }

        return decayedWeight;
    }
}
