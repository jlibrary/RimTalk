using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimLife
{
    // 采集 Pawn 心情信息（动态读取，避免过期）
    public class Mood
    {
        private readonly Pawn _pawn;

        public Mood(Pawn pawn)
        {
            _pawn = pawn;
        }

        // 0-100 的当前心情百分比
        private static int GetMoodPercent(Pawn pawn)
        {
            var m = pawn?.needs?.mood;
            if (m == null) return 0;
            return (int)Math.Round((m.CurLevelPercentage) * 100f);
        }

        private string GetMood()
        {
            return $"{_pawn.needs.mood.MoodString}({_pawn?.needs?.mood.CurInstantLevel})";
        }

        // 聚合心情影响：按 Thought def 分组，求和 MoodOffset，合成 "Label(+N/-N)" 短语
        private static List<string> GetInfluenceStrings(Pawn pawn)
        {
            var result = new List<string>();
            if (pawn == null) return result;

            var thoughts = new List<Thought>();
            pawn.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

            if (thoughts == null || thoughts.Count == 0) return result;

            var grouped = thoughts
                .GroupBy(t => t?.def?.defName ?? t?.def?.label ?? "Unknown")
                .Select(g =>
                {
                    float sum = g.Sum(t => t.MoodOffset());
                    // 选择一个可读标签（优先 LabelCap）
                    var label = g.FirstOrDefault()?.LabelCap ?? g.FirstOrDefault()?.def?.label ?? "Unknown";
                    return (label, sum);
                })
                // 可选排序：绝对值越大越靠前，有利于阅读（非裁剪）
                .OrderByDescending(x => Math.Abs(x.sum))
                .ThenBy(x => x.label);

            foreach (var (label, sum) in grouped)
            {
                // 跳过对心情无贡献的 0 影响（不属于裁剪，语义上“不影响心情”）
                if (Math.Abs(sum) <= 0f) continue;

                int rounded = (int)Math.Round(sum);
                // Verse 自带 ToStringWithSign 扩展，输出 +N/-N
                string signed = rounded.ToStringWithSign();
                result.Add($"{label}({signed})");
            }

            return result;
        }

        // JSON 数组构造（简易转义）
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

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // 仅数值（0-100）
        public string ToStringLite()
        {
            return GetMood();
        }

        // 数值 + 影响数组（合成短语）
        public string ToStringFull()
        {
            int value = GetMoodPercent(_pawn);
            var influences = GetInfluenceStrings(_pawn);

            var jw = new Tool.JsonWriter(256)
                .Prop("Mood", GetMood())
                .PropRaw("Influences", BuildJsonArray(influences));
            return jw.Close();
        }
    }
}
