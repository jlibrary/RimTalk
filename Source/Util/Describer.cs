using System;
using System.Globalization;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace RimTalk.Util;

public static class Describer
{
    public static string Wealth(float wealthTotal)
    {
        return wealthTotal switch
        {
            < 50_000f => "RimTalk.Describer.Wealth.Impecunious".Translate(),
            < 100_000f => "RimTalk.Describer.Wealth.Needy".Translate(),
            < 200_000f => "RimTalk.Describer.Wealth.NoLongerStarving".Translate(),
            < 300_000f => "RimTalk.Describer.Wealth.ModeratelyProsperous".Translate(),
            < 400_000f => "RimTalk.Describer.Wealth.Rich".Translate(),
            < 600_000f => "RimTalk.Describer.Wealth.Luxurious".Translate(),
            < 1_000_000f => "RimTalk.Describer.Wealth.Extravagant".Translate(),
            < 1_500_000f => "RimTalk.Describer.Wealth.TreasuresFillTheHome".Translate(),
            < 2_000_000f => "RimTalk.Describer.Wealth.AsWealthyAsAGlitterworld".Translate(),
            _ => "RimTalk.Describer.Wealth.RichestInTheGalaxy".Translate()
        };
    }

    private static readonly ThoughtDef NeedBeautyThoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("NeedBeauty");

    // Reuses vanilla's Need_Beauty/ThoughtWorker_NeedBeauty pipeline (the same one behind the
    // "ugly environment"/"beautiful environment" mood thoughts) instead of re-averaging cells ourselves.
    public static string Beauty(Pawn pawn)
    {
        var need = pawn?.needs?.TryGetNeed<Need_Beauty>();
        if (need == null) return null;

        // Mirrors ThoughtWorker_NeedBeauty's category-to-stage mapping; Neutral has no stage in vanilla.
        int? stageIndex = need.CurCategory switch
        {
            BeautyCategory.Hideous => 0,
            BeautyCategory.VeryUgly => 1,
            BeautyCategory.Ugly => 2,
            BeautyCategory.Pretty => 3,
            BeautyCategory.VeryPretty => 4,
            BeautyCategory.Beautiful => 5,
            _ => null
        };

        if (stageIndex == null)
            return "RimTalk.Describer.Beauty.Neutral".Translate();

        var stages = NeedBeautyThoughtDef?.stages;
        return stages != null && stageIndex.Value < stages.Count ? stages[stageIndex.Value].label : null;
    }

    // Reuses vanilla's RoomStatDef score-stage labels instead of a mod-defined bucket ladder.
    public static string Cleanliness(float cleanliness)
    {
        return RoomStatDefOf.Cleanliness.GetScoreStage(cleanliness)?.label;
    }

    public static string Resistance(float value)
    {
        return value switch
        {
            <= 0f => "RimTalk.Describer.Resistance.CompletelyBroken".Translate(),
            < 2f => "RimTalk.Describer.Resistance.BarelyResisting".Translate(),
            < 6f => "RimTalk.Describer.Resistance.Weakened".Translate(),
            < 12f => "RimTalk.Describer.Resistance.StrongWilled".Translate(),
            _ => "RimTalk.Describer.Resistance.ExtremelyDefiant".Translate()
        };
    }

    public static string Will(float value)
    {
        return value switch
        {
            <= 0f => "RimTalk.Describer.Will.NoWillLeft".Translate(),
            < 2f => "RimTalk.Describer.Will.WeakWilled".Translate(),
            < 6f => "RimTalk.Describer.Will.ModerateWill".Translate(),
            < 12f => "RimTalk.Describer.Will.StrongWill".Translate(),
            _ => "RimTalk.Describer.Will.Unyielding".Translate()
        };
    }

    public static string Suppression(float value)
    {
        return value switch
        {
            < 20f => "RimTalk.Describer.Suppression.OpenlyRebellious".Translate(),
            < 50f => "RimTalk.Describer.Suppression.Unstable".Translate(),
            < 80f => "RimTalk.Describer.Suppression.GenerallyObedient".Translate(),
            _ => "RimTalk.Describer.Suppression.CompletelyCowed".Translate()
        };
    }

    // Standalone adjective, not a verb phrase - appended after a "Name(Relation)" label.
    public static string Opinion(float value)
    {
        return value switch
        {
            >= 80f => "RimTalk.Describer.Opinion.Adoring".Translate(),
            >= 40f => "RimTalk.Describer.Opinion.Warm".Translate(),
            >= 20f => "RimTalk.Describer.Opinion.Friendly".Translate(),
            > -20f => "RimTalk.Describer.Opinion.Neutral".Translate(),
            >= -40f => "RimTalk.Describer.Opinion.Cold".Translate(),
            >= -80f => "RimTalk.Describer.Opinion.Hostile".Translate(),
            _ => "RimTalk.Describer.Opinion.Loathing".Translate()
        };
    }

    // Remaining hit-point fraction of a Thing (e.g. GenLabel.LabelExtras), not job/task progress.
    public static string Condition(float pctHitPoints)
    {
        return pctHitPoints switch
        {
            >= 95f => "RimTalk.Describer.Condition.Pristine".Translate(),
            >= 75f => "RimTalk.Describer.Condition.Scratched".Translate(),
            >= 50f => "RimTalk.Describer.Condition.Damaged".Translate(),
            >= 25f => "RimTalk.Describer.Condition.BadlyDamaged".Translate(),
            _ => "RimTalk.Describer.Condition.Wrecked".Translate()
        };
    }

    public static string Progress(float pct)
    {
        return pct switch
        {
            <= 0f => "RimTalk.Describer.Progress.NotStarted".Translate(),
            < 25f => "RimTalk.Describer.Progress.JustStarted".Translate(),
            < 75f => "RimTalk.Describer.Progress.Underway".Translate(),
            < 100f => "RimTalk.Describer.Progress.NearlyDone".Translate(),
            _ => "RimTalk.Describer.Progress.Complete".Translate()
        };
    }

    // Matches the "(84%)" / "(masterwork 84%)" hit-point suffix GenLabel.LabelExtras appends to
    // damaged, non-stacked Things - that's remaining health, not job/task progress.
    private static readonly Regex ConditionSuffixPattern =
        new(@"\((?:([^()]*?)\s*)?(\d+(?:\.\d+)?)%\)", RegexOptions.Compiled);

    public static string StripConditionSuffix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        return ConditionSuffixPattern.Replace(text, m =>
        {
            string prefix = m.Groups[1].Value.TrimEnd();
            string word = Condition(float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            return string.IsNullOrEmpty(prefix) ? $"({word})" : $"({prefix} {word})";
        });
    }

    public static string GetLabelShort(this Gender gender)
    {
        return gender switch
        {
            Gender.Male => "M",
            Gender.Female => "F",
            _ => ""
        };
    }
}
