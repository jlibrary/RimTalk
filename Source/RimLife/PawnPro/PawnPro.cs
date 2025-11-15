using RimWorld;
using System.Linq;
using System.Text;
using Verse;

namespace RimLife
{
	// Pawn 类型
	public enum PawnType
	{
		Character, //角色
		Animal, // 动物
		Mechanoid, //机械
		Insect, // 虫族
		Other //其它
	}

	// Pawn关系
	public enum PawnRelation
	{
		OurParty, // 自己
		Ally, //盟友
		Neutral, // 中立
		Enemy, // 敌人
		Other //其他
	}

	public class PawnPro
	{
		// --- Constructor ---
		public PawnPro(Pawn pawn)
		{
			Myself = pawn;
			_speciesInfo = new SpeciesInfo(pawn);
			_healthInfo = new HealthInfo(pawn);
			_perspective = new PerspectiveInfo(pawn);
			_trait = new TraitsInfo(pawn);
			_mood = new MoodInfo(pawn);
			_activity = new ActivityInfo(pawn);
		}

		// --- Public Properties ---

		// Basic Info (dynamic)
		public string PawnID => Myself.ThingID;
		public string Epithet => Myself?.Name?.ToStringShort ?? Myself?.LabelShort ?? "Unknown";
		public string FullName => Myself?.Name?.ToStringFull ?? Myself?.LabelCap ?? "Unknown";
		public int Age => Myself?.ageTracker?.AgeBiologicalYears ?? 0;
		public string LifeStage => GetLifeStageAtAge(Myself, Age)?.def?.label;

		// Activity Info
		public string Action => _activity?.ToStringFull() ?? "{}";
		public System.Collections.Generic.IEnumerable<string> ActionQueue => _activity?.ActionQueue ?? Enumerable.Empty<string>();

		// Core Objects
		public Pawn Pawn => Myself;
		public Faction Faction => Myself?.Faction;

		// --- Public Methods ---

		// Get relation with another Pawn
		public string GetPawnRelation(Pawn otherPawn)
		{
			if (Myself?.relations == null || otherPawn == null || otherPawn == Myself)
			{
				return "";
			}

			string label = null;
			float opinionValue = 0f;

			try
			{
				opinionValue = Myself.relations.OpinionOf(otherPawn);

				// Step 1: Check for the most important direct or family relationship
				PawnRelationDef mostImportantRelation = Myself.GetMostImportantRelation(otherPawn);
				if (mostImportantRelation != null)
				{
					label = mostImportantRelation.GetGenderSpecificLabelCap(otherPawn);
				}

				// Step 2: If no family relation, check for an overriding status
				if (string.IsNullOrEmpty(label))
				{
					if ((Myself.IsPrisoner || Myself.IsSlave) && otherPawn.IsFreeNonSlaveColonist)
					{
						label = "Master".Translate();
					}
					else if (otherPawn.IsPrisoner)
					{
						label = "Prisoner".Translate();
					}
					else if (otherPawn.IsSlave)
					{
						label = "Slave".Translate();
					}
					else if (Myself.Faction != null && otherPawn.Faction != null && Myself.Faction.HostileTo(otherPawn.Faction))
					{
						label = "Enemy".Translate();
					}
				}

				// Step 3: If no other label found, fall back to opinion-based relationship
				if (string.IsNullOrEmpty(label) && !IsVisitor(otherPawn) && !IsEnemy(otherPawn))
				{
					const float FriendOpinionThreshold = 20f;
					const float RivalOpinionThreshold = -20f;

					if (opinionValue >= FriendOpinionThreshold)
					{
						label = "Friend".Translate();
					}
					else if (opinionValue <= RivalOpinionThreshold)
					{
						label = "Rival".Translate();
					}
					else
					{
						label = "Acquaintance".Translate();
					}
				}

				if (!string.IsNullOrEmpty(label))
				{
					string opinion = opinionValue.ToStringWithSign();
					return $"{label}(Opinion:{opinion})";
				}
			}
			catch (System.Exception)
			{
				// Skip if opinion calculation fails
			}

			return "";
		}

		// Get relation with a faction
		public string GetFactionRelation(Faction otherFaction = null)
		{
			// Default to player faction if null
			otherFaction ??= Faction.OfPlayer;
			return ComputeRelation(otherFaction).ToString();
		}

