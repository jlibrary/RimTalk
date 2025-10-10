using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

namespace RimTalk.Patch;

public static class ThoughtTracker
{
    // Check if thought was already processed, if not mark it as processed
    public static void TryMarkAsProcessed(Pawn pawn, Thought thought)
    {
        if (pawn == null || thought == null || thought.def == null) return;
        var hediff = Hediff_Persona.GetOrAddNew(pawn);

        if (hediff == null) return;

        // Use centralized method from Hediff_Persona
        if (hediff.TryMarkAsSpoken(thought))
        {
            Cache.Get(pawn)?.AddTalkRequest(GetThoughtLabel(thought), pawn, TalkType.Thought);
        }
    }

    public static string GetThoughtLabel(Thought thought)
    {
        if (thought == null) return null;

        float offset;
        try
        {
            offset = thought.MoodOffset();
        }
        catch
        {
            return null; 
        }


        if (offset > 0)
            return $"new good feeling: {thought.LabelCap}";
        if (offset < 0)
            return $"new bad feeling: {thought.LabelCap}";
        return $"new feeling: {thought.LabelCap}";
    }

    public static bool IsThoughtStillActive(Pawn pawn, string thoughtLabel)
    {
        if (pawn?.needs?.mood?.thoughts == null || string.IsNullOrEmpty(thoughtLabel))
            return false;

        // Check memory thoughts
        var memoryThoughts = pawn.needs.mood.thoughts.memories?.Memories;
        if (memoryThoughts != null)
        {
            if (memoryThoughts.Any(m =>
                    m != null && !string.IsNullOrEmpty(m.LabelCap) && thoughtLabel.Contains(m.LabelCap)))
                return true;
        }

        // Check situational thoughts
        var allThoughts = new List<Thought>();
        pawn.needs.mood.thoughts.GetAllMoodThoughts(allThoughts);
        return allThoughts.Any(t =>
            t != null && !string.IsNullOrEmpty(t.LabelCap) && thoughtLabel.Contains(t.LabelCap));
    }
}

// Track memory thoughts when added
[HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory))]
[HarmonyPatch([typeof(Thought_Memory), typeof(Pawn)])]
public static class PatchMemoryThoughtHandlerTryGainMemory
{
    static void Postfix(Thought_Memory newThought, Pawn otherPawn)
    {
        if (Current.ProgramState != ProgramState.Playing)
        {
            return;
        }

        if (newThought?.pawn == null)
            return;

        // Skip all social thoughts (chitchat, insults, compliments, etc.)
        if (newThought is Thought_MemorySocial)
        {
            return;
        }

        // Only log thoughts with significant mood impact (±3 or more)
        float moodImpact;
        try
        {
            moodImpact = newThought.MoodOffset();
        }
        catch (Exception)
        {
            return; // Skip this thought if another mod has issues
        }

        if (Math.Abs(moodImpact) < 3f)
        {
            return;
        }

        if (moodImpact > 0 && newThought.pawn.InMentalState)
        {
            return;
        }

        ThoughtTracker.TryMarkAsProcessed(newThought.pawn, newThought);
    }
}

// Track situational thoughts by detecting changes
[HarmonyPatch(typeof(ThoughtHandler), nameof(ThoughtHandler.GetDistinctMoodThoughtGroups))]
public static class PatchThoughtHandlerGetDistinctMoodThoughtGroups
{
    // Store last known situational thoughts per pawn
    private static readonly Dictionary<Pawn, HashSet<string>> LastSituationalThoughts = new();

    static void Postfix(ThoughtHandler __instance, List<Thought> outThoughts)
    {
        if (Current.ProgramState != ProgramState.Playing)
        {
            return;
        }
        
        if (__instance.pawn == null || !__instance.pawn.Spawned)
            return;

        // Get current situational thoughts
        var currentThoughts = new HashSet<string>();
        foreach (var thought in outThoughts)
        {
            if (thought is Thought_Situational)
            {
                currentThoughts.Add(thought.def.defName);
            }
        }

        // Get previous thoughts for this pawn
        if (!LastSituationalThoughts.TryGetValue(__instance.pawn, out var previousThoughts))
        {
            previousThoughts = new HashSet<string>();
            LastSituationalThoughts[__instance.pawn] = previousThoughts;
        }

        // Find new thoughts (appeared)
        var newThoughts = currentThoughts.Except(previousThoughts).ToList();
        foreach (var thoughtDefName in newThoughts)
        {
            // Get the actual thought object
            var thought = outThoughts.FirstOrDefault(t => t.def.defName == thoughtDefName);
            if (thought != null)
            {
                try
                {
                    if (!(thought.MoodOffset() > 0 && __instance.pawn.InMentalState))
                    {
                        ThoughtTracker.TryMarkAsProcessed(__instance.pawn, thought);
                    }
                }
                catch
                {
                }
            }
        }

        // Update stored thoughts
        LastSituationalThoughts[__instance.pawn] = currentThoughts;
    }

    public static void Clear()
    {
        LastSituationalThoughts.Clear();
    }
}