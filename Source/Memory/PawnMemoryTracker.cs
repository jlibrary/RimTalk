using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimTalk.Memory;

/// <summary>
/// Core algorithmic engine for managing, decaying, selecting, and pruning pawn memories.
/// Zero background LLM calls, zero allocation on regular game ticks (lazy on-demand evaluation).
/// </summary>
public static class PawnMemoryTracker
{
    public const int MaxMemoriesPerPawn = 25;
    public const int MaxCoreTraumasPerPawn = 3;
    public const int MaxMilestonesPerTarget = 3;
    public const float MinSignificantWeight = 5f;
    public const float PurgeWeightThreshold = 1.5f;
    public const float DefaultHalfLifeDays = 4f;
    public const float TraumaHalfLifeDays = 7f;
    public const int DebounceTicks = 12000; // ~5 in-game hours debounce window

    /// <summary>
    /// Resolves the effective half-life in days for a memory entry.
    /// If halfLifeDays parameter matches DefaultHalfLifeDays (the default fallback),
    /// the entry's dynamic weight-calibrated half-life is used.
    /// Otherwise, the explicitly requested halfLifeDays is honored.
    /// </summary>
    public static float ResolveHalfLifeDays(MemoryEntry entry, float halfLifeDays = DefaultHalfLifeDays)
    {
        if (entry == null) return halfLifeDays;
        return Mathf.Abs(halfLifeDays - DefaultHalfLifeDays) < 0.001f ? entry.GetDynamicHalfLifeDays() : halfLifeDays;
    }

    /// <summary>
    /// Adds a new memory or updates/debounces an existing memory with matching target and event key.
    /// </summary>
    public static void AddOrUpdateMemory(
        List<MemoryEntry> memories,
        int targetPawnId,
        string targetPawnName,
        string eventKey,
        float weight,
        string note,
        int currentTick,
        bool isDirective = false,
        bool isCoreTrauma = false)
    {
        AddOrUpdateMemory(null, -1, memories, targetPawnId, targetPawnName, eventKey, weight, note, currentTick, isDirective, isCoreTrauma, false);
    }

    /// <summary>
    /// Adds or updates a memory while recording the mutation into MemoryHistory for debugging.
    /// </summary>
    public static void AddOrUpdateMemory(
        string sourcePawnName,
        int sourcePawnId,
        List<MemoryEntry> memories,
        int targetPawnId,
        string targetPawnName,
        string eventKey,
        float weight,
        string note,
        int currentTick,
        bool isDirective = false,
        bool isCoreTrauma = false)
    {
        AddOrUpdateMemory(sourcePawnName, sourcePawnId, memories, targetPawnId, targetPawnName, eventKey, weight, note, currentTick, isDirective, isCoreTrauma, false);
    }

    /// <summary>
    /// Adds or updates a memory, core trauma, player directive, or permanent milestone.
    /// </summary>
    public static void AddOrUpdateMemory(
        string sourcePawnName,
        int sourcePawnId,
        List<MemoryEntry> memories,
        int targetPawnId,
        string targetPawnName,
        string eventKey,
        float weight,
        string note,
        int currentTick,
        bool isDirective,
        bool isCoreTrauma,
        bool isMilestone)
    {
        AddOrUpdateMemory(sourcePawnName, sourcePawnId, memories, targetPawnId, targetPawnName, eventKey, weight, note, currentTick, isDirective, isCoreTrauma, isMilestone, MemoryPerspective.None);
    }

