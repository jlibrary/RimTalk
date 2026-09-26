using System;
using System.Collections.Generic;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;
using Cache = RimTalk.Data.Cache;

namespace RimTalk.Memory;

/// <summary>
/// Intercepts and transforms game events (social thoughts, heroic/tragic tales)
/// into compact episodic memories stored on pawn persona hediffs.
/// </summary>
public static class MemoryHookService
{
    /// <summary>
    /// Default enable memory toggle: false by default.
    /// </summary>
    public static bool DefaultEnableMemory => false;

    /// <summary>
    /// Processes a gained thought memory, extracting relational sentiment toward other pawns.
    /// Filters out trivial thoughts and clamps emotional weights.
    /// </summary>
    public static void TryRecordThought(Thought_Memory thought, Pawn otherPawn)
    {
        if (Current.ProgramState != ProgramState.Playing || thought?.pawn == null)
            return;

        var observer = thought.pawn;

        // 1. Resolve target pawn for interpersonal context and grief/trauma evaluation
        if (otherPawn == null && thought is Thought_MemorySocial socialThought)
        {
            otherPawn = socialThought.otherPawn;
        }

        // 2. Check for Core Trauma / Severe Grief (all-pervading mental state)
        if (IsSevereGriefOrTrauma(thought, observer, otherPawn, out string traumaNote))
        {
            var hediffTrauma = Hediff_Persona.GetOrAddNew(observer);
            hediffTrauma?.RecordCoreTrauma(otherPawn?.thingIDNumber ?? -1, otherPawn?.LabelShort ?? string.Empty, thought.def?.defName ?? "Trauma", -85f, traumaNote);
        }

        if (otherPawn == null || otherPawn == observer || !otherPawn.RaceProps.Humanlike)
            return;

        string thoughtDefName = thought.def?.defName ?? string.Empty;
        if (IsIgnoredThought(thoughtDefName) || IsCoveredByAuthoritativeTale(thoughtDefName))
            return;

        // Calculate emotional weight from social opinion offset and mood offset
        float weight = 0f;
        if (thought is Thought_MemorySocial st)
        {
            try
            {
                weight = st.OpinionOffset() * 2.5f;
            }
            catch
            {
                weight = 0f;
            }
        }

        try
        {
            weight += thought.MoodOffset() * 4f;
        }
        catch
        {
            // Ignore any third-party thought calculation errors
        }

        weight = Mathf.Clamp(weight, -100f, 100f);

        // Ignore trivial minor fluctuations (< 5 weight)
        if (Mathf.Abs(weight) < 5f)
            return;

        string thoughtLabel = thought.LabelCap;
        if (string.IsNullOrEmpty(thoughtLabel))
            thoughtLabel = thought.def?.defName ?? "interaction";

        // Natural, concise note without artificial boilerplate
        string note;
        if (thoughtDefName.Equals("HarmedMe", StringComparison.OrdinalIgnoreCase))
        {
            note = "RimTalk.Memory.AttackedBy".Translate(otherPawn.LabelShort);
        }
        else if (thoughtDefName.Equals("Insulted", StringComparison.OrdinalIgnoreCase))
        {
            note = "RimTalk.Memory.InsultedBy".Translate(otherPawn.LabelShort);
        }
        else if (thoughtDefName.Equals("Slighted", StringComparison.OrdinalIgnoreCase))
        {
            note = "RimTalk.Memory.SlightedBy".Translate(otherPawn.LabelShort);
        }
        else
        {
            note = thoughtLabel;
        }

        var hediff = Hediff_Persona.GetOrAddNew(observer);
        hediff?.RecordMemory(otherPawn.thingIDNumber, otherPawn.LabelShort, thoughtDefName, weight, note, MemoryPerspective.Target);

        // Record reciprocal action for otherPawn (the originator/actor) so they remember what they did
        RecordOriginatorMemory(otherPawn, observer, thoughtDefName);
    }

    /// <summary>
    /// Routine procedural conversations (chitchat, deep talk) are completely excluded from episodic memories.
    /// </summary>
    public static bool IsIgnoredThought(string thoughtDefName) => PawnMemoryTracker.IsExcludedRoutineChat(thoughtDefName);

