using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimLife
{
	// Pawn视角信息：它现在应该看到什么
	public readonly struct Perspective
	{
		public readonly List<Building> RoomBuildings;
		public readonly List<string> VisiblePawnIDs;
		public readonly List<string> RoommateIDs;
		public readonly List<string> RecognizablePawnIDs;

		private const float RecognizableRange =13f; // 可识别的距离
		private const float VisualRange =26f; //视觉范围

		private readonly PawnPro Myself;

        public Perspective(PawnPro pawn)
        {
            Myself = pawn;

            var room = Myself.GetPawn.GetRoom();
            var map = Myself.GetPawn.Map;

            if (room != null && !room.PsychologicallyOutdoors)
            {
                RoomBuildings = [.. room.ContainedThings<Building>()];
            }
            else if (map != null)
            {
                var myPawn = Myself.GetPawn;
                RoomBuildings = map.listerThings.AllThings
                    .OfType<Building>()
                    .Where(b => b.Position.InHorDistOf(myPawn.Position, VisualRange))
                    .ToList();
            }
            else
            {
                RoomBuildings = new List<Building>();
            }

            if (map == null)
            {
                VisiblePawnIDs = new List<string>();
                RoommateIDs = new List<string>();
                RecognizablePawnIDs = new List<string>();
                return;
            }

            VisiblePawnIDs = map.mapPawns.AllPawnsSpawned
                .Where(p => p != pawn.GetPawn && GenSight.LineOfSight(pawn.GetPawn.Position, p.Position, map, true))
                .Select(p => p.ThingID)
                .ToList();

            RoommateIDs = room?.ContainedAndAdjacentThings
                .OfType<Pawn>()
                .Where(p => p != pawn.GetPawn)
                .Select(p => p.ThingID)
                .ToList() ?? new List<string>();

            RecognizablePawnIDs = map.mapPawns.AllPawnsSpawned
                .Where(p => p != pawn.GetPawn && p.Position.InHorDistOf(pawn.GetPawn.Position, RecognizableRange))
                .Select(p => p.ThingID)
                .ToList();

        }

        public string ToStringFull()
        {
            var jw = new Tool.JsonWriter(256);

            // Buildings -> 数组字符串列表
            var buildingCounts = GroupBuildings();
            if (buildingCounts != null && buildingCounts.Count >0)
            {
                var pairs = buildingCounts.Select(kv => $"{kv.Key} x{kv.Value}");
                jw = jw.Array("Buildings", pairs);
            }

            // VisiblePawns (approx species for non-recognizable) ->统计简化种族名称
            if (VisiblePawnIDs != null && VisiblePawnIDs.Any())
            {
                var distinctVisible = VisiblePawnIDs.Except(RecognizablePawnIDs ?? Enumerable.Empty<string>());
                if (distinctVisible.Any())
                {
                    var speciesCounts = new Dictionary<string, int>();
                    foreach (var id in distinctVisible)
                    {
                        Pawn p = Tool.GetPawn(id);
                        string species = GetApproxSpeciesName(p);
                        if (!speciesCounts.ContainsKey(species)) speciesCounts[species] =0;
                        speciesCounts[species]++;
                    }
                    if (speciesCounts.Count >0)
                    {
                        var pairs2 = speciesCounts.Select(kv => $"{kv.Key} x{kv.Value}");
                        jw = jw.Array("VisiblePawns", pairs2);
                    }
                }
            }

            // Recognizable -> 数组，每个元素:{ "PawnInfo": <lite JSON>, "RelationToMe": "..." }
            if (RecognizablePawnIDs != null && RecognizablePawnIDs.Any())
            {
                var myselfPawnPro = Myself; 
                var arr = RecognizablePawnIDs.Select(id =>
                {
                    Pawn p = Tool.GetPawn(id);
                    PawnPro p_pro = p != null ? new PawnPro(p) : null;

                    var jw2 = new Tool.JsonWriter(128)
                        .PropRaw("PawnInfo", p_pro?.ToStringCore())
                        .Prop("MyOpinion", myselfPawnPro.GetPawnRelation(p));
                    return jw2.Close();
                })
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

                if (arr.Count >0)
                {
                    jw = jw.ArrayRaw("Recognizable", arr);
                }
            }

            return jw.Close();
        }

        private Dictionary<string, int> GroupBuildings()
		{
			if (RoomBuildings == null || RoomBuildings.Count ==0) return null;
			return RoomBuildings.GroupBy(b => b.Label).ToDictionary(g => g.Key, g => g.Count());
		}

		private static string GetApproxSpeciesName(Pawn p)
		{
			return p?.def?.label ?? "Unknown";
		}
	}
}