    /// <summary>
    /// Adds or updates a memory, core trauma, player directive, or permanent milestone with perspective tracking.
    /// </summary>
    public static void AddOrUpdateMemory(
        string sourcePawnName,
        int sourcePawnId,
        List<MemoryEntry> memories,
        int targetPawnId,
        string targetPawnName,
        string eventKey,
        float weight,
        string note,
        int currentTick,
        bool isDirective,
        bool isCoreTrauma,
        bool isMilestone,
        MemoryPerspective perspective)
    {
        if (memories == null || string.IsNullOrEmpty(eventKey) || Mathf.Abs(weight) < 1f)
            return;

        // Clean up completely faded memories inline without background tick overhead
        PurgeDecayedMemories(memories, currentTick, DefaultHalfLifeDays, sourcePawnName, sourcePawnId);

        // Enforce milestone capacity per target pawn (allows up to MaxMilestonesPerTarget = 3)
        if (isMilestone && targetPawnId >= 0)
        {
            bool sameEventExists = false;
            int targetMilestoneCount = 0;
            int lowestWeightIndex = -1;
            float lowestAbsWeight = float.MaxValue;

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                if (m != null && m.IsMilestone && m.TargetPawnId == targetPawnId)
                {
                    if (string.Equals(m.EventKey, eventKey, StringComparison.OrdinalIgnoreCase))
                    {
                        sameEventExists = true;
                        break;
                    }
                    targetMilestoneCount++;
                    float absWeight = Mathf.Abs(m.BaseWeight);
                    if (absWeight < lowestAbsWeight)
                    {
                        lowestAbsWeight = absWeight;
                        lowestWeightIndex = i;
                    }
                }
            }

            if (!sameEventExists && targetMilestoneCount >= MaxMilestonesPerTarget)
            {
                if (Mathf.Abs(weight) >= lowestAbsWeight && lowestWeightIndex >= 0)
                {
                    var evicted = memories[lowestWeightIndex];
                    memories.RemoveAt(lowestWeightIndex);
                    if (evicted != null)
                    {
                        MemoryHistory.Add(new MemoryLogEntry
                        {
                            SourcePawnName = sourcePawnName ?? string.Empty,
                            SourcePawnId = sourcePawnId,
                            TargetPawnName = evicted.TargetPawnName,
                            TargetPawnId = evicted.TargetPawnId,
                            ChangeType = MemoryChangeType.Evicted,
                            EventKey = evicted.EventKey,
                            Note = evicted.Note,
                            OldWeight = evicted.BaseWeight,
                            NewWeight = 0f,
                            Tick = currentTick,
                            Details = $"Milestone evicted by more significant event '{eventKey}' ({Mathf.Abs(weight):F1} >= {lowestAbsWeight:F1})"
                        });
                    }
                }
                else
                {
                    // Existing milestones are all more prominent: retain existing
                    return;
                }
            }
        }