		public bool IsVisitor(Pawn pawn)
		{
			return pawn?.Faction != null && pawn.Faction != Myself.Faction && !pawn.HostileTo(Myself.Faction);
		}

		public bool IsEnemy(Pawn pawn)
		{
			return pawn != null && pawn.HostileTo(Myself.Faction);
		}

		// --- Serialization Methods ---

		public string ToStringCore()
		{
			return $"{BuildBaseInfo()},{_activity.ToStringLite()}";
		}

		public string ToStringLite()
		{
			var jw = new Tool.JsonWriter(256)
				.Prop("ID", PawnID)
				.Prop("PawnInfo", BuildBaseInfo())
				.PropRaw("Childhood", Myself.story.Childhood?.title ?? string.Empty)
				.PropRaw("Adulthood", Myself.story.Adulthood?.title ?? string.Empty)
				.PropRaw("Traits", _trait.ToStringLite())
				.PropRaw("Action", _activity.ToStringLite())
				.PropRaw("Mood", _mood.ToStringLite())
				.PropRaw("Equipment", BuildEquipmentLite())
				.Prop("HealthStatus", _healthInfo.OverallStatus.ToString());
			return jw.Close();
		}

		public string ToStringFull()
		{
			var childhoodExperiences = new Tool.JsonWriter(128)
				.Prop("Title", Myself.story.Childhood?.title ?? string.Empty)
				.Prop("Description", Myself.story.Childhood?.description ?? string.Empty);

			var adulthoodExperiences = new Tool.JsonWriter(512)
				.Prop("Title", Myself.story.Adulthood?.title ?? string.Empty)
				.Prop("Description", Myself.story.Adulthood?.description ?? string.Empty);

			var jw = new Tool.JsonWriter(1024)
				.Prop("ID", PawnID)
				.Prop("PawnInfo", BuildBaseInfo())
				.Prop("FullName", FullName)
				.PropRaw("Childhood", childhoodExperiences.Close())
				.PropRaw("Adulthood", adulthoodExperiences.Close())
				.PropRaw("Traits", _trait.ToStringFull())
				.Prop("Stage", LifeStage ?? string.Empty)
				.PropRaw("Mood", _mood.ToStringFull())
				.PropRaw("Species", _speciesInfo.ToStringFull())
				.PropRaw("Action", _activity.ToStringFull())
				.Prop("ActionQueueCount", ActionQueue.Any() ? $"+{ActionQueue.Count()}" : string.Empty)
				.PropRaw("Equipment", BuildEquipmentFull())
				.PropRaw("Health", _healthInfo.ToStringFull())
				.PropRaw("Perspective", _perspective.ToStringFull());

			return jw.Close();
		}

		// --- Private Fields ---

		private readonly Pawn Myself;
		private readonly SpeciesInfo _speciesInfo;
		private readonly HealthInfo _healthInfo;
		private readonly PerspectiveInfo _perspective;
		private readonly TraitsInfo _trait;
		private readonly MoodInfo _mood;
		private readonly ActivityInfo _activity;

		// --- Private Methods ---

		private PawnRelation ComputeRelation(Faction otherFaction)
		{
			if (Myself == null) return PawnRelation.Other;

			// No faction (e.g., wild animals)
			if (Myself.Faction == null)
			{
				return PawnRelation.Neutral;
			}

			// Own faction
			if (Myself.Faction == otherFaction)
			{
				return PawnRelation.OurParty;
			}

			// Relation with other factions
			var relation = Myself.Faction.RelationWith(otherFaction);
			if (relation == null)
			{
				return PawnRelation.Neutral; // Default to neutral
			}

			switch (relation.kind)
			{
				case FactionRelationKind.Hostile: return PawnRelation.Enemy;
				case FactionRelationKind.Ally: return PawnRelation.Ally;
				case FactionRelationKind.Neutral: return PawnRelation.Neutral;
				default: return PawnRelation.Other;
			}
		}

		private string BuildBaseInfo()
		{
			var sb = new StringBuilder(128);
			sb.Append(Epithet)
				.Append(" (")
				.Append(Age)
				.Append("yo, ")
				.Append(GetFactionDisplayName())
				.Append("'s ")
				.Append(_speciesInfo.SpeciesName)
				.Append(" ")
				.Append(GetGenderText())
				.Append(", ")
				.Append(GetFactionRelation())
				.Append(")");
			return sb.ToString();
		}

