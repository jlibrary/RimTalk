using System;
using System.Globalization;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace RimTalk.Util;

public static class Describer
{
    // These feed the LLM prompt, not the UI - kept in English regardless of active game language
    // so they stay consistent with the rest of ContextBuilder's English scaffolding.
    public static string Wealth(float wealthTotal)
    {
        return wealthTotal switch
        {
            < 50_000f => "destitute",
            < 100_000f => "struggling",
            < 200_000f => "modest",
            < 300_000f => "prosperous",
            < 400_000f => "rich",
            < 600_000f => "luxurious",
            < 1_000_000f => "extravagant",
            < 1_500_000f => "opulent",
            < 2_000_000f => "glitterworld-tier",
            _ => "legendary"
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
            return "unremarkable";

        var stages = NeedBeautyThoughtDef?.stages;
        // untranslatedLabel is the stage's raw pre-DefInjection text, captured in ThoughtStage.PostLoad -
        // stays English (or whatever a beauty-tweaking mod authored) regardless of active game language,
        // while still tracking that mod's stage count/thresholds instead of a hardcoded ladder.
        return stages != null && stageIndex.Value < stages.Count ? stages[stageIndex.Value].untranslatedLabel : null;
    }

    // Reuses vanilla's RoomStatDef score-stage labels instead of a mod-defined bucket ladder.
    public static string Cleanliness(float cleanliness)
    {
        // Same untranslatedLabel mechanism as Beauty() above - stays in English while still
        // respecting a mod's custom RoomStatDef.scoreStages thresholds/count.
        return RoomStatDefOf.Cleanliness.GetScoreStage(cleanliness)?.untranslatedLabel;
    }

    public static string Resistance(float value)
    {
        return value switch
        {
            <= 0f => "broken",
            < 2f => "wavering",
            < 6f => "weakened",
            < 12f => "stubborn",
            _ => "defiant"
        };
    }

    public static string Will(float value)
    {
        return value switch
        {
            <= 0f => "broken",
            < 2f => "frail",
            < 6f => "moderate",
            < 12f => "resolute",
            _ => "unyielding"
        };
    }

    public static string Suppression(float value)
    {
        return value switch
        {
            < 20f => "rebellious",
            < 50f => "unruly",
            < 80f => "obedient",
            _ => "subdued"
        };
    }

    // Standalone adjective, not a verb phrase - appended after a "Name(Relation)" label.
    public static string Opinion(float value)
    {
        return value switch
        {
            >= 80f => "adoring",
            >= 40f => "warm",
            >= 20f => "friendly",
            > -20f => "neutral",
            >= -40f => "cold",
            >= -80f => "hostile",
            _ => "loathing"
        };
    }

    // Remaining hit-point fraction of a Thing (e.g. GenLabel.LabelExtras), not job/task progress.
    public static string Condition(float pctHitPoints)
    {
        return pctHitPoints switch
        {
            >= 95f => "pristine",
            >= 75f => "scratched",
            >= 50f => "damaged",
            >= 25f => "badly damaged",
            _ => "wrecked"
        };
    }

    public static string Progress(float pct)
    {
        return pct switch
        {
            <= 0f => "not started",
            < 25f => "just started",
            < 75f => "underway",
            < 100f => "nearly done",
            _ => "complete"
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
