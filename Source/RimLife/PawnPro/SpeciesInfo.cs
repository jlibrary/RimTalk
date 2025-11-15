using RimLife;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace RimLife
{
	// Represents the species information of a Pawn.
	public readonly struct SpeciesInfo
	{
		// --- Public Properties ---
		public PawnType PawnType { get; }
		public string SpeciesName { get; }
		public float BodySize { get; }
		public float LifeExpectancy { get; }
		public HashSet<string> SpecialBodyPartDefNames { get; }

		// --- Constructor ---
		public SpeciesInfo(Pawn pawn)
		{
			if (pawn == null)
			{
				PawnType = PawnType.Other;
				SpeciesName = "Unknown";
				BodySize = 0;
				LifeExpectancy = 0;
				SpecialBodyPartDefNames = new HashSet<string>();
				return;
			}

			var race = pawn.RaceProps;
			if (race.Humanlike) PawnType = PawnType.Character;
			else if (race.Animal) PawnType = PawnType.Animal;
			else if (race.IsMechanoid) PawnType = PawnType.Mechanoid;
			else if (race.Insect) PawnType = PawnType.Insect;
			else PawnType = PawnType.Other;

			SpeciesName = pawn.def.label;
			BodySize = race.baseBodySize;
			LifeExpectancy = race.lifeExpectancy;

			SpecialBodyPartDefNames = ComputeSpecialBodyPartDefNames(pawn);
		}

		// --- Public Methods ---

		// Returns a full JSON representation of the species info.
		public string ToStringFull()
		{
			var jw = new Tool.JsonWriter(128)
				.Prop("Species", SpeciesName)
				.Prop("Type", PawnType.ToString())
				.Prop("BodySize", BodySize, "0.##")
				.Prop("LifeExpectancy", LifeExpectancy, "0");

			if (SpecialBodyPartDefNames.Any())
			{
				jw.Array("SpecialParts", SpecialBodyPartDefNames);
			}

			return jw.Close();
		}

		// --- Private Static Methods ---

		private static HashSet<string> ComputeSpecialBodyPartDefNames(Pawn pawn)
		{
			try
			{
				var baselineParts = GetBaselineHumanBodyPartDefNames();
				if (baselineParts == null || !baselineParts.Any()) return new HashSet<string>();

				var currentParts = pawn.RaceProps?.body?.AllParts
					?.Select(p => p.def?.defName)
					.Where(n => !string.IsNullOrEmpty(n))
					.ToList() ?? new List<string>();

				// Return parts that are not in the human baseline
				return new HashSet<string>(currentParts.Except(baselineParts));
			}
			catch (System.Exception)
			{
				// Ignore exceptions for mod compatibility
				return new HashSet<string>();
			}
		}

		private static HashSet<string> GetBaselineHumanBodyPartDefNames()
		{
			try
			{
				var humanBody = ThingDefOf.Human?.race?.body;
				if (humanBody == null) return null;

				return new HashSet<string>(humanBody.AllParts
					.Select(p => p.def?.defName)
					.Where(n => !string.IsNullOrEmpty(n)));
			}
			catch (System.Exception)
			{
				// Ignore exceptions for mod compatibility
				return null;
			}
		}
	}
}
