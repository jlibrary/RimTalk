using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimTalk.Service
{
    public static class PawnService
    {

        private const float HearingRange = 10f;
        public static bool IsAbleToTalk(Pawn pawn)
        {
            return pawn.Awake()
                   && pawn.CurJobDef != JobDefOf.LayDown
                   && pawn.CurJobDef != JobDefOf.LayDownAwake
                   && pawn.CurJobDef != JobDefOf.LayDownResting;
        }
        
        public static List<Pawn> GetPawnsAbleToTalk()
        {
            List<Pawn> list = new List<Pawn>();

            // Sort the pawns by LastTalkTick (ascending)
            IEnumerable<Pawn> sortedPawns = Cache.Keys.OrderBy(pawn => Cache.Get(pawn).LastTalkTick);

            foreach (Pawn pawn in sortedPawns)
            {
                if (IsAbleToTalk(pawn) && pawn.Map == Find.CurrentMap)
                {
                    list.Add(pawn);
                }
            }

            return list;
        }

        public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
        {
            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            return thoughts
                .GroupBy(t => t.def.defName)
                .ToDictionary(g => g.First(), g => g.Sum(t => t.MoodOffset()));
        }

        public static HashSet<Hediff> GetHediffs(Pawn pawn)
        {
            return pawn.health.hediffSet.hediffs.Where(hediff => hediff.Visible).ToHashSet();
        }

        // Compare and return new thought that has the highest overall effect
        public static KeyValuePair<Thought, float> GetNewThought(Pawn pawn)
        {
            var newThoughts = GetThoughts(pawn).OrderByDescending(kvp => Math.Abs(kvp.Value));

            return newThoughts.FirstOrDefault(kvp =>
                !Cache.Get(pawn).Thoughts.TryGetValue(kvp.Key.def.defName, out float moodOffset) ||
                Math.Abs(kvp.Value) > Math.Abs(moodOffset));
        }

        public static string GetNewThoughtLabel(Thought thought)
        {
            if (thought == null) return null;

            // var offset = thought.MoodOffset();
            // var attitude = offset > 0 ? "up" : offset < 0 ? "down" : "";

            return $"thought: {thought.LabelCap} - {thought.Description}";
        }

        public static string GetTalkSubject(Pawn pawn)
        {
            var text = "";
            foreach (Pawn otherPawn in Cache.Keys)
            {
                // override talk subject to other pawn's mental state 25% of time
                if (otherPawn.InMentalState && Rand.Chance(0.25f))
                {
                    text += $"{otherPawn.Name.ToStringShort} in {otherPawn.MentalState.def.LabelCap},";
                }
            }

            if (text.NullOrEmpty())
                text = "continue conversation if person nearby, else new topic. No repetition.";
            else
            {
                return $"while {text}";
            }

            if (pawn.CurJobDef == JobDefOf.GotoWander || pawn.CurJobDef == JobDefOf.Wait_Wander)
                return text;

            string jobString = "";
            PawnState pawnState = Cache.Get(pawn);
            // check current job to avoid repeating
            if (pawnState != null && pawnState.CurrentJob != pawn.CurJob.def)
            {
                pawnState.CurrentJob = pawn.CurJob.def;
                jobString = GetJobString(pawn);
            }

            return $"{text} {jobString}";
        }

        public static void BuildContext(List<Pawn> pawns = null)
        {
            if (AIService.IsContextUpdating()) return;

            StringBuilder context = new StringBuilder();
            var instruction = Regex.Replace(Constant.Instruction, @"\r\n", "\n");
            instruction = Regex.Replace(instruction, @"  +", " ");

            context.AppendLine(instruction).AppendLine();

            int count = 0;
            
            foreach (Map map in Find.Maps)
            {
                if (pawns == null)
                    pawns = map.mapPawns.FreeColonistsSpawned.ToList();

                foreach (Pawn pawn in pawns)
                {
                    if (pawn.IsFreeColonist)
                    {
                        string pawnContext = CreatePawnContext(pawn);
                        Cache.Get(pawn).Context = pawnContext;
                        count++;
                        context.AppendLine();
                        context.AppendLine($"[Colonist {count} START]");
                        context.AppendLine(pawnContext);
                        context.AppendLine($"[Colonist {count} END]");
                    }
                }
            }

            if (count == 1)
                context.AppendLine("You are alone. Speak as internal monologue.");
            
            if (count != 0)
                AIService.UpdateContext(context.ToString());
        }

        public static string CreatePawnBackstory(Pawn pawn, bool withDesc = false)
        {
            StringBuilder sb = new StringBuilder();
            
            var name = pawn.Name.ToStringShort;
            var title = pawn.story.title == null ? "" : $"({pawn.story.title})";
            var genderAndAge = Regex.Replace(pawn.MainDesc(false), @"\(\d+\)", "");
            sb.AppendLine($"{name} {title} ({genderAndAge})");
            
            if (ModsConfig.BiotechActive && pawn.genes?.Xenotype != null)
            {
                var xenotypeInfo = $"Race: {pawn.genes.Xenotype.LabelCap}";
                if (!pawn.genes.Xenotype.descriptionShort.NullOrEmpty())
                    xenotypeInfo += $" - {pawn.genes.Xenotype.descriptionShort}";
                if (withDesc)
                    xenotypeInfo += $" - {pawn.genes.Xenotype.description}";
                sb.AppendLine(xenotypeInfo);
            }

            if (ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
            {
                var notableGenes = pawn.genes.GenesListForReading
                    .Where(g => g.def.biostatMet != 0 || g.def.biostatCpx != 0)
                    .Select(g => g.def.LabelCap + (withDesc ? $":{g.def.description}" : ""));
    
                if (notableGenes.Any())
                {
                    sb.AppendLine($"Notable Genes: {string.Join(", ", notableGenes)}");
                }
            }

            // Add Ideology information
            if (ModsConfig.IdeologyActive && pawn.ideo?.Ideo != null)
            {
                var ideo = pawn.ideo.Ideo;
                
                var ideologyInfo = $"Ideology: {ideo.name}";
                sb.AppendLine(ideologyInfo);
                
                var memes = ideo.memes.Select(m => m.LabelCap.Resolve()).ToList();
                if (memes.Any())
                {
                    sb.AppendLine($"Memes: {string.Join(", ", memes)}");
                }
            }

            if (pawn.story.Childhood != null)
            {
                var childHood =
                    $"Childhood: {pawn.story.Childhood.title}({pawn.story.Childhood.titleShort})";
                if (withDesc) childHood += $":{Sanitize(pawn.story.Childhood.description, pawn)}";
                sb.AppendLine(childHood);
            }

            if (pawn.story.Adulthood != null)
            {
                var adulthood =
                    $"Adulthood: {pawn.story.Adulthood.title}({pawn.story.Adulthood.titleShort})";
                if (withDesc) adulthood += $":{Sanitize(pawn.story.Adulthood.description, pawn) }";
                sb.AppendLine(adulthood);
            }

            var traits = "Traits: \n";
            foreach (Trait trait in pawn.story.traits.TraitsSorted)
            {
                foreach (TraitDegreeData degreeData in trait.def.degreeDatas)
                {
                    if (degreeData.degree == trait.Degree)
                    {
                        traits += degreeData.label + (withDesc? $":{Sanitize(degreeData.description, pawn)}\n" : ",");
                        break;
                    }
                }
            }

            sb.AppendLine(traits);

            var skills = "Skills: ";
            foreach (SkillRecord skillRecord in pawn.skills.skills)
            {
                skills += $"{skillRecord.def.label}: {skillRecord.Level}, ";
            }

            sb.AppendLine(skills);
            
            return sb.ToString();
        }

        public static string CreatePawnContext(Pawn pawn)
        {
            pawn.def.hideMainDesc = true;

            StringBuilder sb = new StringBuilder();

            sb.Append(CreatePawnBackstory(pawn));
            
            var relations = "";
            foreach (Pawn otherPawn in pawn.relations.PotentiallyRelatedPawns)
                if (!otherPawn.Dead && !otherPawn.relations.hidePawnRelations)
                {
                    PawnRelationDef relationDef = pawn.GetMostImportantRelation(otherPawn);
                    if (relationDef != null)
                        relations +=
                            $"{relationDef.GetGenderSpecificLabel(otherPawn)}: {OpinionString(pawn, otherPawn)}, ";
                }

            foreach (Map map in Find.Maps)
            foreach (Pawn otherPawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                if (pawn != otherPawn && otherPawn.IsFreeColonist &&
                    !pawn.relations.PotentiallyRelatedPawns.Contains(otherPawn))
                    if (RelationString(pawn, otherPawn) != null)
                        relations += $"{RelationString(pawn, otherPawn)}: {OpinionString(pawn, otherPawn)}, ";

            if (relations != "")
                sb.AppendLine("Relations: " + relations);
            
            var equipment = "Equipment: ";
            if (pawn.equipment?.Primary != null)
                equipment += $"Weapon: {pawn.equipment.Primary.LabelCap}, ";

            var apparel = pawn.apparel?.WornApparel?.Select(a => a.LabelCap);
            if (apparel?.Any() == true)
                equipment += $"Apparel: {string.Join(", ", apparel)}";

            if (equipment != "Equipment: ")
                sb.AppendLine(equipment);

            var method = AccessTools.Method(typeof(HealthCardUtility), "VisibleHediffs");
            IEnumerable<Hediff> hediffs = (IEnumerable<Hediff>)method.Invoke(null, new object[] { pawn, false });

            var hediffDict = hediffs
                .GroupBy(hediff => hediff.def)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(",",
                        group.Select(hediff => hediff.Part?.Label ?? ""))); // Values are concatenated body parts

            var healthInfo = string.Join(",", hediffDict.Select(kvp => $"{kvp.Key.label}({kvp.Value})"));

            if (healthInfo != "")
                sb.AppendLine($"Health: {healthInfo}");

            var thoughts = "Memory: ";
            foreach (Thought thought in GetThoughts(pawn).Keys)
            {
                if (thought.def.durationDays > 0)
                    thoughts += $"{Sanitize(thought.LabelCap)}, ";
            }

            sb.AppendLine($"Personality: {Cache.Get(pawn).Personality}");

            sb.AppendLine(thoughts);

            var mood = $"Mood: {pawn.needs.mood.MoodString}";

            sb.Append(mood);

            return sb.ToString();
        }

        private static string Sanitize(string text, Pawn pawn = null)
        {
            if (pawn != null)
                text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).Resolve();
            return text.StripTags().RemoveLineBreaks();
        }

        private static string RelationString(Pawn pawn, Pawn otherPawn)
        {
            var opinion = pawn.relations.OpinionOf(otherPawn);
            if (opinion < -20)
            {
                return "Rival".Translate();
            }

            return opinion > 20 ? "Friend".Translate() : null;
        }

        private static string OpinionString(Pawn pawn, Pawn otherPawn)
        {
            var opinion = pawn.relations.OpinionOf(otherPawn).ToStringWithSign();
            return otherPawn.RaceProps.Humanlike
                ? $"{otherPawn.Name.ToStringShort} {opinion}"
                : otherPawn.Name.ToStringShort;
        }

        private static string GetJobString(Pawn pawn)
        {
            string jobString = pawn.GetJobReport();
    
            // Replace non-colonist names with their occupation/status
            if (pawn.CurJob != null)
            {
                Pawn targetPawn = GetTargetPawnFromJob(pawn.CurJob);
                if (targetPawn != null && !targetPawn.IsFreeColonist)
                {
                    string occupation = targetPawn.KindLabel;
                    jobString = jobString.Replace(targetPawn.LabelShort, occupation);
                }
            }
    
            string currentActivity = $"(currently {jobString}";

            if (pawn.CurJob?.def == JobDefOf.Research)
            {
                ResearchProjectDef project = Find.ResearchManager.GetProject();
                currentActivity += $" about {project.label}";
            }

            return currentActivity + ")";
        }

        private static Pawn GetTargetPawnFromJob(Job job)
        {
            if (job.targetA.HasThing && job.targetA.Thing is Pawn pawnA)
                return pawnA;
    
            if (job.targetB.HasThing && job.targetB.Thing is Pawn pawnB)
                return pawnB;
    
            if (job.targetC.HasThing && job.targetC.Thing is Pawn pawnC)
                return pawnC;
    
            return null;
        }

        public static string GetNearByPawn(Pawn pawn)
        {
            if (pawn.Map == null || pawn.Map.mapPawns?.FreeColonistsSpawned == null)
                return "none";

            if (pawn.Map == null || pawn.Map.mapPawns?.FreeColonistsSpawned == null)
                return "none";

            var nearbyPawnNames = pawn.Map.mapPawns.FreeColonistsSpawned
                .Where(colonist => colonist != pawn) 
                .Where(IsAbleToTalk) 
                .Where(colonist => colonist.health.capacities.GetLevel(PawnCapacityDefOf.Hearing) > 0.0)
                .Where(colonist => colonist.Position.InHorDistOf
                    (pawn.Position, HearingRange * colonist.health.capacities.GetLevel(PawnCapacityDefOf.Hearing)))
                .Where(colonist => pawn.GetRoom() == colonist.GetRoom()) // Same room check
                .Select(colonist => colonist.Name.ToStringShort)
                .ToList();
            
            return nearbyPawnNames.Any() ? string.Join(", ", nearbyPawnNames) : "none";
        }
    }
}