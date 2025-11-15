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
		private readonly Pawn Myself;

		public PawnPro(Pawn pawn)
		{
			Myself = pawn;
			_speciesInfo = new SpeciesInfo(pawn);
			_healthInfo = new HealthInfo(pawn);
			_perspective = new Perspective(this);
			_trait = new TraitsInfo(pawn);
			_mood = new Mood(pawn);
			_activity = new Activity(pawn);
		}

		public SpeciesInfo _speciesInfo;

		public HealthInfo _healthInfo;

		public Perspective _perspective;

		public TraitsInfo _trait;

		public Mood _mood;

		private Activity _activity;

		// 姓名、称谓、年龄等采用动态读取，避免过期
		public string PawnID => Myself.ThingID;
		public string Epithet => Myself?.Name?.ToStringShort ?? Myself?.LabelShort ?? "Unknown";
		public string FullName => Myself?.Name?.ToStringFull ?? Myself?.LabelCap ?? "Unknown";
		public int Age => Myself?.ageTracker?.AgeBiologicalYears ?? 0;
		public string LifeStage => GetLifeStageAtAge(Myself, Age)?.def?.label;

		// Structured action object (JSON), computed by Activity helper
		public string Action => _activity?.ToStringFull() ?? "{}";
		public System.Collections.Generic.IEnumerable<string> ActionQueue => _activity?.ActionQueue ?? System.Linq.Enumerable.Empty<string>();

		public Pawn GetPawn { get { return Myself; } }

		public Faction GetFaction { get { return Myself?.Faction; } }

		// 获取与另一个 Pawn 的关系
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

				// Step1: Check for the most important direct or family relationship
				PawnRelationDef mostImportantRelation = Myself.GetMostImportantRelation(otherPawn);
				if (mostImportantRelation != null)
				{
					label = mostImportantRelation.GetGenderSpecificLabelCap(otherPawn);
				}

				// Step2: If no family relation, check for an overriding status
				if (string.IsNullOrEmpty(label))
				{
					// Master relationship
					if ((Myself.IsPrisoner || Myself.IsSlave) && otherPawn.IsFreeNonSlaveColonist)
					{
						label = "Master".Translate();
					}
					// Prisoner or slave labels
					else if (otherPawn.IsPrisoner)
					{
						label = "Prisoner".Translate();
					}
					else if (otherPawn.IsSlave)
					{
						label = "Slave".Translate();
					}
					// Hostile relationship
					else if (Myself.Faction != null && otherPawn.Faction != null && Myself.Faction.HostileTo(otherPawn.Faction))
					{
						label = "Enemy".Translate();
					}
				}

				// Step3: If no other label found, fall back to opinion-based relationship
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
					string pawnName = otherPawn.LabelShort;
					string opinion = opinionValue.ToStringWithSign();
					return $"{label}(Favorability:{opinion})";
				}
			}
			catch (System.Exception)
			{
				// Skip if opinion calculation fails
			}

			return "";
		}

		// 获取与另一个派系的关系
		public string GetCampRelation(Faction otherFaction = null)
		{
			// 空 -> 默认玩家派系
			otherFaction ??= Faction.OfPlayer;

			//其他派系直接计算
			return ComputeRelationUnsafe(otherFaction).ToString();
		}

		private PawnRelation ComputeRelationUnsafe(Faction otherFaction)
		{
			if (Myself == null) return PawnRelation.Other;

			// 没有派系（例如野生动物）
			if (Myself.Faction == null)
			{
				return PawnRelation.Neutral;
			}

			// 自身派系
			if (Myself.Faction == otherFaction)
			{
				return PawnRelation.OurParty;
			}

			// 查询与其他派系的关系
			var relation = Myself.Faction.RelationWith(otherFaction);
			if (relation == null)
			{
				return PawnRelation.Neutral; // 默认中立
			}

			switch (relation.kind)
			{
				case FactionRelationKind.Hostile: return PawnRelation.Enemy;
				case FactionRelationKind.Ally: return PawnRelation.Ally;
				case FactionRelationKind.Neutral: return PawnRelation.Neutral;
				default: return PawnRelation.Other;
			}
		}

		// 获取特定年龄对应的生命阶段
		static LifeStageAge GetLifeStageAtAge(Pawn pawn, float age)
		{
			if (pawn?.RaceProps?.lifeStageAges == null) return null;
			return pawn.RaceProps.lifeStageAges.FindLast(stage => stage.minAge <= age);
		}

		private string GetGenderText()
		{
			return Myself.gender.ToString();
		}

		public string ToStringCore()
		{
			return $"{BaseInfo()},{_activity.ToStringLite()}";
		}

		public string ToStringLite()
		{
			var jw = new Tool.JsonWriter(256)
				.Prop("ID", PawnID)
				.Prop("PawnInfo", BaseInfo())
				.PropRaw("Childhood", Myself.story.Childhood?.title ?? string.Empty)
				.PropRaw("Adulthood", Myself.story.Adulthood?.title ?? string.Empty)
				.PropRaw("Traits", _trait.ToStringLite())
				.PropRaw("Action", _activity.ToStringLite())
				.PropRaw("Mood", _mood.ToStringLite())
				.PropRaw("Equipment", BuildEquipmentLite())
				.Prop("HealthStatus", _healthInfo.overallStatus.ToString());
			return jw.Close();
		}

		public string ToStringFull()
		{
			var childhoodExperiences = new Tool.JsonWriter(128)
				.Prop("Title", Myself.story.Childhood?.title ?? string.Empty)
				.Prop("description", Myself.story.Childhood?.description ?? string.Empty);

			var adulthoodExperiences = new Tool.JsonWriter(512)
				.Prop("Title", Myself.story.Adulthood?.title ?? string.Empty)
				.Prop("description", Myself.story.Adulthood?.description ?? string.Empty);

			var jw = new Tool.JsonWriter(1024)
				.Prop("ID", PawnID)
				.Prop("PawnInfo", BaseInfo())
				.Prop("FullName", FullName)
				.PropRaw("Childhood", childhoodExperiences.Close())
				.PropRaw("Adulthood", adulthoodExperiences.Close())
				.PropRaw("Traits", _trait.ToStringFull())
				.Prop("Stage", LifeStage ?? string.Empty)
				.PropRaw("Mood", _mood.ToStringFull())
				.PropRaw("Species", _speciesInfo.ToStringFull())
				.PropRaw("Action", _activity.ToStringFull())
				.Prop("ActionQueueSize", ActionQueue.Any() ? $"+{ActionQueue.Count()}" : string.Empty)
				.PropRaw("Equipment", BuildEquipmentFull())
				.PropRaw("Health", _healthInfo.ToStringFull())
				.PropRaw("Perspective", _perspective.ToStringFull());

			return jw.Close();
		}

		private string MyFactionText()
		{
			string factionText = "None";
			if (Myself?.Faction != null)
			{
				string name = Myself.Faction.Name;
				if (string.IsNullOrEmpty(name)) name = Myself.Faction.def?.label ?? Myself.Faction.ToString();
				factionText = name;
			}
			return factionText;
		}

		public bool IsVisitor(Pawn pawn)
		{
			return pawn?.Faction != null && pawn.Faction != Myself.Faction && !pawn.HostileTo(Myself.Faction);
		}

		public bool IsEnemy(Pawn pawn)
		{
			return pawn != null && pawn.HostileTo(Myself.Faction);
		}

		string BaseInfo()
		{
			var sb = new StringBuilder(128);
			sb.Append(Epithet)
				.Append(" (")
				.Append(Age)
				.Append(" years old, ")
				.Append(MyFactionText())
				.Append("'s ")
				.Append(_speciesInfo.SpeciesName)
				.Append(GetGenderText())
				.Append(",")
				.Append(GetCampRelation())
				.Append(")");
			return sb.ToString();
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

		// --- Equipment helpers ---
		private static string Escape(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		private string BuildArray(System.Collections.Generic.IEnumerable<string> values)
		{
			var sb = new StringBuilder(128);
			sb.Append("[");
			bool first = true;
			foreach (var v in values)
			{
				if (!first) sb.Append(",");
				sb.Append("\"").Append(Escape(v)).Append("\"");
				first = false;
			}
			sb.Append("]");
			return sb.ToString();
		}

		private string BuildEquipmentLite()
		{
			string weapon = Myself?.equipment?.Primary?.LabelCap ?? string.Empty;
			// Inventory items: Name xCount
			var inv = Myself?.inventory?.innerContainer?
				.Where(t => t != null && t.stackCount > 0)
				.GroupBy(t => t.LabelCap)
				.Select(g => $"{g.Key} x{g.Sum(t => t.stackCount)}") ?? System.Linq.Enumerable.Empty<string>();

			var jw = new Tool.JsonWriter(256)
				.Prop("Weapon", weapon)
				.PropRaw("Inventory", BuildArray(inv));

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
			var inv = Myself?.inventory?.innerContainer?
				.Where(t => t != null && t.stackCount > 0)
				.GroupBy(t => t.LabelCap)
				.Select(g => $"{g.Key} x{g.Sum(t => t.stackCount)}") ?? System.Linq.Enumerable.Empty<string>();
			var apparel = Myself?.apparel?.WornApparel?
				.Where(a => a != null)
				.Select(a => a.LabelCap) ?? System.Linq.Enumerable.Empty<string>();

			var jw = new Tool.JsonWriter(384)
				.Prop("Weapon", weapon)
				.PropRaw("Inventory", BuildArray(inv))
				.PropRaw("Apparel", BuildArray(apparel));

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
	}
}
