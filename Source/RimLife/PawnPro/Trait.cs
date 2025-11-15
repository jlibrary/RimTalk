using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimLife
{
    // 收集 Pawn 的特质与技能信息（动态读取）
    public class TraitsInfo
    {
        private readonly Pawn _pawn;

        public TraitsInfo(Pawn pawn)
        {
            _pawn = pawn;
        }

        // --- Helpers ---
        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static string BuildJsonArray(IEnumerable<string> items)
        {
            var sb = new StringBuilder(128);
            sb.Append("[");
            bool first = true;
            foreach (var s in items)
            {
                if (!first) sb.Append(",");
                sb.Append("\"").Append(EscapeJsonString(s ?? string.Empty)).Append("\"");
                first = false;
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string GetTraitLabel(Trait trait)
        {
            try
            {
                // Prefer current degree label if present
                var label = trait?.CurrentData?.label;
                if (!string.IsNullOrEmpty(label)) return label.CapitalizeFirst();

                // Fallback lookup by degree match
                if (trait?.def?.degreeDatas != null)
                {
                    foreach (var d in trait.def.degreeDatas)
                    {
                        if (d.degree == trait.Degree)
                            return (d.label ?? trait.def?.label ?? "Unknown").CapitalizeFirst();
                    }
                }

                return (trait?.def?.label ?? "Unknown").CapitalizeFirst();
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetTraitDescription(Trait trait)
        {
            try
            {
                var desc = trait?.CurrentData?.description;
                if (string.IsNullOrEmpty(desc))
                {
                    if (trait?.def?.degreeDatas != null)
                    {
                        foreach (var d in trait.def.degreeDatas)
                        {
                            if (d.degree == trait.Degree)
                            {
                                desc = d.description;
                                break;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(desc)) return string.Empty;

                // Minimal sanitize: remove tags and line breaks
                desc = desc.StripTags();
                desc = desc.Replace("\r", " ").Replace("\n", " ");
                return desc;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<Trait> GetTraits(Pawn pawn)
        {
            return pawn?.story?.traits?.TraitsSorted ?? Enumerable.Empty<Trait>();
        }

        private static IEnumerable<SkillRecord> GetSkills(Pawn pawn)
        {
            return pawn?.skills?.skills ?? Enumerable.Empty<SkillRecord>();
        }

        // --- Lite ---
        // 输出：
        // Traits: ["Kind","Nimble", ...]
        // Expertise: ["Shooting(Lv:12)", "Cooking(Lv:11)", "Crafting(Lv:10)"]
        public string ToStringLite()
        {
            var traitLabels = GetTraits(_pawn)
            .Select(GetTraitLabel)
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

            var topSkills = GetSkills(_pawn)
            .Where(s => s?.def != null && s.Level >= 10)
            .OrderByDescending(s => s.Level)
            .ThenBy(s => s.def.label)
            .Take(3)
            .Select(s => $"{s.def.label.CapitalizeFirst()}({s.Level})")
            .ToList();

            var jw = new Tool.JsonWriter(256)
            .PropRaw("Traits", BuildJsonArray(traitLabels))
            .PropRaw("Expertise(Lv)", BuildJsonArray(topSkills));

            return jw.Close();
        }

        // --- Full ---
        // 输出：
        // Traits: ["Kind:更乐于助人...","Nimble:身手敏捷...", ...]
        // Skills: ["Shooting(Lv:12)", "Cooking(Lv:8)", ..., 所有技能]
        public string ToStringFull()
        {
            var traitsFull = GetTraits(_pawn)
            .Select(t =>
            {
                var label = GetTraitLabel(t);
                var desc = GetTraitDescription(t);
                if (string.IsNullOrEmpty(label)) label = "Unknown";
                if (string.IsNullOrEmpty(desc)) return label;
                return $"{label}:{desc}";
            })
            .ToList();

            var allSkills = GetSkills(_pawn)
            .Where(s => s?.def != null)
            .OrderByDescending(s => s.Level)
            .ThenBy(s => s.def.label)
            .Select(s => $"{s.def.label.CapitalizeFirst()}(Lv:{s.Level})")
            .ToList();

            var jw = new Tool.JsonWriter(512)
            .PropRaw("Traits", BuildJsonArray(traitsFull))
            .PropRaw("Skills", BuildJsonArray(allSkills));

            return jw.Close();
        }
    }
}