    /// <summary>
    /// Records reciprocal memories for the actor/originator pawn (e.g. attacker, rescuer, recruiter).
    /// </summary>
    public static void RecordOriginatorMemory(Pawn actor, Pawn targetPawn, string thoughtDefName)
    {
        if (actor == null || targetPawn == null || actor == targetPawn || string.IsNullOrEmpty(thoughtDefName))
            return;

        if (!actor.RaceProps.Humanlike || !targetPawn.RaceProps.Humanlike)
            return;

        var actorHediff = Hediff_Persona.GetOrAddNew(actor);
        if (actorHediff == null) return;

        if (thoughtDefName.Equals("HarmedMe", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.IHarmed".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "IHarmed", -20f, note, MemoryPerspective.Actor);
        }
        else if (thoughtDefName.Equals("RescuedMe", StringComparison.OrdinalIgnoreCase) || thoughtDefName.Equals("RescuedMeByOfferingHelp", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.IRescued".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "IRescued", 25f, note, MemoryPerspective.Actor);
        }
        else if (thoughtDefName.Equals("BotchedMySurgery", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.IBotchedSurgery".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "IBotchedSurgery", -20f, note, MemoryPerspective.Actor);
        }
        else if (thoughtDefName.Equals("RecruitedMe", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.IRecruited".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "IRecruited", 20f, note, MemoryPerspective.Actor);
        }
        else if (thoughtDefName.Equals("Insulted", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.IInsulted".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "IInsulted", -15f, note, MemoryPerspective.Actor);
        }
        else if (thoughtDefName.Equals("TendedMe", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.TendedPatient".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "TendedPatient", 15f, note, MemoryPerspective.Actor);
        }
        else if (thoughtDefName.Equals("CapturedMe", StringComparison.OrdinalIgnoreCase))
        {
            string note = "RimTalk.Memory.ICaptured".Translate(targetPawn.LabelShort);
            actorHediff.RecordMemory(targetPawn.thingIDNumber, targetPawn.LabelShort, "ICaptured", 10f, note, MemoryPerspective.Actor);
        }
    }

    /// <summary>
    /// Intercepts recorded tales between two pawns (rescue, social fight, nursing, marriage).
    public const float MinTaleBaseInterest = 2.0f;

    /// <summary>
    /// Intercepts recorded tales between two pawns (rescue, social fight, nursing, marriage)
    /// or major personal deeds/crises (berserk, crafting milestones, survival).
    /// </summary>
    public static void TryRecordTale(Tale tale)
    {
        if (Current.ProgramState != ProgramState.Playing || tale == null || tale.def == null)
            return;

        // 1. Two-pawn relational tales
        if (tale is Tale_DoublePawn doublePawnTale)
        {
            var pawnA = doublePawnTale.firstPawnData?.pawn;
            var pawnB = doublePawnTale.secondPawnData?.pawn;

            if (pawnA == null || pawnB == null || pawnA == pawnB)
                return;

            string defName = tale.def.defName ?? string.Empty;

            // Handle major relational milestones with dedicated custom weights/notes
            if (defName.Equals("SocialFight", StringComparison.OrdinalIgnoreCase))
            {
                // Mutual brawl: both retain resentment/tension
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                string note = "RimTalk.Memory.SocialFight".Translate();
                hediffA?.RecordMemory(pawnB.thingIDNumber, pawnB.LabelShort, "SocialFight", -40f, note, MemoryPerspective.None);
                hediffB?.RecordMemory(pawnA.thingIDNumber, pawnA.LabelShort, "SocialFight", -40f, note, MemoryPerspective.None);
                return;
            }
            if (defName.Equals("Marriage", StringComparison.OrdinalIgnoreCase))
            {
                // Celebratory union - permanent relationship milestone
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                string note = "RimTalk.Memory.Marriage".Translate();
                hediffA?.RecordMilestone(pawnB.thingIDNumber, pawnB.LabelShort, "Marriage", 80f, note, MemoryPerspective.None);
                hediffB?.RecordMilestone(pawnA.thingIDNumber, pawnA.LabelShort, "Marriage", 80f, note, MemoryPerspective.None);
                return;
            }
            if (defName.Equals("BecameLover", StringComparison.OrdinalIgnoreCase))
            {
                // Romantic milestone
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                string note = "RimTalk.Memory.BecameLovers".Translate();
                hediffA?.RecordMilestone(pawnB.thingIDNumber, pawnB.LabelShort, "BecameLover", 65f, note, MemoryPerspective.None);
                hediffB?.RecordMilestone(pawnA.thingIDNumber, pawnA.LabelShort, "BecameLover", 65f, note, MemoryPerspective.None);
                return;
            }
            if (defName.Equals("GaveBirth", StringComparison.OrdinalIgnoreCase))
            {
                // In vanilla RimWorld, GaveBirth Tale has firstPawn = Mother, secondPawn = Baby/Child.
                // Mother (pawnA) gives birth to Child (pawnB).
                var mother = pawnA;
                var child = pawnB;

                var motherHediff = Hediff_Persona.GetOrAddNew(mother);

                // 1. Mother -> Child enduring life milestone (welcomed child into world)
                string noteMotherToChild = "RimTalk.Memory.ChildBorn".Translate(child.LabelShort);
                motherHediff?.RecordMilestone(child.thingIDNumber, child.LabelShort, "GaveBirth", 85f, noteMotherToChild, MemoryPerspective.None);

                // 2. Identify Father (if known) and record co-parenting and fatherhood milestones
                var father = child.GetFather();
                if (father != null && father.RaceProps.Humanlike && father != mother)
                {
                    var fatherHediff = Hediff_Persona.GetOrAddNew(father);

                    // Co-parenting milestone between Mother and Father
                    string noteMotherWithFather = "RimTalk.Memory.GaveBirthWith".Translate(father.LabelShort);
                    string noteFatherWithMother = "RimTalk.Memory.GaveBirthWith".Translate(mother.LabelShort);
                    motherHediff?.RecordMilestone(father.thingIDNumber, father.LabelShort, "GaveBirthWith", 85f, noteMotherWithFather, MemoryPerspective.None);
                    fatherHediff?.RecordMilestone(mother.thingIDNumber, mother.LabelShort, "GaveBirthWith", 85f, noteFatherWithMother, MemoryPerspective.None);

                    // Father -> Child milestone (welcomed child into world)
                    string noteFatherToChild = "RimTalk.Memory.ChildBorn".Translate(child.LabelShort);
                    fatherHediff?.RecordMilestone(child.thingIDNumber, child.LabelShort, "GaveBirth", 85f, noteFatherToChild, MemoryPerspective.None);
                }
                return;
            }
            if (defName.Equals("Breakup", StringComparison.OrdinalIgnoreCase))
            {
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);

                // Distinguish between legal Divorce (prior Marriage milestone or Spouse relation) vs ordinary romantic Breakup
                bool hadMarriageMilestone = (hediffA?.Memories != null && HasMemoryWithKey(hediffA.Memories, pawnB.thingIDNumber, "Marriage")) ||
                                            (hediffB?.Memories != null && HasMemoryWithKey(hediffB.Memories, pawnA.thingIDNumber, "Marriage"));

                if (hadMarriageMilestone)
                {
                    // Full Divorce: Enduring negative milestone (-70f)
                    string noteA = "RimTalk.Memory.Divorced".Translate(pawnB.LabelShort);
                    string noteB = "RimTalk.Memory.Divorced".Translate(pawnA.LabelShort);
                    hediffA?.RecordMilestone(pawnB.thingIDNumber, pawnB.LabelShort, "Divorced", -70f, noteA, MemoryPerspective.None);
                    hediffB?.RecordMilestone(pawnA.thingIDNumber, pawnA.LabelShort, "Divorced", -70f, noteB, MemoryPerspective.None);
                }
                else
                {
                    // Casual Romantic Breakup: Decaying episodic memory (-55f, dynamic half-life 8-14 days)
                    string noteA = "RimTalk.Memory.BrokeUpWith".Translate(pawnB.LabelShort);
                    string noteB = "RimTalk.Memory.BrokeUpWith".Translate(pawnA.LabelShort);
                    hediffA?.RecordMemory(pawnB.thingIDNumber, pawnB.LabelShort, "Breakup", -55f, noteA, MemoryPerspective.Target);
                    hediffB?.RecordMemory(pawnA.thingIDNumber, pawnA.LabelShort, "Breakup", -55f, noteB, MemoryPerspective.Target);
                }

                // Invalidate obsolete romantic milestones (Marriage, BecameLover) between these two pawns upon breakup
                RemoveMilestonesWithKeys(hediffA?.Memories, pawnB.thingIDNumber, pawnA.LabelShort, pawnA.thingIDNumber, "Marriage", "BecameLover");
                RemoveMilestonesWithKeys(hediffB?.Memories, pawnA.thingIDNumber, pawnB.LabelShort, pawnB.thingIDNumber, "Marriage", "BecameLover");
                return;
            }
            if (defName.Equals("SavedColonistLife", StringComparison.OrdinalIgnoreCase))
            {
                // PawnA: Victim whose life was saved, PawnB: Rescuer/Savior (episodic memory, not permanent milestone)
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                string noteA = "RimTalk.Memory.SavedLife".Translate(pawnB.LabelShort);
                hediffA?.RecordMemory(pawnB.thingIDNumber, pawnB.LabelShort, "SavedLife", 90f, noteA, MemoryPerspective.Target);
                return;
            }
            if (defName.Equals("DidSurgery", StringComparison.OrdinalIgnoreCase))
            {
                // PawnA (Surgeon) performed surgery on PawnB (Patient)
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                hediffB?.RecordMemory(pawnA.thingIDNumber, pawnA.LabelShort, "DidSurgery", 25f, "RimTalk.Memory.DidSurgery".Translate(), MemoryPerspective.Target);
                return;
            }
            if (defName.Equals("Captured", StringComparison.OrdinalIgnoreCase))
            {
                // PawnA captured PawnB
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                hediffB?.RecordMemory(pawnA.thingIDNumber, pawnA.LabelShort, "CapturedMe", -30f, "RimTalk.Memory.CapturedMe".Translate(), MemoryPerspective.Target);
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                hediffA?.RecordMemory(pawnB.thingIDNumber, pawnB.LabelShort, "ICaptured", 10f, "RimTalk.Memory.ICaptured".Translate(), MemoryPerspective.Actor);
                return;
            }
            if (defName.Equals("ExecutedPrisoner", StringComparison.OrdinalIgnoreCase))
            {
                // PawnA: Prisoner, PawnB: Warden/Executioner
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                string note = "RimTalk.Memory.IExecuted".Translate(pawnA.LabelShort);
                hediffB?.RecordMemory(pawnA.thingIDNumber, pawnA.LabelShort, "IExecuted", -20f, note, MemoryPerspective.Actor);
                return;
            }
            if (defName.Equals("Rescued", StringComparison.OrdinalIgnoreCase))
            {
                // PawnA: Rescued patient, PawnB: Rescuer
                var hediffA = Hediff_Persona.GetOrAddNew(pawnA);
                var hediffB = Hediff_Persona.GetOrAddNew(pawnB);
                string noteA = "RimTalk.Memory.RescuedBy".Translate(pawnB.LabelShort);
                string noteB = "RimTalk.Memory.IRescued".Translate(pawnA.LabelShort);
                hediffA?.RecordMemory(pawnB.thingIDNumber, pawnB.LabelShort, "RescuedMe", 30f, noteA, MemoryPerspective.Target);
                hediffB?.RecordMemory(pawnA.thingIDNumber, pawnA.LabelShort, "IRescued", 20f, noteB, MemoryPerspective.Actor);
                return;
            }

            // General 2-pawn tale fallback (filters out routine chores with baseInterest < 2.0f)
            if (tale.def.baseInterest >= MinTaleBaseInterest && pawnA.RaceProps.Humanlike && pawnB.RaceProps.Humanlike)
            {
                RecordGenericDoublePawnTale(pawnA, pawnB, tale);
            }
            return;
        }

        // 2. Single-pawn personal tales (deeds, crisis, mastery)
        var dominantPawn = tale.DominantPawn;
        if (dominantPawn != null && dominantPawn.RaceProps.Humanlike)
        {
            string singleDefName = tale.def.defName ?? string.Empty;
            if (tale.def.baseInterest >= MinTaleBaseInterest || singleDefName.Equals("LandedInPod", StringComparison.OrdinalIgnoreCase))
            {
                RecordGenericSinglePawnTale(dominantPawn, tale);
            }
        }
    }

    private struct DoubleTaleConfig
    {
        public readonly float WeightActor;
        public readonly float WeightTarget;

        public DoubleTaleConfig(float weightActor, float weightTarget)
        {
            WeightActor = weightActor;
            WeightTarget = weightTarget;
        }
    }

    private static readonly Dictionary<string, DoubleTaleConfig> KnownDoubleTaleConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        { "KidnappedColonist", new DoubleTaleConfig(10f, -60f) },
        { "SoldPrisoner", new DoubleTaleConfig(10f, -50f) },
        { "DownedBy", new DoubleTaleConfig(15f, -40f) },
        { "WoundedBy", new DoubleTaleConfig(15f, -30f) },
        { "KilledBy", new DoubleTaleConfig(20f, -80f) }
    };

    private static readonly Dictionary<string, float> KnownSingleTaleWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Berserk", -30f },
        { "GaveUp", -30f },
        { "Exhausted", -25f },
        { "Heatstroke", -30f },
        { "Hypothermia", -30f },
        { "Illness", -25f },
        { "Toxicity", -30f },
        { "WasOnFire", -35f },
        { "Downed", -30f },
        { "MasterSkill", 35f },
        { "CraftedArt", 35f },
        { "CaravanAssault", 35f },
        { "CaravanAmbushDefeated", 35f },
        { "FinishedResearch", 35f },
        { "LandedInPod", 30f }
    };

    private static void RecordGenericDoublePawnTale(Pawn pawnA, Pawn pawnB, Tale tale)
    {
        string defName = tale.def?.defName ?? string.Empty;
        if (!KnownDoubleTaleConfigs.TryGetValue(defName, out var config))
        {
            if (Prefs.DevMode)
            {
                Log.Message($"[RimTalk] Dropped unmapped Tale_DoublePawn: {defName}");
            }
            return;
        }

        string label = tale.def?.LabelCap.Resolve();
        if (string.IsNullOrEmpty(label))
            label = defName;

        bool isPawnAFirst = true;
        if (string.Equals(tale.def.firstPawnSymbol, "VICTIM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tale.def.secondPawnSymbol, "ATTACKER", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tale.def.secondPawnSymbol, "KILLER", StringComparison.OrdinalIgnoreCase))
        {
            isPawnAFirst = false;
        }

        Pawn actor = isPawnAFirst ? pawnA : pawnB;
        Pawn target = isPawnAFirst ? pawnB : pawnA;

        var hediffActor = Hediff_Persona.GetOrAddNew(actor);
        hediffActor?.RecordMemory(target.thingIDNumber, target.LabelShort, defName, config.WeightActor, label, MemoryPerspective.Actor);

        var hediffTarget = Hediff_Persona.GetOrAddNew(target);
        hediffTarget?.RecordMemory(actor.thingIDNumber, actor.LabelShort, defName, config.WeightTarget, label, MemoryPerspective.Target);
    }

    private static void RecordGenericSinglePawnTale(Pawn pawn, Tale tale)
    {
        string defName = tale.def?.defName ?? string.Empty;
        if (!KnownSingleTaleWeights.TryGetValue(defName, out float weight))
        {
            if (tale.def == null || tale.def.baseInterest < 3.0f)
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[RimTalk] Dropped unmapped Tale_SinglePawn: {defName}");
                }
                return;
            }
            weight = 20f;
        }

        string label = tale.def?.LabelCap.Resolve();
        if (string.IsNullOrEmpty(label))
            label = defName;

        var hediff = Hediff_Persona.GetOrAddNew(pawn);
        hediff?.RecordMemory(-1, string.Empty, defName, weight, label, MemoryPerspective.None);
    }

    /// <summary>
    /// Intercepts pawn death to record kill deeds, homicide trauma, or prisoner executions.
    /// </summary>
    public static void TryRecordKill(Pawn victim, DamageInfo? dinfo)
    {
        if (Current.ProgramState != ProgramState.Playing || victim == null)
            return;

        Pawn killer = dinfo?.Instigator as Pawn;
        if (killer == null || killer == victim)
            return;

        if (!killer.RaceProps.Humanlike || !victim.RaceProps.Humanlike)
            return;

        var killerHediff = Hediff_Persona.GetOrAddNew(killer);
        if (killerHediff == null)
            return;

        bool isCallous = killer.story?.traits?.HasTrait(TraitDefOf.Bloodlust) == true || 
                         killer.story?.traits?.HasTrait(TraitDefOf.Psychopath) == true;

        bool isExecution = dinfo.HasValue && dinfo.Value.Def?.defName != null &&
                           dinfo.Value.Def.defName.IndexOf("Execution", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isFellowColonist = victim.IsColonist || (victim.Faction != null && victim.Faction == killer.Faction);

        if (isExecution)
        {
            string note = "RimTalk.Memory.IExecuted".Translate(victim.LabelShort);
            killerHediff.RecordMemory(victim.thingIDNumber, victim.LabelShort, "IExecuted", isCallous ? 10f : -30f, note, MemoryPerspective.Actor);

            if (killer.IsColonist)
            {
                string traumaNote = "RimTalk.Memory.ExecutedPrisoner".Translate(victim.LabelShort);
                killerHediff.RecordCoreTrauma(victim.thingIDNumber, victim.LabelShort, "ExecutedPrisoner", isCallous ? 15f : -30f, traumaNote);
            }
        }
        else if (isFellowColonist)
        {
            // Killer killed a fellow colonist/faction member
            string note = "RimTalk.Memory.IKilled".Translate(victim.LabelShort);
            killerHediff.RecordMemory(victim.thingIDNumber, victim.LabelShort, "IKilled", isCallous ? 20f : -80f, note, MemoryPerspective.Actor);

            if (killer.IsColonist)
            {
                string traumaNote = isCallous
                    ? (string)"RimTalk.Memory.KilledColonistWithoutRemorse".Translate(victim.LabelShort)
                    : (string)"RimTalk.Memory.KilledColonistGuilt".Translate(victim.LabelShort);
                killerHediff.RecordCoreTrauma(victim.thingIDNumber, victim.LabelShort, "KilledColonist", isCallous ? 35f : -75f, traumaNote);

                // Record murder memory on fellow colonists on the map
                var map = killer.Map ?? victim.Map;
                var colonists = map?.mapPawns?.FreeColonists;
                if (colonists != null)
                {
                    string witnessNote = "RimTalk.Memory.MurderedFellowColonist".Translate(victim.LabelShort);
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        var colonist = colonists[i];
                        if (colonist == killer || colonist == victim || !colonist.RaceProps.Humanlike)
                            continue;

                        var otherHediff = Hediff_Persona.GetOrAddNew(colonist);
                        if (IsBelovedKinOrPartner(colonist, victim))
                        {
                            string kinNote = "RimTalk.Memory.MurderedMyKin".Translate(victim.LabelShort);
                            otherHediff?.RecordMilestone(killer.thingIDNumber, killer.LabelShort, "MurderedKin", -95f, kinNote, MemoryPerspective.Target);
                        }
                        else
                        {
                            otherHediff?.RecordMemory(killer.thingIDNumber, killer.LabelShort, "MurderedColonist", -70f, witnessNote, MemoryPerspective.Target);
                        }
                    }
                }

                // Trigger immediate reaction dialogue
                string murderPrompt = isCallous
                    ? (string)"RimTalk.Memory.WitnessMurderCallous".Translate(killer.LabelShort, victim.LabelShort)
                    : (string)"RimTalk.Memory.WitnessMurderHorrified".Translate(killer.LabelShort, victim.LabelShort);
                Cache.Get(killer)?.AddTalkRequest(murderPrompt, talkType: TalkType.Urgent);
            }
        }
        else if (victim.HostileTo(killer))
        {
            // Combat kill of an enemy
            string note = "RimTalk.Memory.KilledInBattle".Translate(victim.LabelShort);
            killerHediff.RecordMemory(victim.thingIDNumber, victim.LabelShort, "IKilledEnemy", 15f, note, MemoryPerspective.Actor);
        }
    }

    /// <summary>
    /// Intercepts medical treatment to record relational care and gratitude between doctor and patient.
    /// </summary>
    public static void TryRecordTend(Pawn doctor, Pawn patient)
    {
        if (Current.ProgramState != ProgramState.Playing || doctor == null || patient == null || doctor == patient)
            return;

        if (!doctor.RaceProps.Humanlike || !patient.RaceProps.Humanlike)
            return;

        // Patient remembers doctor treating their wounds
        var hediffPatient = Hediff_Persona.GetOrAddNew(patient);
        hediffPatient?.RecordMemory(doctor.thingIDNumber, doctor.LabelShort, "TendedMe", 30f, "RimTalk.Memory.TendedMe".Translate(), MemoryPerspective.Target);

        // Doctor remembers caring for the patient
        var hediffDoctor = Hediff_Persona.GetOrAddNew(doctor);
        hediffDoctor?.RecordMemory(patient.thingIDNumber, patient.LabelShort, "TendedPatient", 15f, "RimTalk.Memory.TendedPatient".Translate(), MemoryPerspective.Actor);
    }

    /// <summary>
    /// Synchronizes active social thoughts bidirectionally between observer and targetPawn into episodic memories.
    /// 1) Observer -> Target: what Target did to Observer (e.g. harmed me, rescued me, insulted me).
    /// 2) Target -> Observer: what Observer did to Target (e.g. I harmed them, I rescued them, I botched surgery on them).
    /// Leverages vanilla's live relationship thoughts with zero-allocation debouncing through AddOrUpdateMemory.
    /// </summary>
    public static void SyncActiveSocialThoughts(Pawn observer, Pawn targetPawn)
    {
        if (Current.ProgramState != ProgramState.Playing || observer == null || targetPawn == null || observer == targetPawn)
            return;

        if (!observer.RaceProps.Humanlike || !targetPawn.RaceProps.Humanlike)
            return;

        var observerHediff = Hediff_Persona.GetOrAddNew(observer);
        if (observerHediff == null)
            return;

        var existingMemories = observerHediff.Memories;

        // 1. Direct Perspective: Observer's thoughts toward targetPawn (Observer as recipient/victim)
        var myMemories = observer.needs?.mood?.thoughts?.memories?.Memories;
        if (myMemories != null && myMemories.Count > 0)
        {
            for (int i = 0; i < myMemories.Count; i++)
            {
                var thought = myMemories[i];
                if (thought is Thought_MemorySocial socialThought && socialThought.otherPawn == targetPawn)
                {
                    string defName = thought.def?.defName ?? string.Empty;
                    if (string.IsNullOrEmpty(defName) || IsIgnoredThought(defName)) continue;

                    // Skip if already tracked in episodic memories to prevent runaway debounce loops
                    if (!HasMemoryWithKey(existingMemories, targetPawn.thingIDNumber, defName))
                    {
                        TryRecordThought(socialThought, targetPawn);
                    }
                }
            }
        }

        // 2. Inverse Perspective: Target's thoughts where observer was the originator/actor
        // Allows the actor (e.g. attacker, rescuer) to remember their own action toward the target!
        var targetMemories = targetPawn.needs?.mood?.thoughts?.memories?.Memories;
        if (targetMemories != null && targetMemories.Count > 0)
        {
            for (int i = 0; i < targetMemories.Count; i++)
            {
                var thought = targetMemories[i];
                if (thought is Thought_MemorySocial socialThought && socialThought.otherPawn == observer)
                {
                    string defName = thought.def?.defName ?? string.Empty;
                    if (string.IsNullOrEmpty(defName)) continue;

                    string origKey = GetOriginatorEventKey(defName);
                    if (!string.IsNullOrEmpty(origKey) && !HasMemoryWithKey(existingMemories, targetPawn.thingIDNumber, origKey))
                    {
                        RecordOriginatorMemory(observer, targetPawn, defName);
                    }
                }
            }
        }

        // 3. Invalidate broken relational milestones upon divorce or romantic breakup
        if (existingMemories != null && existingMemories.Count > 0)
        {
            for (int i = existingMemories.Count - 1; i >= 0; i--)
            {
                var m = existingMemories[i];
                if (m == null || !m.IsMilestone || m.TargetPawnId != targetPawn.thingIDNumber)
                    continue;

                if (string.Equals(m.EventKey, "Marriage", StringComparison.OrdinalIgnoreCase))
                {
                    bool isStillSpouse = observer.relations != null && observer.relations.DirectRelationExists(PawnRelationDefOf.Spouse, targetPawn);
                    bool isDeceased = targetPawn.Dead;
                    if (!isStillSpouse && !isDeceased)
                    {
                        existingMemories.RemoveAt(i);
                        MemoryHistory.Add(new MemoryLogEntry
                        {
                            SourcePawnName = observer.LabelShort,
                            SourcePawnId = observer.thingIDNumber,
                            TargetPawnName = targetPawn.LabelShort,
                            TargetPawnId = targetPawn.thingIDNumber,
                            ChangeType = MemoryChangeType.Evicted,
                            EventKey = m.EventKey,
                            Note = m.Note,
                            OldWeight = m.BaseWeight,
                            NewWeight = 0f,
                            Tick = Find.TickManager?.TicksGame ?? 0,
                            Details = "Marriage milestone invalidated because spouse relation ceased"
                        });
                    }
                }
                else if (string.Equals(m.EventKey, "BecameLover", StringComparison.OrdinalIgnoreCase))
                {
                    bool isRomantic = observer.relations != null &&
                                      (observer.relations.DirectRelationExists(PawnRelationDefOf.Lover, targetPawn) ||
                                       observer.relations.DirectRelationExists(PawnRelationDefOf.Fiance, targetPawn) ||
                                       observer.relations.DirectRelationExists(PawnRelationDefOf.Spouse, targetPawn));
                    bool isDeceased = targetPawn.Dead;
                    if (!isRomantic && !isDeceased)
                    {
                        existingMemories.RemoveAt(i);
                        MemoryHistory.Add(new MemoryLogEntry
                        {
                            SourcePawnName = observer.LabelShort,
                            SourcePawnId = observer.thingIDNumber,
                            TargetPawnName = targetPawn.LabelShort,
                            TargetPawnId = targetPawn.thingIDNumber,
                            ChangeType = MemoryChangeType.Evicted,
                            EventKey = m.EventKey,
                            Note = m.Note,
                            OldWeight = m.BaseWeight,
                            NewWeight = 0f,
                            Tick = Find.TickManager?.TicksGame ?? 0,
                            Details = "BecameLover milestone invalidated because romantic relation ceased"
                        });
                    }
                }
            }
        }
    }

    private static string GetOriginatorEventKey(string thoughtDefName)
    {
        if (string.IsNullOrEmpty(thoughtDefName)) return string.Empty;
        if (thoughtDefName.Equals("HarmedMe", StringComparison.OrdinalIgnoreCase)) return "IHarmed";
        if (thoughtDefName.Equals("RescuedMe", StringComparison.OrdinalIgnoreCase) || thoughtDefName.Equals("RescuedMeByOfferingHelp", StringComparison.OrdinalIgnoreCase)) return "IRescued";
        if (thoughtDefName.Equals("BotchedMySurgery", StringComparison.OrdinalIgnoreCase)) return "IBotchedSurgery";
        if (thoughtDefName.Equals("RecruitedMe", StringComparison.OrdinalIgnoreCase)) return "IRecruited";
        if (thoughtDefName.Equals("Insulted", StringComparison.OrdinalIgnoreCase)) return "IInsulted";
        if (thoughtDefName.Equals("TendedMe", StringComparison.OrdinalIgnoreCase)) return "TendedPatient";
        if (thoughtDefName.Equals("CapturedMe", StringComparison.OrdinalIgnoreCase)) return "ICaptured";
        return string.Empty;
    }

    private static bool HasMemoryWithKey(List<MemoryEntry> memories, int targetPawnId, string eventKey)
    {
        if (memories == null || string.IsNullOrEmpty(eventKey)) return false;
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            if (m != null && m.TargetPawnId == targetPawnId && string.Equals(m.EventKey, eventKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsSevereGriefOrTrauma(Thought_Memory thought, Pawn observer, Pawn otherPawn, out string traumaNote)
    {
        traumaNote = string.Empty;
        if (thought?.def == null || observer == null) return false;

        float moodOffset;
        try { moodOffset = thought.MoodOffset(); } catch { return false; }
        if (moodOffset > -10f) return false;

        string defName = thought.def.defName ?? string.Empty;

        // Exclude deaths or harm of prisoners, guests, rivals, or enemies
        if (defName.IndexOf("Prisoner", StringComparison.OrdinalIgnoreCase) >= 0 ||
            defName.IndexOf("Guest", StringComparison.OrdinalIgnoreCase) >= 0 ||
            defName.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0 ||
            defName.IndexOf("Rival", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        if (otherPawn != null && (otherPawn.HostileTo(observer) || otherPawn.IsPrisoner))
            return false;

        bool isRecognizedTrauma = false;

        // 1. Severe bodily trauma to observer
        if (defName.IndexOf("MyOrganHarvested", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            isRecognizedTrauma = true;
        }
        // 2. Severe personal grief: death of family, partner, bonded animal, or close colonist
        else if (defName.IndexOf("Died", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 defName.IndexOf("Lost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 defName.IndexOf("Killed", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (defName.StartsWith("My", StringComparison.OrdinalIgnoreCase) ||
                defName.IndexOf("Bonded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.IndexOf("ColonistLost", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isRecognizedTrauma = true;
            }
            else if (otherPawn != null)
            {
                if (otherPawn.IsColonist)
                {
                    isRecognizedTrauma = true;
                }
                else if (observer.relations != null)
                {
                    var directRelations = observer.relations.DirectRelations;
                    if (directRelations != null)
                    {
                        for (int i = 0; i < directRelations.Count; i++)
                        {
                            if (directRelations[i]?.otherPawn == otherPawn)
                            {
                                isRecognizedTrauma = true;
                                break;
                            }
                        }
                    }

                    if (!isRecognizedTrauma && observer.relations.OpinionOf(otherPawn) >= 20)
                    {
                        isRecognizedTrauma = true;
                    }
                }
            }
        }

        if (!isRecognizedTrauma) return false;

        string label = thought.LabelCap;
        if (string.IsNullOrEmpty(label)) label = defName;

        if (otherPawn != null && !string.IsNullOrEmpty(otherPawn.LabelShort))
            traumaNote = "RimTalk.Memory.TraumaWithTarget".Translate(label, otherPawn.LabelShort);
        else
            traumaNote = "RimTalk.Memory.TraumaWithoutTarget".Translate(label);

        return true;
    }

    private static bool IsCoveredByAuthoritativeTale(string thoughtDefName)
    {
        if (string.IsNullOrEmpty(thoughtDefName)) return false;

        return thoughtDefName.Equals("HadSocialFight", StringComparison.OrdinalIgnoreCase) ||
               thoughtDefName.Equals("GotMarried", StringComparison.OrdinalIgnoreCase) ||
               thoughtDefName.Equals("HoneymoonPhase", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBelovedKinOrPartner(Pawn observer, Pawn target)
    {
        if (observer?.relations == null || target == null) return false;
        return observer.relations.DirectRelationExists(PawnRelationDefOf.Spouse, target) ||
               observer.relations.DirectRelationExists(PawnRelationDefOf.Lover, target) ||
               observer.relations.DirectRelationExists(PawnRelationDefOf.Fiance, target) ||
               observer.relations.DirectRelationExists(PawnRelationDefOf.Child, target) ||
               observer.relations.DirectRelationExists(PawnRelationDefOf.Parent, target) ||
               observer.relations.DirectRelationExists(PawnRelationDefOf.Sibling, target);
    }

    private static void RemoveMilestonesWithKeys(List<MemoryEntry> memories, int targetPawnId, string sourcePawnName, int sourcePawnId, params string[] eventKeys)
    {
        if (memories == null || memories.Count == 0 || targetPawnId < 0 || eventKeys == null) return;
        int currentTick = Find.TickManager?.TicksGame ?? 0;
        for (int i = memories.Count - 1; i >= 0; i--)
        {
            var m = memories[i];
            if (m == null || !m.IsMilestone || m.TargetPawnId != targetPawnId) continue;
            for (int k = 0; k < eventKeys.Length; k++)
            {
                if (string.Equals(m.EventKey, eventKeys[k], StringComparison.OrdinalIgnoreCase))
                {
                    memories.RemoveAt(i);
                    MemoryHistory.Add(new MemoryLogEntry
                    {
                        SourcePawnName = sourcePawnName ?? string.Empty,
                        SourcePawnId = sourcePawnId,
                        TargetPawnName = m.TargetPawnName,
                        TargetPawnId = m.TargetPawnId,
                        ChangeType = MemoryChangeType.Evicted,
                        EventKey = m.EventKey,
                        Note = m.Note,
                        OldWeight = m.BaseWeight,
                        NewWeight = 0f,
                        Tick = currentTick,
                        Details = $"Milestone removed due to relationship transition (breakup/divorce)"
                    });
                    break;
                }
            }
        }
    }
}
