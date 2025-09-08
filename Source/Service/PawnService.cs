using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimTalk.Util;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimTalk.Service
{
    public static class PawnService
    {
        public static Dictionary<Thought, float> GetThoughts(Pawn pawn)
        {
            var thoughts = new List<Thought>();
            pawn?.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

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
        
        public static bool IsPawnInDanger(Pawn pawn)
        {
            if (pawn.Dead) return true;
            if (pawn.Downed) return true;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
            if (pawn.InMentalState) return true;
            if (pawn.IsBurning()) return true;
            if (pawn.health.hediffSet.PainTotal >= pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
            if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;
            if (IsPawnInCombat(pawn)) return true;
            if (pawn.CurJobDef == JobDefOf.Flee) return true;

            // Check severe Hediffs
            foreach (var h in pawn.health.hediffSet.hediffs)
            {
                if (h.Visible && (h.CurStage?.lifeThreatening == true || 
                                  h.def.lethalSeverity > 0 && h.Severity > h.def.lethalSeverity * 0.8f))
                    return true;
            }

            return false;
        }
        
        public static bool IsPawnInCombat(Pawn pawn)
        {
            if (pawn == null) return false;

            // 1. MindState target
            if (pawn.mindState.enemyTarget != null) return true;

            // 2. Stance busy with attack verb
            if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null)
                return true;

            return false;
        }

        public static string GetRole(Pawn pawn)
        {
            if (pawn.IsPrisoner) return "Prisoner";
            if (pawn.IsSlave) return "Slave";
            if (IsVisitor(pawn)) return "Visitor";
            if (IsInvader(pawn)) return "Invader";
            if (pawn.IsFreeColonist) return "Colonist";
            return "Unknown";
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
                return $"{nearbyPawn.Name.ToStringShort}(master)";

            // Slave sees colonist as master
            if (pawn.IsSlave && nearbyPawn.Faction == Faction.OfPlayer)
                return $"{nearbyPawn.Name.ToStringShort}(master)";

            // Labels based on type or faction relationship
            if (nearbyPawn.IsPrisoner) return "prisoner";
            if (nearbyPawn.IsSlave) return "slave";

            if (nearbyPawn.Faction != null)
            {
                if (pawn.Faction != null && pawn.Faction.HostileTo(nearbyPawn.Faction))
                    return "invader";

                // Friendly visitor or colonist
                string typeLabel = nearbyPawn.Faction == Faction.OfPlayer ? $"{nearbyPawn.Name.ToStringShort}(colonist)" : "visitor";
                return $"{nearbyPawn.Name.ToStringShort} ({typeLabel})";
            }

            // Default to name
            return nearbyPawn.Name?.ToStringShort ?? nearbyPawn.LabelShort;
        }

        public static string GetPawnStatusFull(Pawn pawn)
        {

            bool isInDanger = false;
            pawn.def.hideMainDesc = true;
            string status = pawn.GetInspectString();
            List<string> parts = new List<string>();
            
            // --- 1. Nearby pawns ---
            List<Pawn> nearByPawns = PawnSelector.GetAllNearByPawns(pawn);
            if (nearByPawns.Any())
            {
                // Collect critical statuses of nearby pawns
                var nearbyNotableStatuses = nearByPawns
                        .Where(IsPawnInDanger)
                        .Take(2)
                        .Select(other => $"{other.Name.ToStringShort} in {other.GetInspectString().Replace("\n", "; ")}")
                        .ToList();

                if (nearbyNotableStatuses.Any())
                {
                    parts.Add("\nPeople in condition nearby: " + string.Join("; ", nearbyNotableStatuses));
                    isInDanger = true;
                }

                // Names of nearby pawns
                var nearbyNames = nearByPawns
                    .Select(nearbyPawn => 
                    {
                        string name = GetPawnName(pawn, nearbyPawn);
                        if (Cache.Get(nearbyPawn) is PawnState pawnState && !pawnState.CanDisplayTalk())
                        {
                            name += "(mute)";
                        }
                        return name;
                    })
                    .ToList();

                string nearbyText = nearbyNames.Count == 0 ? "none"
                    : nearbyNames.Count > 3
                        ? string.Join(", ", nearbyNames.Take(3)) + ", and others"
                        : string.Join(", ", nearbyNames);

                parts.Add($"Nearby: {nearbyText}");
            }
            else
            {
                parts.Add("Nearby people: none");
            }
            
            // --- 2. Add time ---
            parts.Add($"Time: {CommonUtil.GetInGameHour12HString()}");
            
            // --- 3. Add status ---
            parts.Add($"Currently: {status}");
            
            if (IsPawnInDanger(pawn))
                parts.Add("\nbe dramatic");

            if (IsInvader(pawn))
            {
                parts.Add("\ninvading user colony");
                return string.Join("\n", parts);
            }

            // --- 4. Enemy proximity / combat info ---
            Pawn nearestHostile = HostilePawnNearBy(pawn);
            if (nearestHostile != null)
            {
                float distance = pawn.Position.DistanceTo(nearestHostile.Position);

                if (distance <= 10f)
                    parts.Add("\nThreat: Engaging in battle!");
                else if (distance <= 20f)
                    parts.Add("\nThreat: Hostiles are dangerously close!");
                else
                    parts.Add("\nAlert: hostiles in the area");
                isInDanger = true;
            }
            
            if (!isInDanger)
                parts.Add(Constant.Prompt);

            return string.Join("\n", parts);
        }

        public static Pawn HostilePawnNearBy(Pawn pawn)
        {
            return GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                Find.CurrentMap,
                Find.CurrentMap.mapPawns.AllPawnsSpawned
                    .Where(p =>
                        p.HostileTo(Faction.OfPlayer) &&
                        p.Spawned &&
                        p.Awake() &&
                        p.health.capacities.CapableOf(PawnCapacityDefOf.Moving) &&
                        p.CurJobDef != null &&            // must have an active job
                        p.GetLord() != null &&            // belongs to a raid/siege lord
                        (p.GetLord().LordJob is LordJob_AssaultColony ||
                         p.GetLord().LordJob is LordJob_Siege))
                    .Cast<Thing>(),
                PathEndMode.OnCell,
                TraverseParms.For(pawn),
                9999f) as Pawn;
        }
    }
}