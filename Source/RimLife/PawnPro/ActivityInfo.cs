using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimLife
{
    // Summarizes a pawn's current activity in a compact but informative structure.
    // Output is JSON text to align with HealthInfo/SpeciesInfo patterns.
    internal sealed class ActivityInfo
    {
        // --- Constructor ---
        public ActivityInfo(Pawn pawn)
        {
            _pawn = pawn;
        }

        // --- Public Properties ---
        public IEnumerable<string> ActionQueue
        {
            get
            {
                try
                {
                    return _pawn?.jobs?.jobQueue?
                        .Select(qj => SafeReport(qj.job))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray() ?? Enumerable.Empty<string>();
                }
                catch
                {
                    return Enumerable.Empty<string>();
                }
            }
        }

        // --- Public Methods ---

        // Lite JSON: core fields only
        public string ToStringLite()
        {
            Build(out _, out var text, out _, out _, out _, out _);
            return text;
        }

        // Full JSON: include queue and extra flags
        public string ToStringFull()
        {
            Build(out var kind, out var text, out var target, out var lord, out var mental, out var research);
            var jw = new Tool.JsonWriter(256)
                .Prop("Text", text)
                .Prop("Kind", kind);

            if (target != null) jw.Prop("Target", target);
            if (mental != null) jw.Prop("MentalState", mental);
            if (lord != null) jw.Prop("LordJob", lord);
            if (research != null)
            {
                var rj = new Tool.JsonWriter(64)
                    .Prop("Project", research.Value.project)
                    .Prop("Percent", research.Value.percent);
                jw.PropRaw("Research", rj.Close());
            }

            return jw.Close();
        }

        // --- Private Fields ---

        private readonly Pawn _pawn;

        // --- Private Constants ---
        private const string KindIdle = "Idle";
        private const string KindMental = "Mental";
        private const string KindAttacking = "Attacking";
        private const string KindJob = "Job";
        private const string KindLord = "Lord";
        private const string KindLordAndJob = "LordAndJob";

        // --- Private Methods ---

        private void Build(out string kind, out string text, out string target, out string lord, out string mental, out (string project, int percent)? research)
        {
            // Default values
            kind = KindIdle;
            text = "Idle";
            target = null;
            lord = null;
            mental = null;
            research = null;

            if (_pawn == null) return;

            // 1. Mental state takes precedence
            if (_pawn.InMentalState)
            {
                kind = KindMental;
                mental = _pawn.MentalState?.InspectLine;
                text = mental ?? "Mental break";
                return;
            }

            // No current job
            if (_pawn.CurJobDef == null) return;

            // 2. Attacking status
            bool isAttacking = _pawn.stances?.curStance is Stance_Busy busy && busy.verb != null;
            if (isAttacking)
            {
                kind = KindAttacking;
                target = _pawn.TargetCurrentlyAimingAt.Thing?.LabelShortCap;
                text = target != null ? $"Attacking {target}" : "Attacking";
            }

            // 3. Lord and job reports
            lord = _pawn.GetLord()?.LordJob?.GetReport(_pawn);
            var jobReport = SafeReport(_pawn.CurJob);

            if (!isAttacking)
            {
                if (string.IsNullOrEmpty(lord))
                {
                    if (!string.IsNullOrEmpty(jobReport))
                    {
                        kind = KindJob;
                        text = jobReport;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(jobReport))
                    {
                        kind = KindLord;
                        text = lord;
                    }
                    else
                    {
                        kind = KindLordAndJob;
                        text = $"{lord} ({jobReport})";
                    }
                }
            }

            // 4. Research progress attachment
            if (_pawn.CurJob?.def != null && ResearchJobDefNames.Contains(_pawn.CurJob.def.defName))
            {
                ResearchProjectDef currentProject = Find.ResearchManager.GetProject();
                if (currentProject != null)
                {
                    float progress = Find.ResearchManager.GetProgress(currentProject);
                    int pct = currentProject.baseCost > 0 ? (int)((progress / currentProject.baseCost) * 100f) : 0;
                    research = (currentProject.label, pct);
                    text += $" (Project: {currentProject.label} - {pct}%)";
                }
            }
        }

        private string SafeReport(Job job)
        {
            try
            {
                if (job == null) return null;

                // Prefer driver report for richer text
                var driver = _pawn?.jobs?.curDriver;
                if (driver?.job == job)
                {
                    string driverReport = driver.GetReport();
                    if (!string.IsNullOrEmpty(driverReport)) return driverReport;
                }

                // Fallback for queued jobs or when driver report is empty
                return job.def?.reportString?.CapitalizeFirst();
            }
            catch
            {
                return null; // Suppress errors for stability
            }
        }

        // --- Private Static Fields ---

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
