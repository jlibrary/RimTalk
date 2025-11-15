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
    // Pawn物种信息
    public readonly struct SpeciesInfo
    {
        public SpeciesInfo(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike)
            {
                PawnType = PawnType.Character;
            }
            else if (pawn.RaceProps.Animal)
            {
                PawnType = PawnType.Animal;
            }
            else if (pawn.RaceProps.IsMechanoid)
            {
                PawnType = PawnType.Mechanoid;
            }
            else if (pawn.RaceProps.Insect)
            {
                PawnType = PawnType.Insect;
            }
            else
            {
                PawnType = PawnType.Other;
            }

            LifeExpectancy = pawn.RaceProps.lifeExpectancy;
            SpeciesName = pawn.def.label;
            BodySize = pawn.RaceProps.baseBodySize;

            SpecialBodyPartDefNames = ComputeSpecialBodyPartDefNames(pawn);
        }

        // JSON 输出
        public string ToStringFull()
        {
            var jw = new Tool.JsonWriter(128)
                .Prop("species", SpeciesName)
                .Prop("type", PawnType.ToString())
                .Prop("bodySize", BodySize, "0.##")
                .Prop("lifeExpectancy", LifeExpectancy, "0");

            if (SpecialBodyPartDefNames.Count > 0)
            {
                jw = jw.Array("specialParts", SpecialBodyPartDefNames);
            }

            return jw.Close();
        }

        public PawnType PawnType { get; }
        public string SpeciesName { get; }
        public float BodySize { get; }
        public float LifeExpectancy { get; }

        public HashSet<string> SpecialBodyPartDefNames { get; }

        private static HashSet<string> ComputeSpecialBodyPartDefNames(Pawn pawn)
        {
            try
            {
                var baseline = GetBaselineBodyPartDefNames(pawn);
                if (baseline != null && baseline.Count > 0)
                {
                    var current = pawn.RaceProps?.body?.AllParts
                        ?.Select(p => p.def?.defName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList() ?? new List<string>();

                    return new HashSet<string>(current.Where(n => !baseline.Contains(n)));
                }
            }
            catch
            {
                // 忽略异常以提升兼容性（其他模组可能影响定义）
            }
            return new HashSet<string>();
        }

        private static HashSet<string> GetBaselineBodyPartDefNames(Pawn pawn)
        {
            try
            {
                if (pawn?.RaceProps?.Humanlike == true && ThingDefOf.Human?.race?.body != null)
                {
                    var humanBody = ThingDefOf.Human.race.body;
                    return new HashSet<string>(humanBody.AllParts
                        .Select(p => p.def?.defName)
                        .Where(n => !string.IsNullOrEmpty(n)));
                }
            }
            catch
            {
                // 忽略异常以提升兼容性
            }
            return null;
        }
    }
}
