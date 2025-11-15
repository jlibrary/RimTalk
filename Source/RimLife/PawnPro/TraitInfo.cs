using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimLife
{
	// Collects and formats a Pawn's traits and skills.
	public class TraitsInfo
	{
		// --- Constructor ---
		public TraitsInfo(Pawn pawn)
		{
			_pawn = pawn;
		}

		// --- Public Methods ---

		// Returns a lite JSON string with top traits and skills.
		public string ToStringLite()
		{
			var traitLabels = GetTraits(_pawn)
				.Select(GetTraitLabel)
				.Where(l => !string.IsNullOrEmpty(l))
				.ToList();

			var topSkills = GetSkills(_pawn)
				.Where(s => s.Level >= 10)
				.OrderByDescending(s => s.Level)
				.ThenBy(s => s.def.label)
				.Take(3)
				.Select(s => $"{s.def.label.CapitalizeFirst()} ({s.Level})")
				.ToList();

			var jw = new Tool.JsonWriter(256)
				.PropRaw("Traits", BuildJsonArray(traitLabels))
				.PropRaw("Expertise", BuildJsonArray(topSkills));

			return jw.Close();
		}

		// Returns a full JSON string with all traits (with descriptions) and skills.
		public string ToStringFull()
		{
			var traitsFull = GetTraits(_pawn)
				.Select(t =>
				{
					string label = GetTraitLabel(t);
					string desc = GetTraitDescription(t);
					return string.IsNullOrEmpty(desc) ? label : $"{label}: {desc}";
				})
				.ToList();

			var allSkills = GetSkills(_pawn)
				.OrderByDescending(s => s.Level)
				.ThenBy(s => s.def.label)
				.Select(s => $"{s.def.label.CapitalizeFirst()} (Lv: {s.Level})")
				.ToList();

			var jw = new Tool.JsonWriter(512)
				.PropRaw("Traits", BuildJsonArray(traitsFull))
				.PropRaw("Skills", BuildJsonArray(allSkills));

			return jw.Close();
		}

		// --- Private Fields ---
		private readonly Pawn _pawn;

		// --- Private Helper Methods ---

		private static IEnumerable<Trait> GetTraits(Pawn pawn)
		{
			return pawn?.story?.traits?.allTraits ?? Enumerable.Empty<Trait>();
		}

		private static IEnumerable<SkillRecord> GetSkills(Pawn pawn)
		{
			return pawn?.skills?.skills ?? Enumerable.Empty<SkillRecord>();
		}

		private static string GetTraitLabel(Trait trait)
		{
			if (trait == null) return "Unknown";
			return trait.LabelCap ?? "Unknown";
		}

		private static string GetTraitDescription(Trait trait)
		{
			if (trait == null) return string.Empty;

			string desc = trait.CurrentData?.description;
			if (string.IsNullOrEmpty(desc)) return string.Empty;

			// Sanitize: remove tags and normalize whitespace
			desc = desc.StripTags().Trim();
			desc = System.Text.RegularExpressions.Regex.Replace(desc, @"\s+", " ");
			return desc;
		}

		// --- Private Static JSON Helpers ---

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
