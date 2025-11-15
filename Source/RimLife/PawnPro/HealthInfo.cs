using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimLife
{
    // Pawn 健康信息
    public readonly struct HealthInfo
    {
        public HealthInfo(Pawn pawn, HashSet<string> specialBodyPartDefNames = null)
        {
            bodyParts = GetBodyPartsFull(pawn, specialBodyPartDefNames);
            overallStatus = GetOverallHealthStatus(pawn);
            HasImplant = bodyParts.Any(p => p.IsImplant);
            HasDamage = bodyParts.Any(p => p.IsMissing || p.IsInjured || p.IsDiseased);
            HasOther = bodyParts.Any(p => p.HasOther);
        }

        // JSON 输出（紧凑），例如：{"status":"Stable","parts":{"右腿":"木腿,HP:100%"}}
        public string ToStringFull()
        {
            var jw = new Tool.JsonWriter(192).Prop("status", overallStatus.ToString());
            var parts = GetAffectedParts();
            if (parts.Count >0)
            {
                var partsObj = new Tool.JsonWriter(256);
                foreach (var part in parts)
                {
                    // 汇总状态
                    string statuses = (part.HediffDetails != null && part.HediffDetails.Count >0)
                        ? string.Join("、", part.HediffDetails)
                        : string.Empty;

                    int hpPct = part.MaxHealth >0 ? (int)Math.Round((part.Health / part.MaxHealth) *100f) :0;
                    string value = $"{statuses},HP:{hpPct}%({(int)part.Health}/{(int)part.MaxHealth})";

                    partsObj = partsObj.Prop(part.Name, value);
                }
                jw = jw.PropRaw("parts", partsObj.Close());
            }
            return jw.Close();
        }

        private List<BodyPart> GetAffectedParts()
        {
            return bodyParts.Where(p => p.IsMissing || p.IsInjured || p.IsDiseased || p.IsImplant || p.HasOther).ToList();
        }

        public enum HealthStatus
        {
            Healthy,
            Stable,
            Poor,
            Critical,
            Dead
        }

        struct BodyPart
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

        List<BodyPart> GetBodyPartsFull(Pawn pawn, HashSet<string> specialBodyPartDefNames)
        {
            var parts = new List<BodyPart>();
            var hediffSet = pawn.health.hediffSet;

            // First, check for parts with hediffs
            foreach (var hediff in hediffSet.hediffs.Where(h => h.Part != null && h.Visible))
            {
                var partRecord = hediff.Part;
                if (parts.Any(p => p.Name == partRecord.LabelCap)) continue; // Already processed

                var bodyPart = CreateBodyPart(partRecord, hediffSet);

                parts.Add(bodyPart);
            }

            // Also check for missing parts that might not have a visible hediff
            foreach (var partRecord in pawn.RaceProps.body.AllParts)
            {
                if (hediffSet.PartIsMissing(partRecord) && !parts.Any(p => p.Name == partRecord.LabelCap))
                {
                    var bodyPart = CreateBodyPart(partRecord, hediffSet);
                    parts.Add(bodyPart);
                }
            }

            // Add whole-body hediffs under a special "Whole Body" part
            var wholeBodyHediffs = hediffSet.hediffs.Where(h => h.Part == null && h.Visible).ToList();
            if (wholeBodyHediffs.Any())
            {
                var wholeBodyPart = new BodyPart
                {
                    Name = "Whole Body", // Using a generic name for whole-body effects
                    Health = pawn.health.summaryHealth.SummaryHealthPercent * 100,
                    MaxHealth = 100,
                    IsMissing = false,
                    IsInjured = false,
                    IsDiseased = wholeBodyHediffs.Any(h => h.def.isBad),
                    IsImplant = false,
                    HasOther = true, // Classify them as 'Other'
                    HediffDetails = new List<string>()
                };
                wholeBodyPart.HediffDetails.AddRange(wholeBodyHediffs.Select(h => h.LabelCap));
                parts.Add(wholeBodyPart);
            }

            // 添加健康但需要显示的特殊部件
            if (specialBodyPartDefNames != null && specialBodyPartDefNames.Count >0)
            {
                foreach (var partRecord in pawn.RaceProps.body.AllParts)
                {
                    string defName = partRecord.def.defName;
                    if (!specialBodyPartDefNames.Contains(defName)) continue;

                    int idx = parts.FindIndex(p => p.Name == partRecord.LabelCap);
                    if (idx >=0)
                    {
                        var bp = parts[idx];
                        parts[idx] = bp;
                    }
                    else
                    {
                        var bodyPart = CreateBodyPart(partRecord, hediffSet);
                        parts.Add(bodyPart);
                    }
                }
            }

            return parts;
        }

        private BodyPart CreateBodyPart(BodyPartRecord partRecord, HediffSet hediffSet)
        {
            var partHediffs = hediffSet.hediffs.Where(h => h.Part == partRecord).ToList();

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
                var missingHediff = hediffSet.hediffs.FirstOrDefault(h => h.Part == partRecord && h is Hediff_MissingPart);
                bodyPart.HediffDetails.Add(missingHediff?.Label ?? "Missing");
            }

            var injuries = partHediffs.OfType<Hediff_Injury>().ToList();
            var diseases = partHediffs.Where(h => h.def.isBad && (h.def.comps?.Any(c => c is HediffCompProperties_Immunizable) ?? false)).ToList();
            var implants = partHediffs.OfType<Hediff_Implant>().Concat(partHediffs.OfType<Hediff_AddedPart>()).ToList();
            var others = partHediffs.Except(injuries).Except(diseases).Except(implants).Where(h => h.Visible && !(h is Hediff_MissingPart)).ToList();

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

        HealthStatus GetOverallHealthStatus(Pawn pawn)
        {
            var health = pawn.health;
            var hediffSet = health.hediffSet;

            if (health.Dead) return HealthStatus.Dead;
            if (health.Downed) return HealthStatus.Critical;

            float painTotal = hediffSet.PainTotal;
            if (painTotal >0.8f) return HealthStatus.Critical;
            if (painTotal >0.4f) return HealthStatus.Poor;

            float bleedRate = hediffSet.BleedRateTotal;
            if (bleedRate >1.0f) return HealthStatus.Critical;
            if (bleedRate >0.3f) return HealthStatus.Poor;

            bool hasEmergency = hediffSet.hediffs.Any(
                h => h.Visible &&
                h.def.everCurableByItem &&
                h.Severity >= h.def.maxSeverity *0.8f
            );
            if (hasEmergency) return HealthStatus.Poor;

            bool hasAnyBadHediff = hediffSet.hediffs.Any(h => h.Visible && h.def.isBad);
            if (hasAnyBadHediff) return HealthStatus.Stable;

            return HealthStatus.Healthy;
        }

        public readonly HealthStatus overallStatus;
        public readonly bool HasImplant;
        public readonly bool HasDamage;
        public readonly bool HasOther;
        private readonly List<BodyPart> bodyParts;
    }
}
