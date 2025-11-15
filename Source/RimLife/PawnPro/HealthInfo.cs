using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimLife
{
	// Represents the health information of a Pawn.
	public readonly struct HealthInfo
	{
		// --- Public Enums ---
		public enum HealthStatus
		{
			Healthy,
			Stable,
			Poor,
			Critical,
			Dead
		}

		// --- Constructor ---
		public HealthInfo(Pawn pawn, HashSet<string> specialBodyPartDefNames = null)
		{
			_bodyParts = GetBodyPartsFull(pawn, specialBodyPartDefNames);
			OverallStatus = GetOverallHealthStatus(pawn);
			HasImplant = _bodyParts.Any(p => p.IsImplant);
			HasDamage = _bodyParts.Any(p => p.IsMissing || p.IsInjured || p.IsDiseased);
			HasOther = _bodyParts.Any(p => p.HasOther);
		}

		// --- Public Properties ---
		public readonly HealthStatus OverallStatus;
		public readonly bool HasImplant;
		public readonly bool HasDamage;
		public readonly bool HasOther;

		// --- Public Methods ---

		// JSON output (full), e.g., {"Status":"Stable","Parts":{"Right Leg":"Wooden Leg, HP:100%"}}
		public string ToStringFull()
		{
			var jw = new Tool.JsonWriter(192).Prop("Status", OverallStatus.ToString());
			var affectedParts = GetAffectedParts();

			if (affectedParts.Any())
			{
				var partsObj = new Tool.JsonWriter(256);
				foreach (var part in affectedParts)
				{
					string statuses = (part.HediffDetails != null && part.HediffDetails.Any())
						? string.Join(", ", part.HediffDetails)
						: string.Empty;

					int hpPct = part.MaxHealth > 0 ? (int)Math.Round((part.Health / part.MaxHealth) * 100f) : 0;
					string value = $"{statuses}, HP:{hpPct}% ({(int)part.Health}/{(int)part.MaxHealth})";

					partsObj.Prop(part.Name, value);
				}
				jw.PropRaw("Parts", partsObj.Close());
			}
			return jw.Close();
		}

		// --- Private Structs ---
		private struct BodyPart
		{
			public string Name;
			public float Health;
			public float MaxHealth;
			public bool IsMissing;
			public bool IsInjured;
			public bool IsDiseased;
			public bool IsImplant;
			public bool HasOther;
			public List<string> HediffDetails;
		}

		// --- Private Fields ---
		private readonly List<BodyPart> _bodyParts;

		// --- Private Methods ---

		private List<BodyPart> GetAffectedParts()
		{
			return _bodyParts.Where(p => p.IsMissing || p.IsInjured || p.IsDiseased || p.IsImplant || p.HasOther).ToList();
		}

		private List<BodyPart> GetBodyPartsFull(Pawn pawn, HashSet<string> specialBodyPartDefNames)
		{
			var parts = new List<BodyPart>();
			if (pawn == null) return parts;

			var hediffSet = pawn.health.hediffSet;
			var processedParts = new HashSet<BodyPartRecord>();

			// 1. Process parts with visible hediffs
			foreach (var hediff in hediffSet.hediffs.Where(h => h.Part != null && h.Visible))
			{
				if (processedParts.Contains(hediff.Part)) continue;

				parts.Add(CreateBodyPart(hediff.Part, hediffSet));
				processedParts.Add(hediff.Part);
			}

			// 2. Add missing parts that might not have a visible hediff
			foreach (var partRecord in hediffSet.GetMissingPartsCommonAncestors())
			{
				if (processedParts.Contains(partRecord.Part)) continue;

				parts.Add(CreateBodyPart(partRecord.Part, hediffSet));
				processedParts.Add(partRecord.Part);
			}

			// 3. Add whole-body hediffs under a special "Whole Body" part
			var wholeBodyHediffs = hediffSet.hediffs.Where(h => h.Part == null && h.Visible).ToList();
			if (wholeBodyHediffs.Any())
			{
				parts.Add(new BodyPart
				{
					Name = "Whole Body",
					Health = pawn.health.summaryHealth.SummaryHealthPercent * 100,
					MaxHealth = 100,
					IsDiseased = wholeBodyHediffs.Any(h => h.def.isBad),
					HasOther = true,
					HediffDetails = wholeBodyHediffs.Select(h => h.LabelCap).ToList()
				});
			}

			// 4. Add healthy but explicitly requested special parts
			if (specialBodyPartDefNames != null)
			{
				foreach (var partRecord in pawn.RaceProps.body.AllParts)
				{
					if (specialBodyPartDefNames.Contains(partRecord.def.defName) && !processedParts.Contains(partRecord))
					{
						parts.Add(CreateBodyPart(partRecord, hediffSet));
						processedParts.Add(partRecord);
					}
				}
			}

			return parts;
		}

		private BodyPart CreateBodyPart(BodyPartRecord partRecord, HediffSet hediffSet)
		{
			var partHediffs = hediffSet.hediffs.Where(h => h.Part == partRecord && h.Visible).ToList();

			var bodyPart = new BodyPart
			{
				Name = partRecord.LabelCap,
				Health = hediffSet.GetPartHealth(partRecord),
				MaxHealth = partRecord.def.hitPoints,
				HediffDetails = new List<string>()
			};

			bodyPart.IsMissing = hediffSet.PartIsMissing(partRecord);
			if (bodyPart.IsMissing)
			{
				var missingHediff = partHediffs.FirstOrDefault(h => h is Hediff_MissingPart);
				bodyPart.HediffDetails.Add(missingHediff?.LabelCap ?? "Missing");
			}

			var injuries = partHediffs.OfType<Hediff_Injury>().ToList();
			var diseases = partHediffs.Where(h => h.def.isBad && h.def.HasComp(typeof(HediffComp_Immunizable))).ToList();
			var implants = partHediffs.OfType<Hediff_Implant>().Concat(partHediffs.OfType<Hediff_AddedPart>()).ToList();
			var others = partHediffs.Except(injuries).Except(diseases).Except(implants).Where(h => !(h is Hediff_MissingPart)).ToList();

			bodyPart.IsInjured = injuries.Any();
			bodyPart.IsDiseased = diseases.Any();
			bodyPart.IsImplant = implants.Any();
			bodyPart.HasOther = others.Any();

			bodyPart.HediffDetails.AddRange(injuries.Select(h => h.LabelCap));
			bodyPart.HediffDetails.AddRange(diseases.Select(h => h.LabelCap));
			bodyPart.HediffDetails.AddRange(implants.Select(h => h.LabelCap));
			bodyPart.HediffDetails.AddRange(others.Select(h => h.LabelCap));

			bodyPart.HediffDetails = bodyPart.HediffDetails.Distinct().ToList();

			return bodyPart;
		}

		private HealthStatus GetOverallHealthStatus(Pawn pawn)
		{
			if (pawn == null) return HealthStatus.Dead;

			var health = pawn.health;
			if (health.Dead) return HealthStatus.Dead;
			if (health.Downed) return HealthStatus.Critical;

			var hediffSet = health.hediffSet;
			float painTotal = hediffSet.PainTotal;
			if (painTotal > 0.8f) return HealthStatus.Critical;
			if (painTotal > 0.4f) return HealthStatus.Poor;

			float bleedRate = hediffSet.BleedRateTotal;
			if (bleedRate > 1.0f) return HealthStatus.Critical;
			if (bleedRate > 0.3f) return HealthStatus.Poor;

			bool hasLifeThreateningHediff = hediffSet.hediffs.Any(h => h.Visible && h.CurStage?.lifeThreatening == true);
			if (hasLifeThreateningHediff) return HealthStatus.Critical;

			bool hasBadHediff = hediffSet.hediffs.Any(h => h.Visible && h.def.isBad);
			if (hasBadHediff) return HealthStatus.Stable;

			return HealthStatus.Healthy;
		}
	}
}
