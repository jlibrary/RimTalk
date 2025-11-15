using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimLife
{
    // Summarizes a pawn's current activity in a compact but informative structure.
    // Output is JSON text to align with HealthInfo/SpeciesInfo patterns.
    internal sealed class Activity
    {
        private readonly Pawn _pawn;

        public Activity(Pawn pawn)
        {
            _pawn = pawn;
        }

        // Public accessor used by PawnPro
        public IEnumerable<string> ActionQueue
        {
            get
            {
                try
                {
                    return _pawn?.jobs?.jobQueue?
                        .Select(qj => SafeReport(qj.job))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray() ?? Array.Empty<string>();
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
        }

        // Lite JSON: core fields only
        public string ToStringLite()
        {
            Build(out var kind, out var text, out var target, out var lord, out var mental, out var research);

            return text;
        }

        // Full JSON: include queue and extra flags
        public string ToStringFull()
        {
            Build(out var kind, out var text, out var target, out var lord, out var mental, out var research);
            var jw = new Tool.JsonWriter(256)
                .Prop("text", text)
                .Prop("kind", kind);

            if (target != null) jw.Prop("target", target);
            if (mental != null) jw.Prop("mental", mental);
            if (lord != null) jw.Prop("lord", lord);
            if (research != null)
            {
                var rj = new Tool.JsonWriter(64)
                    .Prop("project", research.Value.project)
                    .Prop("percent", research.Value.percent);
                jw.PropRaw("research", rj.Close());
            }

            return jw.Close();
        }

        private void Build(out string kind, out string text, out string target, out string lord, out string mental, out (string project, int percent)? research)
        {
            kind = "idle";
            text = "Idle";
            target = null;
            lord = null;
            mental = null;
            research = null;

            if (_pawn == null)
            {
                return;
            }

            //1) Mental state takes precedence
            if (_pawn.InMentalState)
            {
                kind = "mental";
                mental = _pawn.MentalState?.InspectLine;
                text = mental ?? "Mental break";
                return;
            }

            // No current job
            if (_pawn.CurJobDef == null)
            {
                return;
            }

            //2) Attacking target if any
            bool attacking = _pawn.stances?.curStance is Stance_Busy busy && busy.verb != null;
            if (attacking)
            {
                kind = "attacking";
                target = _pawn.TargetCurrentlyAimingAt.Thing?.LabelShortCap;
                text = target != null ? $"Attacking {target}" : "Attacking";
            }

            //3) Lord and job reports
            lord = _pawn.GetLord()?.LordJob?.GetReport(_pawn);
            var job = SafeReport(_pawn.CurJob);

            if (!attacking)
            {
                if (lord == null)
                {
                    kind = string.IsNullOrEmpty(job) ? "idle" : "job";
                    text = string.IsNullOrEmpty(job) ? "Idle" : job;
                }
                else
                {
                    kind = string.IsNullOrEmpty(job) ? "lord" : "lord+job";
                    text = string.IsNullOrEmpty(job) ? lord : $"{lord} ({job})";
                }
            }

            //4) Research progress attachment for known research jobs
            if (_pawn.CurJob?.def != null && ResearchJobDefNames.Contains(_pawn.CurJob.def.defName))
            {
                ResearchProjectDef project = Find.ResearchManager.GetProject();
                if (project != null)
                {
                    float progress = Find.ResearchManager.GetProgress(project);
                    int pct = project.baseCost >0 ? (int)((progress / project.baseCost) *100f) :0;
                    research = (project.label, pct);
                    text += project != null ? $" (Project: {project.label} - {pct:F0}%)" : string.Empty;
                }
            }
        }

        private string SafeReport(Job job)
        {
            try
            {
                // Prefer driver report when possible for richer text
                var driverReport = _pawn?.jobs?.curDriver?.job == job ? _pawn?.jobs?.curDriver?.GetReport() : null;
                if (!string.IsNullOrEmpty(driverReport)) return driverReport;

                // Fallback: queued job has no driver, use definition report string
                return job?.def?.reportString?.CapitalizeFirst();
            }
            catch
            {
                return null;
            }
        }

        private bool IsInCombat()
        {
            if (_pawn == null) return false;
            if (_pawn.mindState?.enemyTarget != null) return true;
            if (_pawn.stances?.curStance is Stance_Busy busy && busy.verb != null) return true;

            Pawn hostilePawn = GetHostilePawnNearBy(_pawn);
            return hostilePawn != null && _pawn.Position.DistanceTo(hostilePawn.Position) <=20f;
        }

        private bool IsInDanger()
        {
            if (_pawn == null) return false;
            if (_pawn.Dead) return true;
            if (_pawn.Downed) return true;
            if (!_pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return true;
            if (_pawn.IsBurning()) return true;
            if (_pawn.health.hediffSet.PainTotal >= _pawn.GetStatValue(StatDefOf.PainShockThreshold)) return true;
            if (_pawn.health.hediffSet.BleedRateTotal >0.3f) return true;
            if (IsInCombat()) return true;
            if (_pawn.CurJobDef == JobDefOf.Flee) return true;

            foreach (var h in _pawn.health.hediffSet.hediffs)
            {
                if (h.Visible && (h.CurStage?.lifeThreatening == true ||
                    (h.def.lethalSeverity >0 && h.Severity > h.def.lethalSeverity *0.8f)))
                    return true;
            }

            return false;
        }

        private static Pawn GetHostilePawnNearBy(Pawn pawn)
        {
            if (pawn == null) return null;
            var hostileTargets = pawn.Map?.attackTargetsCache?.TargetsHostileToFaction(pawn.Faction);
            if (hostileTargets == null) return null;

            Pawn closestPawn = null;
            float closestDistSq = float.MaxValue;

            foreach (var target in hostileTargets.Where(target => GenHostility.IsActiveThreatTo(target, pawn.Faction)))
            {
                if (target.Thing is not Pawn threatPawn) continue;
                Lord lord = threatPawn.GetLord();

                if (lord != null && (lord.CurLordToil is LordToil_ExitMapFighting || lord.CurLordToil is LordToil_ExitMap))
                    continue;

                if (threatPawn.RaceProps.IsMechanoid && lord != null && lord.CurLordToil is LordToil_DefendPoint)
                    continue;

                float distSq = pawn.Position.DistanceToSquared(threatPawn.Position);
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestPawn = threatPawn;
                }
            }

            return closestPawn;
        }

        // Known research job defs, including popular mods
        private static readonly HashSet<string> ResearchJobDefNames = new HashSet<string>
        {
            "Research",
            // MOD: Research Reinvented
            "RR_Analyse",
            "RR_AnalyseInPlace",
            "RR_AnalyseTerrain",
            "RR_Research",
            "RR_InterrogatePrisoner",
            "RR_LearnRemotely"
        };
    }
}