        // Search for existing entry with same target, event key, and perspective
        MemoryEntry existing = null;
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            if (m != null && m.TargetPawnId == targetPawnId &&
                string.Equals(m.EventKey, eventKey, StringComparison.OrdinalIgnoreCase) &&
                m.Perspective == perspective)
            {
                existing = m;
                break;
            }
        }

        if (existing != null)
        {
            existing.LastTick = currentTick;
            existing.Count++;

            // Non-stacking single-instance events (e.g. HarmedMe, IHarmed, RescuedMe, SocialFight):
            // Keep base weight fixed according to vanilla mechanics. Only refresh timestamp and note.
            if (IsNonStackingEvent(eventKey))
            {
                existing.CreatedTick = currentTick;
                if (!string.IsNullOrEmpty(note))
                    existing.Note = note;
                if (!string.IsNullOrEmpty(targetPawnName))
                    existing.TargetPawnName = targetPawnName;
                existing.IsMilestone = isMilestone || existing.IsMilestone;
                return;
            }

            int elapsed = currentTick - existing.CreatedTick;
            float oldWeight = existing.BaseWeight;
            if (elapsed < DebounceTicks)
            {
                // Debounce rapid repeats: add diminishing delta weight and refresh note if provided
                float newWeight = Mathf.Clamp(existing.BaseWeight + (weight * 0.4f), -100f, 100f);
                existing.BaseWeight = newWeight;
                existing.CreatedTick = currentTick;
                if (!string.IsNullOrEmpty(note))
                    existing.Note = note;
                if (!string.IsNullOrEmpty(targetPawnName))
                    existing.TargetPawnName = targetPawnName;
                existing.IsDirective = isDirective || existing.IsDirective;
                existing.IsCoreTrauma = isCoreTrauma || existing.IsCoreTrauma;
                existing.IsMilestone = isMilestone || existing.IsMilestone;

                MemoryHistory.Add(new MemoryLogEntry
                {
                    SourcePawnName = sourcePawnName ?? string.Empty,
                    SourcePawnId = sourcePawnId,
                    TargetPawnName = existing.TargetPawnName,
                    TargetPawnId = existing.TargetPawnId,
                    ChangeType = MemoryChangeType.Updated,
                    EventKey = existing.EventKey,
                    Note = existing.Note,
                    OldWeight = oldWeight,
                    NewWeight = newWeight,
                    IsDirective = existing.IsDirective,
                    IsCoreTrauma = existing.IsCoreTrauma,
                    Tick = currentTick,
                    Details = $"Debounced repeat (+{weight * 0.4f:F1} delta)"
                });
                return;
            }

            // Older existing memory: update with blended weight
            float halfLife = existing.GetDynamicHalfLifeDays();
            float currentDecayed = isMilestone ? existing.BaseWeight : existing.GetDecayedWeight(currentTick, halfLife);
            float blendedWeight = Mathf.Clamp(currentDecayed + weight, -100f, 100f);
            existing.BaseWeight = blendedWeight;
            existing.CreatedTick = currentTick;
            if (!string.IsNullOrEmpty(note))
                existing.Note = note;
            if (!string.IsNullOrEmpty(targetPawnName))
                existing.TargetPawnName = targetPawnName;
            existing.IsDirective = isDirective || existing.IsDirective;
            existing.IsCoreTrauma = isCoreTrauma || existing.IsCoreTrauma;
            existing.IsMilestone = isMilestone || existing.IsMilestone;

            MemoryHistory.Add(new MemoryLogEntry
            {
                SourcePawnName = sourcePawnName ?? string.Empty,
                SourcePawnId = sourcePawnId,
                TargetPawnName = existing.TargetPawnName,
                TargetPawnId = existing.TargetPawnId,
                ChangeType = MemoryChangeType.Updated,
                EventKey = existing.EventKey,
                Note = existing.Note,
                OldWeight = oldWeight,
                NewWeight = blendedWeight,
                IsDirective = existing.IsDirective,
                IsCoreTrauma = existing.IsCoreTrauma,
                Tick = currentTick,
                Details = $"Updated blended weight ({currentDecayed:F1} + {weight:F1})"
            });
            return;
        }

        // Add brand new entry
        var newEntry = new MemoryEntry(targetPawnId, targetPawnName, eventKey, weight, currentTick, note, isDirective, isCoreTrauma, isMilestone, perspective, 1, currentTick);
        memories.Add(newEntry);

        MemoryHistory.Add(new MemoryLogEntry
        {
            SourcePawnName = sourcePawnName ?? string.Empty,
            SourcePawnId = sourcePawnId,
            TargetPawnName = targetPawnName,
            TargetPawnId = targetPawnId,
            ChangeType = isMilestone ? MemoryChangeType.MilestoneRecorded : (isCoreTrauma ? MemoryChangeType.CoreTraumaAdded : (isDirective ? MemoryChangeType.DirectiveSet : MemoryChangeType.Added)),
            EventKey = eventKey,
            Note = note,
            OldWeight = 0f,
            NewWeight = weight,
            IsDirective = isDirective,
            IsCoreTrauma = isCoreTrauma,
            Tick = currentTick,
            Details = isMilestone ? "Permanent milestone recorded" : (isCoreTrauma ? "Core trauma recorded" : (isDirective ? "Directive recorded" : "New episodic memory"))
        });

        // Cap memories by category to protect Directives, Core Traumas, and Milestones from ordinary memory eviction
        if (isCoreTrauma)
        {
            PruneWeakestTraumas(memories, currentTick, sourcePawnName, sourcePawnId);
        }
        else if (!isDirective && !isMilestone)
        {
            PruneWeakestOrdinaryMemories(memories, currentTick, sourcePawnName, sourcePawnId);
        }
    }

    /// <summary>
    /// Selects all active severe core traumas (e.g. grief over loved ones) above the significance threshold.
    /// Returns up to maxTraumas items ordered by descending severity.
    /// Deduplicates multiple traumas about the same individual so one pawn does not monopolize all slots.
    /// </summary>
    public static List<MemoryEntry> SelectCoreTraumas(List<MemoryEntry> memories, int currentTick, float halfLifeDays = 7f, int maxTraumas = 3)
    {
        var list = new List<MemoryEntry>();
        if (memories == null || memories.Count == 0) return list;

        const float threshold = 35f;
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            if (m == null || !m.IsCoreTrauma) continue;

            float absWeight = Mathf.Abs(m.GetDecayedWeight(currentTick, halfLifeDays));
            if (absWeight >= threshold)
            {
                list.Add(m);
            }
        }

        if (list.Count > 1)
        {
            list.Sort((a, b) => Mathf.Abs(b.GetDecayedWeight(currentTick, halfLifeDays))
                .CompareTo(Mathf.Abs(a.GetDecayedWeight(currentTick, halfLifeDays))));
        }

        // Deduplicate core traumas by target pawn so a single person doesn't monopolize multiple slots
        var result = new List<MemoryEntry>(Math.Min(list.Count, maxTraumas));
        var seenTargetIds = new HashSet<int>();

        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry.TargetPawnId > 0)
            {
                if (!seenTargetIds.Add(entry.TargetPawnId))
                    continue; // Skip lower-weight duplicate trauma for the same person
            }

            result.Add(entry);
            if (result.Count >= maxTraumas)
                break;
        }

        return result;
    }

    /// <summary>
    /// Selects the most prominent core trauma (e.g. grief over loved one, loss of limb)
    /// that currently pervades the pawn's overall mindset.
    /// </summary>
    public static MemoryEntry SelectCoreTrauma(List<MemoryEntry> memories, int currentTick, float halfLifeDays = 7f)
    {
        var traumas = SelectCoreTraumas(memories, currentTick, halfLifeDays, 1);
        return traumas.Count > 0 ? traumas[0] : null;
    }

    /// <summary>
    /// Selects all active player directives given to this pawn (up to maxDirectives).
    /// </summary>
    public static List<MemoryEntry> SelectActiveDirectives(List<MemoryEntry> memories, int currentTick, float halfLifeDays = 20f, int maxDirectives = 5)
    {
        var list = new List<MemoryEntry>();
        if (memories == null || memories.Count == 0) return list;

        foreach (var m in memories)
        {
            if (m is not { IsDirective: true }) continue;

            // Directives are explicit active player rules; do not drop them due to passive time decay unless cleared
            if (Mathf.Abs(m.BaseWeight) < MinSignificantWeight) continue;

            list.Add(m);
        }

        if (list.Count > 1)
        {
            list.Sort((a, b) => b.CreatedTick.CompareTo(a.CreatedTick));
            if (list.Count > maxDirectives)
                list.RemoveRange(maxDirectives, list.Count - maxDirectives);
        }

        return list;
    }

    /// <summary>
    /// Selects the most recent active player directive given to this pawn.
    /// </summary>
    public static MemoryEntry SelectActiveDirective(List<MemoryEntry> memories, int currentTick, float halfLifeDays = 20f)
    {
        var list = SelectActiveDirectives(memories, currentTick, halfLifeDays, 1);
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>
    /// Strips legacy '(had positive experience ...)' and '(had grievance ...)' boilerplate from notes.
    /// </summary>
    public static string StripBoilerplate(string note)
    {
        if (string.IsNullOrEmpty(note)) return string.Empty;
        var trimmed = note.Trim();
        if (trimmed.StartsWith("had positive experience (", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
            return trimmed.Substring(25, trimmed.Length - 26).Trim();
        if (trimmed.StartsWith("had grievance or conflict (", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
            return trimmed.Substring(27, trimmed.Length - 28).Trim();
        return trimmed;
    }

    /// <summary>
    /// Selects top personal deed/crisis memories (TargetPawnId == -1, excluding directives and core traumas) sorted by absolute recall score.
    /// </summary>
    public static List<MemoryEntry> SelectPersonalMemories(
        List<MemoryEntry> memories,
        int currentTick,
        float halfLifeDays = DefaultHalfLifeDays,
        int maxMemories = 2)
    {
        var list = new List<MemoryEntry>();
        if (memories == null || memories.Count == 0)
            return list;

        foreach (var entry in memories)
        {
            if (entry == null || entry.TargetPawnId != -1 || entry.IsDirective || entry.IsCoreTrauma)
                continue;

            float score = entry.GetRecallScore(currentTick, ResolveHalfLifeDays(entry, halfLifeDays));
            if (Mathf.Abs(score) >= MinSignificantWeight)
            {
                list.Add(entry);
            }
        }

        if (list.Count > 1)
        {
            list.Sort((a, b) => Mathf.Abs(b.GetRecallScore(currentTick, ResolveHalfLifeDays(b, halfLifeDays)))
                .CompareTo(Mathf.Abs(a.GetRecallScore(currentTick, ResolveHalfLifeDays(a, halfLifeDays)))));
            if (list.Count > maxMemories)
                list.RemoveRange(maxMemories, list.Count - maxMemories);
        }

        return list;
    }

    /// <summary>
    /// Selects top recall memories for a specific target pawn sorted by absolute recall score.
    /// Does not force artificial positive/negative pairing.
    /// </summary>
    public static List<MemoryEntry> SelectTopRecallMemories(
        List<MemoryEntry> memories,
        int targetPawnId,
        int currentTick,
        float halfLifeDays = DefaultHalfLifeDays,
        int maxMemories = 2)
    {
        var list = new List<MemoryEntry>();
        if (memories == null || memories.Count == 0 || targetPawnId < 0)
            return list;

        foreach (var entry in memories)
        {
            if (entry == null || entry.TargetPawnId != targetPawnId)
                continue;

            float score = entry.GetRecallScore(currentTick, ResolveHalfLifeDays(entry, halfLifeDays));
            if (Mathf.Abs(score) >= MinSignificantWeight)
            {
                list.Add(entry);
            }
        }

        if (list.Count > 1)
        {
            list.Sort((a, b) => Mathf.Abs(b.GetRecallScore(currentTick, ResolveHalfLifeDays(b, halfLifeDays)))
                .CompareTo(Mathf.Abs(a.GetRecallScore(currentTick, ResolveHalfLifeDays(a, halfLifeDays)))));
            if (list.Count > maxMemories)
                list.RemoveRange(maxMemories, list.Count - maxMemories);
        }

        return list;
    }

    /// <summary>
    /// Selects the most recent episodic memories toward a target pawn, excluding already selected prominent memories.
    /// Orders by descending CreatedTick (most recent first) and filters by MinSignificantWeight.
    /// </summary>
    public static List<MemoryEntry> SelectRecentMemories(
        List<MemoryEntry> memories,
        int targetPawnId,
        int currentTick,
        List<MemoryEntry> excludeMemories = null,
        float halfLifeDays = DefaultHalfLifeDays,
        int maxMemories = 2)
    {
        var list = new List<MemoryEntry>();
        if (memories == null || memories.Count == 0 || targetPawnId < 0)
            return list;

        foreach (var entry in memories)
        {
            if (entry == null || entry.TargetPawnId != targetPawnId)
                continue;

            if (excludeMemories != null && excludeMemories.Contains(entry))
                continue;

            float score = entry.GetRecallScore(currentTick, ResolveHalfLifeDays(entry, halfLifeDays));
            if (Mathf.Abs(score) >= MinSignificantWeight)
            {
                list.Add(entry);
            }
        }

        if (list.Count > 1)
        {
            list.Sort((a, b) => b.CreatedTick.CompareTo(a.CreatedTick));
            if (list.Count > maxMemories)
                list.RemoveRange(maxMemories, list.Count - maxMemories);
        }

        return list;
    }

    /// <summary>
    /// Selects the most prominent positive and negative memories for a specific target pawn.
    /// Maintained for backwards compatibility.
    /// </summary>
    public static (MemoryEntry positive, MemoryEntry negative) SelectTopMemories(
        List<MemoryEntry> memories,
        int targetPawnId,
        int currentTick,
        float halfLifeDays = DefaultHalfLifeDays)
    {
        if (memories == null || memories.Count == 0 || targetPawnId < 0)
            return (null, null);

        MemoryEntry bestPos = null;
        float maxPosScore = MinSignificantWeight;

        MemoryEntry bestNeg = null;
        float minNegScore = -MinSignificantWeight;

        foreach (var entry in memories)
        {
            if (entry == null || entry.TargetPawnId != targetPawnId)
                continue;

            float score = entry.GetRecallScore(currentTick, ResolveHalfLifeDays(entry, halfLifeDays));

            if (score > maxPosScore)
            {
                maxPosScore = score;
                bestPos = entry;
            }
            else if (score < minNegScore)
            {
                minNegScore = score;
                bestNeg = entry;
            }
        }

        return (bestPos, bestNeg);
    }

    /// <summary>
    /// Records recall timestamp on selected memories to prevent immediate echoing in consecutive dialogue ticks.
    /// </summary>
    public static void MarkRecalled(MemoryEntry entry, int currentTick)
    {
        if (entry != null)
        {
            entry.LastRecalledTick = currentTick;
        }
    }

    /// <summary>
    /// Prunes completely faded memories whose decayed weight has dropped below the purge threshold.
    /// Directives are excluded as they are explicit active rules.
    /// </summary>
    public static void PurgeDecayedMemories(List<MemoryEntry> memories, int currentTick, float halfLifeDays = DefaultHalfLifeDays)
    {
        PurgeDecayedMemories(memories, currentTick, halfLifeDays, null, -1);
    }

    /// <summary>
    /// Routine procedural conversations (chitchat, deep talk) are completely excluded from episodic memories.
    /// </summary>
    public static bool IsExcludedRoutineChat(string eventKey)
    {
        if (string.IsNullOrEmpty(eventKey)) return false;
        return eventKey.Equals("Chitchat", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("DeepTalk", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("CrashedTogether", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("RimTalk_Chitchat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prunes completely faded memories whose decayed weight has dropped below the purge threshold,
    /// and purges excluded routine chats (chitchat/deeptalk), logging removals to MemoryHistory.
    /// </summary>
    public static void PurgeDecayedMemories(List<MemoryEntry> memories, int currentTick, float halfLifeDays, string sourcePawnName, int sourcePawnId)
    {
        if (memories == null || memories.Count == 0) return;

        for (int i = memories.Count - 1; i >= 0; i--)
        {
            var m = memories[i];
            if (m == null)
            {
                memories.RemoveAt(i);
                continue;
            }

            bool isIgnored = IsExcludedRoutineChat(m.EventKey);
            float effectiveHalfLife = ResolveHalfLifeDays(m, halfLifeDays);
            bool isDecayed = !m.IsDirective && !m.IsMilestone && Mathf.Abs(m.GetDecayedWeight(currentTick, effectiveHalfLife)) < PurgeWeightThreshold;

            if (isIgnored || isDecayed)
            {
                MemoryHistory.Add(new MemoryLogEntry
                {
                    SourcePawnName = sourcePawnName ?? string.Empty,
                    SourcePawnId = sourcePawnId,
                    TargetPawnName = m.TargetPawnName,
                    TargetPawnId = m.TargetPawnId,
                    ChangeType = isIgnored ? MemoryChangeType.Cleared : MemoryChangeType.PurgedDecay,
                    EventKey = m.EventKey,
                    Note = m.Note,
                    OldWeight = m.BaseWeight,
                    NewWeight = 0f,
                    IsDirective = m.IsDirective,
                    IsCoreTrauma = m.IsCoreTrauma,
                    Tick = currentTick,
                    Details = isIgnored ? "Routine conversation purged" : "Decayed below threshold (1.5) and purged"
                });
                memories.RemoveAt(i);
            }
        }
    }

    private static void PruneWeakestOrdinaryMemories(List<MemoryEntry> memories, int currentTick, string sourcePawnName = null, int sourcePawnId = -1)
    {
        int ordinaryCount = 0;
        foreach (var m in memories)
        {
            if (m != null && !m.IsDirective && !m.IsCoreTrauma && !m.IsMilestone)
                ordinaryCount++;
        }

        while (ordinaryCount > MaxMemoriesPerPawn)
        {
            int weakestIndex = -1;
            float minAbsWeight = float.MaxValue;

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                if (m == null)
                {
                    weakestIndex = i;
                    break;
                }

                // Never prune Directives, Core Traumas, or Milestones
                if (m.IsDirective || m.IsCoreTrauma || m.IsMilestone)
                    continue;

                float absWeight = Mathf.Abs(m.GetDecayedWeight(currentTick, m.GetDynamicHalfLifeDays()));
                if (absWeight < minAbsWeight)
                {
                    minAbsWeight = absWeight;
                    weakestIndex = i;
                }
            }

            if (weakestIndex >= 0 && weakestIndex < memories.Count)
            {
                var evicted = memories[weakestIndex];
                if (evicted != null)
                {
                    MemoryHistory.Add(new MemoryLogEntry
                    {
                        SourcePawnName = sourcePawnName ?? string.Empty,
                        SourcePawnId = sourcePawnId,
                        TargetPawnName = evicted.TargetPawnName,
                        TargetPawnId = evicted.TargetPawnId,
                        ChangeType = MemoryChangeType.Evicted,
                        EventKey = evicted.EventKey,
                        Note = evicted.Note,
                        OldWeight = evicted.BaseWeight,
                        NewWeight = 0f,
                        IsDirective = evicted.IsDirective,
                        IsCoreTrauma = evicted.IsCoreTrauma,
                        Tick = currentTick,
                        Details = "Evicted exceeding capacity limit (25)"
                    });
                }
                memories.RemoveAt(weakestIndex);
                ordinaryCount--;
            }
            else
            {
                break;
            }
        }
    }

    private static void PruneWeakestTraumas(List<MemoryEntry> memories, int currentTick, string sourcePawnName = null, int sourcePawnId = -1)
    {
        int traumaCount = 0;
        foreach (var m in memories)
        {
            if (m != null && m.IsCoreTrauma)
                traumaCount++;
        }

        while (traumaCount > MaxCoreTraumasPerPawn)
        {
            int weakestIndex = -1;
            float minAbsWeight = float.MaxValue;

            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                if (m == null || !m.IsCoreTrauma)
                    continue;

                float absWeight = Mathf.Abs(m.GetDecayedWeight(currentTick, TraumaHalfLifeDays));
                if (absWeight < minAbsWeight)
                {
                    minAbsWeight = absWeight;
                    weakestIndex = i;
                }
            }

            if (weakestIndex >= 0 && weakestIndex < memories.Count)
            {
                var evicted = memories[weakestIndex];
                if (evicted != null)
                {
                    MemoryHistory.Add(new MemoryLogEntry
                    {
                        SourcePawnName = sourcePawnName ?? string.Empty,
                        SourcePawnId = sourcePawnId,
                        TargetPawnName = evicted.TargetPawnName,
                        TargetPawnId = evicted.TargetPawnId,
                        ChangeType = MemoryChangeType.Evicted,
                        EventKey = evicted.EventKey,
                        Note = evicted.Note,
                        OldWeight = evicted.BaseWeight,
                        NewWeight = 0f,
                        IsDirective = evicted.IsDirective,
                        IsCoreTrauma = evicted.IsCoreTrauma,
                        Tick = currentTick,
                        Details = "Evicted exceeding trauma capacity limit (3)"
                    });
                }
                memories.RemoveAt(weakestIndex);
                traumaCount--;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Identifies single-instance vanilla social thoughts and tales that have a stack limit of 1
    /// (e.g. HarmedMe, IHarmed, RescuedMe, SocialFight, Marriage, CrashedTogether).
    /// These maintain their fixed calibrated weight without runaway debounce accumulation.
    /// </summary>
    private static bool IsNonStackingEvent(string eventKey)
    {
        if (string.IsNullOrEmpty(eventKey)) return false;
        return eventKey.Equals("HarmedMe", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("IHarmed", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("RescuedMe", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("RescuedMeByOfferingHelp", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("IRescued", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("RecruitedMe", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("IRecruited", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("DidSurgery", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("BotchedMySurgery", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("IBotchedSurgery", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("CapturedMe", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("ICaptured", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("SocialFight", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("Marriage", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("BecameLover", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("SavedColonistLife", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("SavedLife", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("ExecutedPrisoner", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("IExecuted", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("IKilled", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("KilledColonist", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("CrashedTogether", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("TendedMe", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("TendedPatient", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("GaveBirth", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("GaveBirthWith", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("Divorced", StringComparison.OrdinalIgnoreCase) ||
               eventKey.Equals("MurderedKin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Selects the active permanent milestone held toward the target pawn, if any.
    /// Rotates across multiple milestones (LRU / round-robin) while enforcing a 1-day cooldown
    /// across all milestones toward the target to avoid repetitive recitation.
    /// </summary>
    public static MemoryEntry SelectMilestone(
        List<MemoryEntry> memories,
        int targetPawnId,
        int currentTick,
        int cooldownTicks = 60000)
    {
        if (memories == null || memories.Count == 0 || targetPawnId < 0)
            return null;

        // 1. Enforce fatigue cooldown across all milestones for this target (max 1 milestone injected per day)
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            if (m != null && m.IsMilestone && m.TargetPawnId == targetPawnId)
            {
                if (m.LastRecalledTick > 0)
                {
                    int elapsed = currentTick - m.LastRecalledTick;
                    if (elapsed >= 0 && elapsed < cooldownTicks)
                        return null;
                }
            }
        }

        // 2. Select eligible milestone with round-robin rotation (un-recalled first, then least recently recalled)
        MemoryEntry best = null;
        int oldestRecallTick = int.MaxValue;
        int oldestCreatedTick = int.MaxValue;

        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            if (m == null || !m.IsMilestone || m.TargetPawnId != targetPawnId)
                continue;

            int recallTick = m.LastRecalledTick <= 0 ? -1 : m.LastRecalledTick;
            if (recallTick == -1)
            {
                // Unrecalled: prioritize earliest created
                if (best == null || oldestRecallTick > -1 || m.CreatedTick < oldestCreatedTick)
                {
                    best = m;
                    oldestRecallTick = -1;
                    oldestCreatedTick = m.CreatedTick;
                }
            }
            else if (oldestRecallTick > -1 && recallTick < oldestRecallTick)
            {
                // Recalled: pick the one recalled longest ago
                best = m;
                oldestRecallTick = recallTick;
            }
        }

        return best;
    }

    /// <summary>
    /// Calculates shared colony seniority in full years given the colonist ticks of two pawns.
    /// 1 RimWorld year = 60 days = 3,600,000 ticks.
    /// </summary>
    public static int CalculateSharedYears(float ticksA, float ticksB)
    {
        float minTicks = Mathf.Min(ticksA, ticksB);
        return Mathf.FloorToInt(minTicks / 3600000f);
    }

    /// <summary>
    /// Calculates individual colonist seniority in full years given colonist ticks.
    /// </summary>
    public static int CalculateColonyYears(float colonistTicks)
    {
        return Mathf.FloorToInt(colonistTicks / 3600000f);
    }
}
