using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimLife
{
	// Represents what a Pawn can currently perceive in its environment.
	public readonly struct PerspectiveInfo
	{
		// --- Constants ---
		private const float RecognizableRange = 13f; // Distance for detailed recognition
		private const float VisualRange = 26f;       // General line-of-sight distance

		// --- Public Readonly Fields ---
		public readonly List<Building> ContainedBuildings;
		public readonly List<string> VisiblePawnIDs;
		public readonly List<string> RoommateIDs;
		public readonly List<string> RecognizablePawnIDs;

		// --- Private Readonly Fields ---
		private readonly Pawn _myselfPawn;

		// --- Constructor ---
		public PerspectiveInfo(Pawn pawn)
		{
            _myselfPawn = pawn;
			var map = pawn.Map;

			if (map == null)
			{
				ContainedBuildings = new List<Building>();
				VisiblePawnIDs = new List<string>();
				RoommateIDs = new List<string>();
				RecognizablePawnIDs = new List<string>();
				return;
			}

			var room = pawn.GetRoom();

			// Determine buildings in view
			if (room != null && !room.PsychologicallyOutdoors)
			{
				ContainedBuildings = [.. room.ContainedThings<Building>()];
            }
			else
			{
				ContainedBuildings = map.listerThings.AllThings
					.OfType<Building>()
					.Where(b => b.Position.InHorDistOf(pawn.Position, VisualRange))
					.ToList();
			}

			// Find all pawns on the map
			var allPawns = map.mapPawns.AllPawnsSpawned.Where(p => p != pawn).ToList();

			VisiblePawnIDs = allPawns
				.Where(p => GenSight.LineOfSight(pawn.Position, p.Position, map, true))
				.Select(p => p.ThingID)
				.ToList();

			RoommateIDs = room?.ContainedAndAdjacentThings
				.OfType<Pawn>()
				.Where(p => p != pawn)
				.Select(p => p.ThingID)
				.ToList() ?? new List<string>();

			RecognizablePawnIDs = allPawns
				.Where(p => p.Position.InHorDistOf(pawn.Position, RecognizableRange))
				.Select(p => p.ThingID)
				.ToList();
		}

		// --- Public Methods ---

		public string ToStringFull()
		{
            var localMyselfPawnPro = new PawnPro(_myselfPawn);
			var jw = new Tool.JsonWriter(256);

			// Buildings: Grouped by name and count
			var buildingCounts = GroupAndCount(ContainedBuildings, b => b.LabelCap);
			if (buildingCounts.Any())
			{
				jw.Array("Buildings", buildingCounts.Select(kv => $"{kv.Key} x{kv.Value}"));
			}

			// Visible Pawns (not recognizable): Grouped by species
			var nonRecognizablePawns = VisiblePawnIDs.Except(RecognizablePawnIDs ?? Enumerable.Empty<string>())
				.Select(p => Tool.GetPawn(p))
				.Where(p => p != null);

			var speciesCounts = GroupAndCount(nonRecognizablePawns, p => GetApproxSpeciesName(p));
			if (speciesCounts.Any())
			{
				jw.Array("VisiblePawns", speciesCounts.Select(kv => $"{kv.Key} x{kv.Value}"));
			}

			// Recognizable Pawns: Detailed info
			var recognizablePawnDetails = RecognizablePawnIDs
				.Select(id =>
				{
					Pawn p = Tool.GetPawn(id);
					if (p == null) return null;

					var p_pro = new PawnPro(p);
					var jw2 = new Tool.JsonWriter(128)
						.PropRaw("PawnInfo", p_pro.ToStringCore())
						.Prop("MyOpinion", localMyselfPawnPro.GetPawnRelation(p));
					return jw2.Close();
				})
				.Where(s => !string.IsNullOrEmpty(s))
				.ToList();

			if (recognizablePawnDetails.Any())
			{
				jw.ArrayRaw("RecognizablePawns", recognizablePawnDetails);
			}

			return jw.Close();
		}

		// --- Private Helper Methods ---

		private Dictionary<string, int> GroupAndCount<T>(IEnumerable<T> items, System.Func<T, string> keySelector)
		{
			if (items == null || !items.Any()) return new Dictionary<string, int>();
			return items.GroupBy(keySelector).ToDictionary(g => g.Key, g => g.Count());
		}

		private static string GetApproxSpeciesName(Pawn p)
		{
			return p?.def?.label ?? "Unknown";
		}
	}
}
