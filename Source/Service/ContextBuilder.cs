using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimTalk.API;
using RimTalk.Data;
using RimTalk.Source.Data;
using RimTalk.Util;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Service;

public static class ContextBuilder
{
    private static readonly MethodInfo VisibleHediffsMethod =
        AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");

    public static string GetRaceContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRace || !ModsConfig.BiotechActive || pawn.genes?.Xenotype == null)
            return null;
        return $"Race: {pawn.genes.XenotypeLabel}";
    }

    public static string GetNotableGenesContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeNotableGenes || !ModsConfig.BiotechActive ||
            pawn.genes?.GenesListForReading == null)
            return null;

        var notableGenes = pawn.genes.GenesListForReading
            .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
            .Select(g => g.def.LabelCap);

        // For Short level, limit to top 3 most impactful genes
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            notableGenes = pawn.genes.GenesListForReading
                .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                .OrderByDescending(g => Mathf.Abs(g.def.biostatMet) + g.def.biostatCpx)
                .Take(3)
                .Select(g => g.def.LabelCap);
        }

        if (notableGenes.Any())
            return $"Notable Genes: {string.Join(", ", notableGenes)}";
        return null;
    }

    public static string GetAllGenesContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeNotableGenes || !ModsConfig.BiotechActive ||
            pawn.genes?.GenesListForReading == null)
            return null;

        var genes = pawn.genes.GenesListForReading
            .Select(g => g.def?.LabelCap.ToString())
            .Where(label => !string.IsNullOrEmpty(label));

        if (genes.Any())
            return $"Genes: {string.Join(", ", genes)}";
        return null;
    }

    public static string GetIdeologyContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeIdeology || !ModsConfig.IdeologyActive || pawn.ideo?.Ideo == null)
            return null;

        var sb = new StringBuilder();
        var ideo = pawn.ideo.Ideo;

        // For Short level, skip ideology name and only show top 3 memes
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            var memes = ideo.memes?
                .Where(m => m != null)
                .Take(3)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
                return $"Memes: {string.Join(", ", memes)}";
        }
        else
        {
            sb.Append($"Ideology: {ideo.name}");

            var memes = ideo.memes?
                .Where(m => m != null)
                .Select(m => m.LabelCap.Resolve())
                .Where(label => !string.IsNullOrEmpty(label));

            if (memes?.Any() == true)
                sb.Append($"\nMemes: {string.Join(", ", memes)}");

            return sb.ToString();
        }

        return null;
    }

    public static string GetBackstoryContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeBackstory)
            return null;

        var sb = new StringBuilder();

        // For Short level, only include childhood title
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            if (pawn.story?.Adulthood != null)
                return $"Background: {pawn.story.Adulthood.TitleCapFor(pawn.gender)}";
        }
        else
        {
            if (pawn.story?.Childhood != null)
                sb.Append(ContextHelper.FormatBackstory("Childhood", pawn.story.Childhood, pawn, infoLevel));

            if (pawn.story?.Adulthood != null)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append(ContextHelper.FormatBackstory("Adulthood", pawn.story.Adulthood, pawn, infoLevel));
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    public static string GetTraitsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeTraits)
            return null;

        var traits = new List<string>();
        foreach (var trait in pawn.story?.traits?.TraitsSorted ?? Enumerable.Empty<Trait>())
        {
            var degreeData = GenCollection.FirstOrDefault(trait.def.degreeDatas, d => d.degree == trait.Degree);
            if (degreeData != null)
            {
                var traitText = infoLevel == PromptService.InfoLevel.Full
                    ? $"{degreeData.label}:{CommonUtil.Sanitize(degreeData.description, pawn)}"
                    : degreeData.label;
                traits.Add(traitText);
            }
        }

        // For Short level, limit to top 3 traits
        if (infoLevel == PromptService.InfoLevel.Short && traits.Count > 3)
            traits = traits.Take(3).ToList();

        if (traits.Any())
        {
            var separator = infoLevel == PromptService.InfoLevel.Full ? "\n" : ", ";
            return $"Traits: {string.Join(separator, traits)}";
        }

        return null;
    }

    public static string GetSkillsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeSkills)
            return null;

        bool showPassion = infoLevel != PromptService.InfoLevel.Short;
        var records = pawn.skills?.skills;
        if (records == null || records.Count == 0)
            return null;

        var activeSkills = records
            .Where(s => !s.TotallyDisabled && (s.Level > 0 || s.passion != Passion.None))
            .ToList();

        var skillsToGroup = activeSkills.Count > 0 ? activeSkills : records;

        // Group by proficiency tier so a small model reads "who's good at what" at a glance
        // instead of parsing a dozen individual "skill: level" pairs.
        var groups = skillsToGroup
            .GroupBy(s => s.LevelDescriptor)
            .OrderByDescending(g => g.Max(s => s.Level))
            .Select(g =>
            {
                var names = g.Select(s =>
                {
                    // Delegates to vanilla's own SkillUI.GetLabel so a mod's custom passion tier (which can only
                    // reach players by Harmony-patching that same method) is picked up instead of dropped as null.
                    string passionLabel = showPassion && s.passion != Passion.None ? s.passion.GetLabel() : null;
                    return string.IsNullOrEmpty(passionLabel) ? s.def.label : $"{s.def.label} ({passionLabel})";
                });
                return $"[{g.Key}] {string.Join(", ", names)}";
            });

        return $"Skills: {string.Join(" | ", groups)}";
    }

    public static string GetHealthContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeHealth)
            return null;

        var hediffs = (IEnumerable<Hediff>)VisibleHediffsMethod.Invoke(null, [pawn, false]);
        if (hediffs == null) return null;

        // For Short level, only show top 3 most recent/severe hediffs
        if (infoLevel == PromptService.InfoLevel.Short)
        {
            hediffs = hediffs
                .OrderByDescending(h => h.Visible ? 1 : 0)
                .ThenByDescending(h => h.Severity)
                .ThenByDescending(h => h.ageTicks)
                .Take(3);
        }

        var items = new List<string>();

        // Check active bleeding
        if (pawn.health?.hediffSet != null && pawn.health.hediffSet.BleedRateTotal > 0.01f)
        {
            float rate = pawn.health.hediffSet.BleedRateTotal;
            float currentBloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
            float remainingToFatal = 1f - currentBloodLoss;
            if (remainingToFatal > 0f)
            {
                float hoursToDeath = (remainingToFatal / rate) * 24f;
                if (hoursToDeath < 24f)
                {
                    string hoursStr = hoursToDeath < 1f
                        ? $"{(int)(hoursToDeath * 60f)} minutes"
                        : $"{hoursToDeath:0.#} hours";
                    items.Add($"Bleeding ({Describer.Bleeding(rate)}, death in {hoursStr})");
                }
                else
                {
                    items.Add($"Bleeding ({Describer.Bleeding(rate)})");
                }
            }
        }

        var hediffSummary = hediffs
            .GroupBy(h => h.Label)
            .Select(g =>
            {
                var parts = g
                    .Select(h => h.Part?.Label)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .GroupBy(p => p)
                    .Select(pg => pg.Count() > 1 ? $"{pg.Key} x{pg.Count()}" : pg.Key)
                    .ToList();

                string partStr = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
                return g.Count() > 1 ? $"{g.Key} x{g.Count()}{partStr}" : $"{g.Key}{partStr}";
            });

        items.AddRange(hediffSummary);

        if (items.Count > 0)
            return $"Health: {string.Join(", ", items)}";
        return null;
    }

    public static string GetMoodContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeMood)
            return null;

        var m = pawn.needs?.mood;
        if (m?.MoodString != null)
        {
            string mood = pawn.Downed && !pawn.IsBaby()
                ? "Critical: Downed (in pain/distress)"
                : pawn.InMentalState
                    ? $"Mood: {pawn.MentalState?.InspectLine} (in mental break)"
                    : infoLevel == PromptService.InfoLevel.Full
                        ? $"Mood: {m.MoodString} ({(int)(m.CurLevelPercentage * 100)}%)"
                        : $"Mood: {m.MoodString}";
            return mood;
        }

        return null;
    }

    public static string GetThoughtsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeThoughts)
            return null;

        var allThoughts = ContextHelper.GetThoughts(pawn);

        // For Short level, only include latest 3 thoughts
        var thoughts = infoLevel == PromptService.InfoLevel.Short
            ? allThoughts.Keys.Take(3).Select(t => CommonUtil.Sanitize(t.LabelCap ?? t.def?.defName ?? "UnknownThought"))
            : allThoughts.Keys.Select(t => CommonUtil.Sanitize(t.LabelCap ?? t.def?.defName ?? "UnknownThought"));

        if (thoughts.Any())
            return $"Memory: {string.Join(", ", thoughts)}";
        return null;
    }

    public static string GetAllThoughtsContext(Pawn pawn)
    {
        if (pawn?.needs?.mood?.thoughts == null)
            return null;

        var allThoughts = ContextHelper.GetThoughts(pawn);
        if (allThoughts.Count == 0)
            return null;

        var thoughts = allThoughts
            .OrderBy(kvp => kvp.Key.LabelCap)
            .Select(kvp =>
                $"{CommonUtil.Sanitize(kvp.Key.LabelCap ?? kvp.Key.def?.defName ?? "UnknownThought")}({kvp.Value.ToStringWithSign()})");

        return $"Thoughts: {string.Join(", ", thoughts)}";
    }

    public static string GetPrisonerSlaveContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludePrisonerSlaveStatus || (!pawn.IsSlave && !pawn.IsPrisoner))
            return null;

        return pawn.GetPrisonerSlaveStatus(infoLevel);
    }

    public static string GetRelationsContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeRelations)
            return null;

        return RelationsService.GetRelationsString(pawn);
    }

    public static string GetEquipmentContext(Pawn pawn, PromptService.InfoLevel infoLevel)
    {
        var contextSettings = Settings.Get().Context;
        if (!contextSettings.IncludeEquipment)
            return null;

        var equipment = new List<string>();
        if (pawn.equipment?.Primary != null)
            equipment.Add($"[Weapon] {DescribeThingLabel(pawn.equipment.Primary)}");

        var apparelLabels = pawn.apparel?.WornApparel?.Select(DescribeThingLabel);
        var enumerable = apparelLabels as string[] ?? apparelLabels?.ToArray() ?? [];
        if (enumerable.Any())
            equipment.Add($"[Apparel] {string.Join(", ", enumerable)}");

        if (equipment.Any())
            return $"Equipment: {string.Join(" | ", equipment)}";
        return null;
    }

    // GetCustomLabelNoCount(includeHp: false) is the virtual, comp-aware path that drops GenLabel.LabelExtras'
    // raw "(NN%)" hit-point fraction while preserving comp label overrides (art titles, quality, etc.).
    private static string DescribeThingLabel(Thing thing)
    {
        string label = thing.GetCustomLabelNoCount(includeHp: false).CapitalizeFirst(thing.def);

        if (thing.def.useHitPoints && thing.def.stackLimit == 1 && thing.HitPoints < thing.MaxHitPoints)
        {
            float pct = (float)thing.HitPoints / thing.MaxHitPoints * 100f;
            string condition = Describer.Condition(pct);

            // Fold into an existing quality parenthetical instead of appending a second "(...)".
            label = label.EndsWith(")")
                ? $"{label[..^1]}, {condition})"
                : $"{label} ({condition})";
        }

        return label;
    }

    public static void BuildDialogueType(StringBuilder sb, TalkRequest talkRequest, List<Pawn> pawns, string shortName, Pawn mainPawn)
    {
        BuildDialogueType(sb, talkRequest, pawns, shortName, mainPawn, out _, out _);
    }


    public static void BuildDialogueType(StringBuilder sb, TalkRequest talkRequest, List<Pawn> pawns, string shortName, Pawn mainPawn, out string intent, out string topic)
    {
        var intentSb = new StringBuilder();
        var topicSb = new StringBuilder();

        if (talkRequest.IsAnnouncement)
        {
            var speaker = talkRequest.Recipient != null && talkRequest.Recipient.IsPlayer() 
                ? talkRequest.Recipient 
                : talkRequest.Initiator;
            var speakerName = PromptService.GetUniqueName(speaker, pawns);
            var listeners = pawns.Where(p => p != speaker && !p.IsPlayer()).ToList();
            var listenerNames = string.Join(", ", listeners.Select(p => PromptService.GetUniqueName(p, pawns)));

            topicSb.Append($"{speakerName} announced to everyone nearby: '{talkRequest.Prompt}'. ");
            intentSb.Append(listeners.Count > 0
                ? $"Generate brief reactions from listeners ({listenerNames}). Each person who heard should speak at least once. Do not repeat the initial announcement."
                : "Generate brief reactions from nearby listeners. Do not repeat the initial announcement.");

            sb.Append(topicSb).Append(intentSb);
        }
        else if (talkRequest.TalkType.IsFromUser())
        {
            var speaker1Name = PromptService.GetUniqueName(pawns[1], pawns);
            topicSb.Append($"{speaker1Name}({pawns[1].GetRole()}) said to {shortName}: '{talkRequest.Prompt}'. ");

            var mode = Settings.Get().PlayerDialogueMode;

            if (!pawns[1].IsPlayer())
            {
                // Pawn to Pawn
                bool multiTurn = mode != Settings.PlayerDialogueMode.Manual;
                intentSb.Append(multiTurn
                    ? $"Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {shortName}"
                    : $"Generate dialogue starting after this. Do not generate any further lines for {speaker1Name}");
            }
            else
            {
                // Player to Pawn
                if (mode == Settings.PlayerDialogueMode.AIDriven)
                    intentSb.Append($"Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {shortName}");
                else if (mode == Settings.PlayerDialogueMode.AIDrivenPawnOnly && pawns.Count > 2)
                    intentSb.Append($"Generate multi turn dialogues starting after this (do not repeat initial dialogue), beginning with {shortName}. Do not generate any further lines for {speaker1Name}");
                else
                    intentSb.Append($"Generate dialogue starting after this. Do not generate any further lines for {speaker1Name}");
            }

            sb.Append(topicSb).Append(intentSb);
        }
        else
        {
            bool inCombat = mainPawn.IsInCombat() || mainPawn.GetMapRole() == MapRole.Invading;
            bool hasActiveHostiles = inCombat && mainPawn.HasActiveHostiles();

            if (inCombat)
            {
                if (talkRequest.TalkType != TalkType.Urgent && !mainPawn.InMentalState)
                    talkRequest.Prompt = null;

                talkRequest.TalkType = TalkType.Urgent;

                if (mainPawn.CurJobDef == JobDefOf.Flee || mainPawn.CurJobDef == JobDefOf.FleeAndCower)
                {
                    intentSb.Append($"{shortName} dialogue short, panicked/retreating tone (fleeing)");
                }
                else if (!hasActiveHostiles)
                {
                    intentSb.Append($"{shortName} dialogue short, confident/victorious tone (destroying remnants/mopping up)");
                }
                else
                {
                    intentSb.Append(mainPawn.IsSlave || mainPawn.IsPrisoner
                        ? $"{shortName} dialogue short (worry)"
                        : $"{shortName} dialogue short, urgent tone ({mainPawn.GetMapRole().ToString().ToLower()}/command)");
                }
            }
            else if (pawns.Count == 1)
            {
                intentSb.Append(talkRequest.Prompt != null
                    ? $"{shortName} start monologue"
                    : $"{shortName} continue monologue");
            }
            else
            {
                intentSb.Append(talkRequest.Prompt != null
                    ? $"{shortName} start conversation, taking turns"
                    : $"{shortName} continue, taking turns");
            }

            if (mainPawn.InMentalState)
                topicSb.Append("be distressed (mental break)");
            else if (mainPawn.Downed && !mainPawn.IsBaby())
                topicSb.Append("(downed in pain. Short, strained dialogue)");
            else if (talkRequest.Prompt != null)
                topicSb.Append(talkRequest.Prompt);
            else if (talkRequest.TalkType != TalkType.Urgent)
            {
                string topicKeywords = TopicService.TryGetTopic(mainPawn);
                if (topicKeywords != null)
                    topicSb.Append($"Topic keywords: {topicKeywords}.");
            }

            sb.Append(intentSb);
            if (topicSb.Length > 0)
                sb.Append("\n").Append(topicSb);
        }

        intent = intentSb.ToString();
        topic = topicSb.ToString();
    }

    public static void BuildLocationContext(StringBuilder sb, ContextSettings contextSettings, Pawn mainPawn)
    {
        if (!contextSettings.IncludeLocationAndTemperature) return;
        
        var locationStatus = ContextHelper.GetPawnLocationStatus(mainPawn);
        if (string.IsNullOrEmpty(locationStatus)) return;
        
        var temperature = Mathf.RoundToInt(mainPawn.Position.GetTemperature(mainPawn.Map));
        var room = mainPawn.GetRoom();
        var roomRole = room is { PsychologicallyOutdoors: false } ? room.Role?.label ?? "Room" : "";

        var locationInfo = string.IsNullOrEmpty(roomRole)
            ? $"{locationStatus};{temperature}C"
            : $"{locationStatus};{temperature}C;{roomRole}";
        
        // Apply pawn hooks (location is now a pawn property)
        locationInfo = ContextHookRegistry.ApplyPawnHooks(
            ContextCategories.Pawn.Location, mainPawn, locationInfo);
        sb.Append($"\nLocation: {locationInfo}");
    }

    public static void BuildEnvironmentContext(StringBuilder sb, ContextSettings contextSettings, Pawn mainPawn)
    {
        if (contextSettings.IncludeTerrain)
        {
            var terrain = mainPawn.Position.GetTerrain(mainPawn.Map);
            if (terrain != null)
            {
                var value = ContextHookRegistry.ApplyPawnHooks(
                    ContextCategories.Pawn.Terrain, mainPawn, terrain.LabelCap);
                sb.Append($"\nTerrain: {value}");
            }
        }

        if (contextSettings.IncludeBeauty)
        {
            var beautyLabel = Describer.Beauty(mainPawn);
            if (!string.IsNullOrEmpty(beautyLabel))
            {
                var value = ContextHookRegistry.ApplyPawnHooks(
                    ContextCategories.Pawn.Beauty, mainPawn, beautyLabel);
                sb.Append($"\nSurroundings beauty: {value}");
            }
        }

        var pawnRoom = mainPawn.GetRoom();
        if (contextSettings.IncludeCleanliness && pawnRoom is { PsychologicallyOutdoors: false })
        {
            var value = ContextHookRegistry.ApplyPawnHooks(
                ContextCategories.Pawn.Cleanliness, mainPawn,
                Describer.Cleanliness(pawnRoom.GetStat(RoomStatDefOf.Cleanliness)));
            sb.Append($"\nCleanliness: {value}");
        }

        if (contextSettings.IncludeSurroundings)
        {
            var surroundingsText = ContextHelper.CollectNearbyContextText(mainPawn, 3);
            if (!string.IsNullOrEmpty(surroundingsText))
            {
                var value = ContextHookRegistry.ApplyPawnHooks(
                    ContextCategories.Pawn.Surroundings, mainPawn, surroundingsText);
                sb.Append("\nSurroundings:\n");
                sb.Append(value);
            }
        }
    }

    public static string GetEventsContext(Map map, PromptService.InfoLevel infoLevel = PromptService.InfoLevel.Normal)
    {
        return EventService.GetEventsContext(map, infoLevel);
    }

    [Obsolete("Use CommonUtil.Sanitize instead. Kept for backward compatibility.")]
    public static string Sanitize(string text, Pawn pawn = null)
    {
        return CommonUtil.Sanitize(text, pawn);
    }
}