		private string GetFactionDisplayName()
		{
			if (Myself?.Faction == null) return "None";
			string name = Myself.Faction.Name;
			if (string.IsNullOrEmpty(name)) name = Myself.Faction.def?.label ?? Myself.Faction.ToString();
			return name;
		}

		private string GetGenderText()
		{
			return Myself.gender.ToString();
		}

		// --- Equipment Helpers ---

		private string BuildEquipmentLite()
		{
			string weapon = Myself?.equipment?.Primary?.LabelCap ?? string.Empty;
			var inventory = Myself?.inventory?.innerContainer?
				.Where(t => t != null && t.stackCount > 0)
				.GroupBy(t => t.LabelCap)
				.Select(g => $"{g.Key} x{g.Sum(t => t.stackCount)}") ?? Enumerable.Empty<string>();

			var jw = new Tool.JsonWriter(256)
				.Prop("Weapon", weapon)
				.PropRaw("Inventory", BuildJsonArray(inventory));

			// Armor values (CE compatible)
			float blunt = Myself?.GetStatValue(StatDefOf.ArmorRating_Blunt) ?? 0f;
			float sharp = Myself?.GetStatValue(StatDefOf.ArmorRating_Sharp) ?? 0f;

			if (IsCeActive)
			{
				jw.Prop("ArmorBlunt_mmRHA", blunt.ToString("F2"));
				jw.Prop("ArmorSharp_mmRHA", sharp.ToString("F2"));
			}
			else
			{
				jw.Prop("ArmorBlunt", (int)System.Math.Round(blunt * 100)); // convert to percent
				jw.Prop("ArmorSharp", (int)System.Math.Round(sharp * 100));
			}

			return jw.Close();
		}

		private string BuildEquipmentFull()
		{
			string weapon = Myself?.equipment?.Primary?.LabelCap ?? string.Empty;
			var inventory = Myself?.inventory?.innerContainer?
				.Where(t => t != null && t.stackCount > 0)
				.GroupBy(t => t.LabelCap)
				.Select(g => $"{g.Key} x{g.Sum(t => t.stackCount)}") ?? Enumerable.Empty<string>();
			var apparel = Myself?.apparel?.WornApparel?
				.Where(a => a != null)
				.Select(a => a.LabelCap) ?? Enumerable.Empty<string>();

			var jw = new Tool.JsonWriter(384)
				.Prop("Weapon", weapon)
				.PropRaw("Inventory", BuildJsonArray(inventory))
				.PropRaw("Apparel", BuildJsonArray(apparel));

			// Armor values (CE compatible)
			float blunt = Myself?.GetStatValue(StatDefOf.ArmorRating_Blunt) ?? 0f;
			float sharp = Myself?.GetStatValue(StatDefOf.ArmorRating_Sharp) ?? 0f;

			if (IsCeActive)
			{
				jw.Prop("ArmorBlunt_mmRHA", blunt.ToString("F2"));
				jw.Prop("ArmorSharp_mmRHA", sharp.ToString("F2"));
			}
			else
			{
				jw.Prop("ArmorBlunt", (int)System.Math.Round(blunt * 100));
				jw.Prop("ArmorSharp", (int)System.Math.Round(sharp * 100));
			}

			return jw.Close();
		}

		// --- Static Helpers ---

		private static LifeStageAge GetLifeStageAtAge(Pawn pawn, float age)
		{
			if (pawn?.RaceProps?.lifeStageAges == null) return null;
			return pawn.RaceProps.lifeStageAges.FindLast(stage => stage.minAge <= age);
		}

		private static string BuildJsonArray(System.Collections.Generic.IEnumerable<string> values)
		{
			var sb = new StringBuilder(128);
			sb.Append("[");
			bool first = true;
			foreach (var v in values)
			{
				if (!first) sb.Append(",");
				sb.Append("\"").Append(EscapeJson(v)).Append("\"");
				first = false;
			}
			sb.Append("]");
			return sb.ToString();
		}

		private static string EscapeJson(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		// --- CE Compatibility ---
		private static bool? _isCeActive;
		private static bool IsCeActive
		{
			get
			{
				if (_isCeActive == null)
				{
					_isCeActive = ModsConfig.IsActive("ceteam.combatextended");
				}
				return _isCeActive.Value;
			}
		}
	}
}
