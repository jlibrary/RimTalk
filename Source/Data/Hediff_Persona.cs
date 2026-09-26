using System.Collections.Generic;
using RimTalk.Memory;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Data;

public class Hediff_Persona : Hediff
{
    private const string RimtalkHediff = "RimTalk_PersonaData";
    private Dictionary<string, int> _spokenThoughtTicks = new();
    private List<MemoryEntry> _memories = new();
    [Unsaved(false)]
    private int _lastImpressionTick = -1;
    [Unsaved(false)]
    private int _lastImpressionTargetId = -1;
    [Unsaved(false)]
    private string _cachedImpression = string.Empty;
    public string Personality;
    public float TalkInitiationWeight = 1.0f;
    public override bool Visible => false;

    public List<MemoryEntry> Memories => _memories ??= new();
    
    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref Personality, "Personality");
        Scribe_Values.Look(ref TalkInitiationWeight, "TalkInitiationWeight", 1.0f);
        Scribe_Collections.Look(ref _spokenThoughtTicks, "SpokenThoughtTicks", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref _memories, "memories", LookMode.Deep);
        
        if (_spokenThoughtTicks == null)
        {
            _spokenThoughtTicks = new Dictionary<string, int>();
        }
        if (_memories == null)
        {
            _memories = new List<MemoryEntry>();
        }
    }
    
    public static Hediff_Persona GetOrAddNew(Pawn pawn)
    {
        var def = DefDatabase<HediffDef>.GetNamedSilentFail(RimtalkHediff);
        if (pawn?.health?.hediffSet == null || def == null) return null;

        if (pawn.health.hediffSet.GetFirstHediffOfDef(def) is not Hediff_Persona hediff)
        {
            hediff = (Hediff_Persona)HediffMaker.MakeHediff(def, pawn);
        
            // Assign a random personality on creation
            PersonalityData randomPersonalityData =
                pawn.IsMutant ? Constant.PersonaNonHuman
                : pawn.RaceProps.Humanlike ? Constant.Personalities.RandomElement()
                : pawn.RaceProps.Animal ? Constant.PersonaAnimal
                : pawn.RaceProps.IsMechanoid ? Constant.PersonaMech
                : Constant.PersonaNonHuman;
            hediff.Personality = randomPersonalityData.Persona;
        
            if (pawn.IsSlave || pawn.IsPrisoner || pawn.IsVisitor() || pawn.IsEnemy())
            {
                hediff.TalkInitiationWeight = 0.2f;
            }
            else
            {
                hediff.TalkInitiationWeight = randomPersonalityData.Chattiness;
            }
        
            pawn.health.AddHediff(hediff);
        }
    
        // Ensure dictionary is initialized (for both new and existing hediffs)
        hediff._spokenThoughtTicks ??= new Dictionary<string, int>();
    
        return hediff;
    }
    
    // Check if thought was spoken recently, if not mark it as spoken
    // Returns true if successfully marked (was not spoken recently)
    // Returns false if already spoken recently (within intervalTicks)
    public bool TryMarkAsSpoken(Thought thought)
    {
        string key = $"{thought.def.defName}_{thought.CurStageIndex}";
        int currentTick = Find.TickManager.TicksGame;
    
        // Randomize interval from 1 to 2.5 days
        int randomInterval = Random.Range(60000, 150000);
    
        if (_spokenThoughtTicks.TryGetValue(key, out int lastTick))
        {
            if (currentTick - lastTick < randomInterval)
            {
                return false; // Already spoken recently
            }
        }
    
        _spokenThoughtTicks[key] = currentTick;

        // Also mark for nearby pawns so they don't talk about the same thing
        var nearbyPawns = PawnSelector.GetAllNearByPawns(thought.pawn);
        foreach (var p in nearbyPawns)
        {
            if (p == thought.pawn) continue; 
            var hediff = GetOrAddNew(p);
            if (hediff != null)
            {
                hediff._spokenThoughtTicks[key] = currentTick;
            }
        }

        return true;
    }

    /// <summary>
    /// Records or updates an episodic memory toward another pawn with lazy exponential decay.
    /// </summary>
    public void RecordMemory(int targetPawnId, string targetPawnName, string eventKey, float weight, string note, bool isDirective = false, bool isCoreTrauma = false)
    {
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        PawnMemoryTracker.AddOrUpdateMemory(pawn?.LabelShort, pawn?.thingIDNumber ?? -1, Memories, targetPawnId, targetPawnName, eventKey, weight, note, currentTick, isDirective, isCoreTrauma, false);
    }

    /// <summary>
    /// Records or updates a permanent relationship milestone toward another pawn exempt from time decay.
    /// </summary>
    public void RecordMilestone(int targetPawnId, string targetPawnName, string eventKey, float weight, string note)
    {
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        PawnMemoryTracker.AddOrUpdateMemory(pawn?.LabelShort, pawn?.thingIDNumber ?? -1, Memories, targetPawnId, targetPawnName, eventKey, weight, note, currentTick, isDirective: false, isCoreTrauma: false, isMilestone: true);
    }

    /// <summary>
    /// Records or updates a memory with full parameter control.
    /// </summary>
    public void RecordMemory(int targetPawnId, string targetPawnName, string eventKey, float weight, string note, bool isDirective, bool isCoreTrauma, bool isMilestone)
    {
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        PawnMemoryTracker.AddOrUpdateMemory(pawn?.LabelShort, pawn?.thingIDNumber ?? -1, Memories, targetPawnId, targetPawnName, eventKey, weight, note, currentTick, isDirective, isCoreTrauma, isMilestone);
    }

    /// <summary>
    /// Updates active player directives given to this pawn.
    /// Returns true if directives were modified (added, changed, or cleared); returns false if identical to existing directives.
    /// </summary>
    public bool UpdateDirectives(System.Collections.Generic.List<string> directives)
    {
        int inputCount = directives?.Count ?? 0;
        int existingCount = 0;
        bool match = true;

        for (int i = 0; i < Memories.Count; i++)
        {
            if (Memories[i].IsDirective)
            {
                if (existingCount >= inputCount || !string.Equals(Memories[i].Note, directives![existingCount]?.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                }
                existingCount++;
            }
        }

        if (match && existingCount == inputCount)
        {
            return false;
        }

        Memories.RemoveAll(m => m.IsDirective);
        if (directives != null)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int max = System.Math.Min(directives.Count, 5);
            for (int i = 0; i < max; i++)
            {
                var text = directives[i];
                if (!string.IsNullOrWhiteSpace(text))
                {
                    PawnMemoryTracker.AddOrUpdateMemory(pawn?.LabelShort, pawn?.thingIDNumber ?? -1, Memories, -999, "Player", $"PlayerDirective_{i}", 90f, text.Trim(), currentTick, isDirective: true);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Records or updates multiple explicit player directives given to this pawn.
    /// Replaces active directives with the newly synthesized atomic rules.
    /// Preserved for strict ABI backward compatibility.
    /// </summary>
    public void RecordDirectives(System.Collections.Generic.List<string> directives)
    {
        UpdateDirectives(directives);
    }

    /// <summary>
    /// Records an explicit player directive/command given to this pawn.
    /// Overwrites or reinforces active directive with high retention weight.
    /// </summary>
    public void RecordDirective(string directiveText)
    {
        if (string.IsNullOrWhiteSpace(directiveText)) return;
        RecordDirectives(new System.Collections.Generic.List<string> { directiveText });
    }

    /// <summary>
    /// Records an all-pervading emotional trauma or deep grief (e.g. death of spouse, loss of body part).
    /// Preserved for strict ABI backward compatibility.
    /// </summary>
    public void RecordCoreTrauma(string eventKey, float weight, string note)
    {
        RecordCoreTrauma(-1, string.Empty, eventKey, weight, note);
    }

    /// <summary>
    /// Records an all-pervading emotional trauma or deep grief associated with a specific target pawn.
    /// </summary>
    public void RecordCoreTrauma(int targetPawnId, string targetPawnName, string eventKey, float weight, string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        PawnMemoryTracker.AddOrUpdateMemory(pawn?.LabelShort, pawn?.thingIDNumber ?? -1, Memories, targetPawnId, targetPawnName ?? string.Empty, eventKey, weight, note.Trim(), currentTick, isCoreTrauma: true);
    }

    /// <summary>
    /// Retrieves a token-minimal psychological impression directive of the target pawn,
    /// combined with any active player directives and severe core traumas.
    /// </summary>
    public string GetImpressionOf(Pawn targetPawn)
    {
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        int targetId = targetPawn?.thingIDNumber ?? -1;
        if (currentTick == _lastImpressionTick && targetId == _lastImpressionTargetId)
            return _cachedImpression;

        // Synchronize active thoughts from vanilla relationship tab before evaluating impression
        if (targetPawn != null && pawn != null)
        {
            MemoryHookService.SyncActiveSocialThoughts(pawn, targetPawn);
        }

        if (_memories == null || _memories.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();

        // 1. Active Player Directives (if any)
        var directives = PawnMemoryTracker.SelectActiveDirectives(_memories, currentTick);
        if (directives.Count > 0)
        {
            sb.AppendLine(MemoryFormatter.FormatDirectives(directives));
        }

        // 2. Severe Core Trauma / Grief (if any)
        var coreTraumas = PawnMemoryTracker.SelectCoreTraumas(_memories, currentTick);
        if (coreTraumas.Count > 0)
        {
            sb.AppendLine(MemoryFormatter.FormatCoreTraumas(coreTraumas));
        }

        // 2b. Recent Personal Experiences (deeds/crises)
        var personalMemories = PawnMemoryTracker.SelectPersonalMemories(_memories, currentTick);
        if (personalMemories.Count > 0)
        {
            for (int i = 0; i < personalMemories.Count; i++)
            {
                PawnMemoryTracker.MarkRecalled(personalMemories[i], currentTick);
            }
            string personalText = MemoryFormatter.FormatPersonalMemories(personalMemories);
            if (!string.IsNullOrEmpty(personalText))
            {
                sb.AppendLine(personalText);
            }
        }

        // 3. Relational 1:1 Impression toward target
        if (targetPawn != null)
        {
            // 3a. Colony Seniority
            if (pawn?.records != null && targetPawn.records != null)
            {
                int sharedYears = PawnMemoryTracker.CalculateSharedYears(
                    pawn.records.GetValue(RimWorld.RecordDefOf.TimeAsColonistOrColonyAnimal),
                    targetPawn.records.GetValue(RimWorld.RecordDefOf.TimeAsColonistOrColonyAnimal));
                if (sharedYears >= 1)
                {
                    int opinion = pawn.relations?.OpinionOf(targetPawn) ?? 0;
                    sb.AppendLine(MemoryFormatter.FormatSeniority(sharedYears, opinion));
                }
            }

            // 3b. Permanent Relationship Milestone (respecting 1-day fatigue cooldown)
            var milestone = PawnMemoryTracker.SelectMilestone(_memories, targetPawn.thingIDNumber, currentTick);
            if (milestone != null)
            {
                PawnMemoryTracker.MarkRecalled(milestone, currentTick);
                string milestoneText = MemoryFormatter.FormatMilestone(milestone, targetPawn.LabelShort);
                if (!string.IsNullOrEmpty(milestoneText))
                {
                    sb.AppendLine(milestoneText);
                }
            }

            // 3c. Recent episodic memories
            var topMemories = PawnMemoryTracker.SelectTopRecallMemories(_memories, targetPawn.thingIDNumber, currentTick, maxMemories: 2);
            for (int i = 0; i < topMemories.Count; i++)
            {
                PawnMemoryTracker.MarkRecalled(topMemories[i], currentTick);
            }

            var impression = MemoryFormatter.FormatMemories(targetPawn.LabelShort, topMemories);
            if (!string.IsNullOrEmpty(impression))
            {
                sb.AppendLine(impression);
            }
        }

        _lastImpressionTick = currentTick;
        _lastImpressionTargetId = targetId;
        _cachedImpression = sb.ToString().TrimEnd();
        return _cachedImpression;
    }

    /// <summary>
    /// Retrieves relational 1:1 episodic memory impression toward target pawn without directives or core traumas.
    /// Used for additional scene participants.
    /// </summary>
    public string GetRelationalMemoryOf(Pawn targetPawn)
    {
        if (targetPawn == null || pawn == null || _memories == null || _memories.Count == 0)
            return string.Empty;

        MemoryHookService.SyncActiveSocialThoughts(pawn, targetPawn);
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        var sb = new System.Text.StringBuilder();

        var milestone = PawnMemoryTracker.SelectMilestone(_memories, targetPawn.thingIDNumber, currentTick);
        if (milestone != null)
        {
            PawnMemoryTracker.MarkRecalled(milestone, currentTick);
            string milestoneText = MemoryFormatter.FormatMilestone(milestone, targetPawn.LabelShort);
            if (!string.IsNullOrEmpty(milestoneText))
            {
                sb.AppendLine(milestoneText);
            }
        }

        var topMemories = PawnMemoryTracker.SelectTopRecallMemories(_memories, targetPawn.thingIDNumber, currentTick, maxMemories: 2);
        for (int i = 0; i < topMemories.Count; i++)
        {
            PawnMemoryTracker.MarkRecalled(topMemories[i], currentTick);
        }

        var impression = MemoryFormatter.FormatMemories(targetPawn.LabelShort, topMemories);
        if (!string.IsNullOrEmpty(impression))
        {
            sb.AppendLine(impression);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Cleans faded memories whose decayed weight is negligible.
    /// Safe to call during pawn sleep or long-interval background checks.
    /// </summary>
    public void CleanFadedMemories()
    {
        if (_memories == null || _memories.Count == 0) return;
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        PawnMemoryTracker.PurgeDecayedMemories(_memories, currentTick, PawnMemoryTracker.DefaultHalfLifeDays, pawn?.LabelShort, pawn?.thingIDNumber ?? -1);
    }
}