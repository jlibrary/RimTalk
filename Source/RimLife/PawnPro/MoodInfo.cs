using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimLife
{
	// Collects and formats Pawn mood information.
	public class MoodInfo
	{
		// --- Constructor ---
		public MoodInfo(Pawn pawn)
		{
			_pawn = pawn;
		}

		// --- Public Methods ---

		// Returns a simple string of the mood value (0-100).
		public string ToStringLite()
		{
			return GetMoodDescription();
		}

		// Returns a full JSON object with mood value and influencing thoughts.
		public string ToStringFull()
		{
			var influences = GetInfluenceStrings(_pawn);

			var jw = new Tool.JsonWriter(256)
				.Prop("Mood", GetMoodDescription())
				.PropRaw("Influences", BuildJsonArray(influences));
			return jw.Close();
		}

		// --- Private Fields ---
		private readonly Pawn _pawn;

		// --- Private Methods ---

		private string GetMoodDescription()
		{
			var moodNeed = _pawn?.needs?.mood;
			if (moodNeed == null) return "Unknown(0)";
			return $"{moodNeed.MoodString}({moodNeed.CurInstantLevel:F1})";
		}

		// Aggregates mood influences by grouping thoughts and summing their offsets.
		private static List<string> GetInfluenceStrings(Pawn pawn)
		{
			var result = new List<string>();
			if (pawn?.needs?.mood?.thoughts == null) return result;

			var thoughts = new List<Thought>();
			pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);

			if (!thoughts.Any()) return result;

			var groupedThoughts = thoughts
				.GroupBy(t => t.def)
				.Select(g =>
				{
					float totalOffset = g.Sum(t => t.MoodOffset());
					string label = g.First().LabelCap;
					return (label, totalOffset);
				})
				.Where(x => Math.Abs(x.totalOffset) > 0.01f) // Filter out negligible influences
				.OrderByDescending(x => Math.Abs(x.totalOffset))
				.ThenBy(x => x.label);

			foreach (var (label, totalOffset) in groupedThoughts)
			{
				string signedOffset = ((int)Math.Round(totalOffset)).ToStringWithSign();
				result.Add($"{label} ({signedOffset})");
			}

			return result;
		}

		// --- Private Static Helper Methods ---

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
			if (string.IsNullOrEmpty(s)) return string.Empty;
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
	}
}
