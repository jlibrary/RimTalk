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
            return Cache.TalkersSortedByTick
                .Where(pawn => IsAbleToTalk(pawn) && pawn.Map == Find.CurrentMap)
                .Take(10)
                .ToList();
        }

        public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
        {
            List<Thought> thoughts = new List<Thought>();
            if (pawn.needs?.mood?.thoughts != null)
            {
                pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            }

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

        public static string GetHostileThreatDescription(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            // Find the closest hostile pawn
            Pawn closestHostile = pawn.Map.mapPawns.AllPawnsSpawned
                .Where(other => other != pawn && other.HostileTo(pawn))
                .OrderBy(other =>
                    pawn.Position.DistanceToSquared(other.Position)) // Use DistanceToSquared for efficiency
                .FirstOrDefault();

            if (closestHostile == null)
            {
                return null; // No hostiles on the map
            }

            float distance = pawn.Position.DistanceTo(closestHostile.Position);

            if (distance <= 10f)
            {
                return "(engaging in battle!)";
            }

            if (distance <= 20f)
            {
                return "(hostiles are dangerously close!)";
            }

            return "(on alert due to nearby hostiles)";
        }

        public static string SpecialConditionLabel(Pawn pawn)
        {
            if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold))
            {
                return "Downed By Pain";
            }

            // Check if the pawn is bleeding out.
            if (pawn.health.hediffSet.BleedRateTotal > 0.01f)
            {
                return "Bleeding Out";
            }

            // A general check for being unable to move, if not downed by a more specific cause.
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                return "Incapacitated, UnableToMove";
            }

            // Check if pawn is in combat first
            if (IsMeleeAttacking(pawn))
                return "fighting in melee";
            if (IsShooting(pawn))
                return "shooting at enemies";
            if (pawn.IsBurning())
                return "you are Caught in fire!!";

            if (pawn.InMentalState)
                return pawn.MentalState.def.LabelCap;

            return null;
        }

        public static string GetTalkSubject(Pawn pawn)
        {
            if (IsInvader(pawn))
                return "invading user colony";

            var text = "";
            // TODO:FIX
            foreach (Pawn otherPawn in Cache.Keys)
            {
                if (pawn != otherPawn && pawn.Position.DistanceTo(otherPawn.Position) <= 10f)
                {
                    string specialCondition = SpecialConditionLabel(otherPawn);
                    if (specialCondition != null && Rand.Chance(0.25f))
                    {
                        text += $"{otherPawn.Name.ToStringShort} in {specialCondition},";
                    }
                }
            }

            text += GetHostileThreatDescription(pawn);

            if (text.NullOrEmpty())
                text = "continue conversation if person nearby, else new topic. No repetition.";
            else
                return $"while {text}";

            if (pawn.CurJobDef == JobDefOf.GotoWander || pawn.CurJobDef == JobDefOf.Wait_Wander)
                return text;

            string jobString = "";
            PawnState pawnState = Cache.Get(pawn);
            if (pawnState != null && pawnState.CurrentJob != pawn.CurJob.def)
            {
                pawnState.CurrentJob = pawn.CurJob.def;
                jobString = GetJobString(pawn);
            }

            return $"{text} {jobString}";
        }

        public static void BuildContext(List<Pawn> pawns)
        {
            if (AIService.IsContextUpdating()) return;

            StringBuilder context = new StringBuilder();
            var instruction = Regex.Replace(Constant.Instruction, @"\r\n", "\n");
            instruction = Regex.Replace(instruction, @"  +", " ");

            context.AppendLine(instruction).AppendLine();

            int count = 0;

            foreach (Pawn pawn in pawns)
            {
                string pawnContext = CreatePawnContext(pawn);
                Cache.Get(pawn).Context = pawnContext;
                count++;
                context.AppendLine();
                context.AppendLine($"[Person {count} START]");
                context.AppendLine(pawnContext);
                context.AppendLine($"[Person {count} END]");
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

            var role = $"Role: {GetRole(pawn)}";
            sb.AppendLine(role);

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

                var memes = ideo?.memes?
                    .Where(m => m != null)
                    .Select(m => m.LabelCap.Resolve())
                    .Where(label => !string.IsNullOrEmpty(label))
                    .ToList();

                if (memes != null && memes.Any())
                {
                    sb.AppendLine($"Memes: {string.Join(", ", memes)}");
                }
            }

            if (IsInvader(pawn))
                return sb.ToString();

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
                if (withDesc) adulthood += $":{Sanitize(pawn.story.Adulthood.description, pawn)}";
                sb.AppendLine(adulthood);
            }

            var traits = "Traits: \n";
            foreach (Trait trait in pawn.story.traits.TraitsSorted)
            {
                foreach (TraitDegreeData degreeData in trait.def.degreeDatas)
                {
                    if (degreeData.degree == trait.Degree)
                    {
                        traits += degreeData.label + (withDesc ? $":{Sanitize(degreeData.description, pawn)}\n" : ",");
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

            // add Health
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

            if (IsInvader(pawn))
                return sb.ToString();

            var mood = $"Mood: {pawn.needs?.mood?.MoodString ?? "N/A"}";
            sb.AppendLine(mood);

            var thoughts = "Memory: ";
            foreach (Thought thought in GetThoughts(pawn).Keys)
            {
                thoughts += $"{Sanitize(thought.LabelCap)}, ";
            }

            sb.AppendLine(thoughts);

            if (IsVisitor(pawn))
                return sb.ToString();

            sb.AppendLine(RelationsService.GetRelationsString(pawn));

            var equipment = "Equipment: ";
            if (pawn.equipment?.Primary != null)
                equipment += $"Weapon: {pawn.equipment.Primary.LabelCap}, ";

            var wornApparel = pawn.apparel?.WornApparel;
            var apparelLabels = wornApparel != null ? wornApparel.Select(a => a.LabelCap) : Enumerable.Empty<string>();

            if (apparelLabels.Any())
            {
                equipment += $"Apparel: {string.Join(", ", apparelLabels)}";
            }

            if (equipment != "Equipment: ")
                sb.AppendLine(equipment);

            var personality = Cache.Get(pawn).Personality;
            if (personality != null)
                sb.AppendLine($"Personality: {personality}");

            return sb.ToString();
        }

        private static string GetRole(Pawn pawn)
        {
            if (pawn.IsPrisoner) return "Prisoner";
            if (pawn.IsSlave) return "Slave";
            if (IsVisitor(pawn)) return "Visitor";
            if (IsInvader(pawn)) return "Invader";
            if (pawn.IsFreeColonist) return "Colonist";
            return "Unknown";
        }

        private static string Sanitize(string text, Pawn pawn = null)
        {
            if (pawn != null)
                text = text.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).Resolve();
            return text.StripTags().RemoveLineBreaks();
        }

        private static string GetJobString(Pawn pawn)
        {
            string jobString = pawn.GetJobReport();

            // Replace non-colonist names with their occupation/status
            if (pawn.CurJob != null)
            {
                Pawn targetPawn = GetTargetPawnFromJob(pawn.CurJob);
                if (targetPawn != null)
                {
                    jobString = jobString.Replace(
                        targetPawn.LabelShort,
                        GetPawnName(pawn, targetPawn)
                    );
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
            if (pawn.Map == null || Cache.Get(pawn) == null)
                return "none";

            // TODO:fix
            var nearbyPawnNames = Cache.GetList()
                .Where(nearbyPawn => nearbyPawn != pawn)
                .Where(IsAbleToTalk)
                .Where(nearbyPawn => nearbyPawn.health.capacities.GetLevel(PawnCapacityDefOf.Hearing) > 0.0)
                .Where(nearbyPawn => pawn.GetRoom() == nearbyPawn.GetRoom()) // Same room check
                .Where(nearbyPawn => nearbyPawn.Position.InHorDistOf(
                    pawn.Position, HearingRange * nearbyPawn.health.capacities.GetLevel(PawnCapacityDefOf.Hearing)))
                .OrderBy(nearbyPawn => pawn.Position.DistanceTo(nearbyPawn.Position)) // Sort by distance
                .Take(5) // Limit to first 5 nearest
                .Select(nearbyPawn => GetPawnName(pawn, nearbyPawn))
                .ToList();

            return nearbyPawnNames.Any() ? string.Join(", ", nearbyPawnNames) : "none";
        }

        public static bool IsVisitor(Pawn pawn)
        {
            return pawn.Faction != null && pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer);
        }

        public static bool IsInvader(Pawn pawn)
        {
            return pawn.HostileTo(Faction.OfPlayer);
        }

        public static string GetPawnName(Pawn pawn, Pawn nearbyPawn)
        {
            // If both are same type or same faction, return the name
            if ((pawn.IsPrisoner && nearbyPawn.IsPrisoner) ||
                (pawn.IsSlave && nearbyPawn.IsSlave) ||
                (pawn.Faction != null && pawn.Faction == nearbyPawn.Faction))
            {
                return nearbyPawn.Name.ToStringShort;
            }

            // Prisoner sees colonist as master
            if (pawn.IsPrisoner && nearbyPawn.Faction == Faction.OfPlayer)
                return "master";

            // Slave sees colonist as master
            if (pawn.IsSlave && nearbyPawn.Faction == Faction.OfPlayer)
                return "master";

            // Labels based on type or faction relationship
            if (nearbyPawn.IsPrisoner) return "prisoner";
            if (nearbyPawn.IsSlave) return "slave";

            if (nearbyPawn.Faction != null)
            {
                if (pawn.Faction != null && pawn.Faction.HostileTo(nearbyPawn.Faction))
                    return "invader";

                // Friendly visitor or colonist
                string typeLabel = nearbyPawn.Faction == Faction.OfPlayer ? "colonist" : "visitor";
                return $"{nearbyPawn.Name.ToStringShort} ({typeLabel})";
            }

            // Default to name
            return nearbyPawn.Name.ToStringShort;
        }

        public static bool IsShooting(Pawn pawn)
        {
            if (pawn?.stances == null)
                return false;

            if (pawn.stances.curStance is Stance_Busy busy && busy.verb != null)
                return !busy.verb.IsMeleeAttack;

            return pawn.CurJobDef == JobDefOf.AttackStatic;
        }

        public static bool IsMeleeAttacking(Pawn pawn)
        {
            if (pawn?.stances == null)
                return false;

            if (pawn.stances.curStance is Stance_Busy busy && busy.verb != null)
                return busy.verb.IsMeleeAttack;

            return pawn.CurJobDef == JobDefOf.AttackMelee;
        }
    }
}