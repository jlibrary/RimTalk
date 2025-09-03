using System;
using Verse;

namespace RimTalk.Data
{
    public static class Constant
    {
        private static readonly Random _random = new Random();
        public const int MaxContentLength = 8192;
        public const int MaxLength = 150;
        public const string DefaultCloudModel = "gemma-3-27b-it";
        
        public static readonly string Lang = LanguageDatabase.activeLanguage.info.friendlyNameNative;

        public static readonly string DefaultInstruction =
            $@"Role-play RimWorld character per profile.

Portrayal:
Age/personality appropriate speech
Skills: max 20, mood: 0-100
Young: impulsive, respect elders
Old: wise, formal
Avoid repetition, improvise

Rules:
Keep conversation order
Original names, no translation
Keep dialogue short, {Lang} only, 1-2 sentences
Harsh when fighting
Concern for sick/mental issues
If no one nearby, monologue";
        
        private const string JsonInstruction = @"

Return JSON array with objects containing ""name"" and ""text"" string keys.";
        
        // Get the current instruction from settings or fallback to default, always append JSON instruction
        public static string Instruction =>
            (string.IsNullOrWhiteSpace(Settings.Get().customInstruction)
                ? DefaultInstruction
                : Settings.Get().customInstruction) + JsonInstruction;

        public static readonly string[] Personalities =
        {
            "RimTalk.Personalities.CheerfulHelper".Translate(),
            "RimTalk.Personalities.CynicalRealist".Translate(),
            "RimTalk.Personalities.ShyThinker".Translate(),
            "RimTalk.Personalities.Hothead".Translate(),
            "RimTalk.Personalities.Philosopher".Translate(),
            "RimTalk.Personalities.DarkHumorist".Translate(),
            "RimTalk.Personalities.Caregiver".Translate(),
            "RimTalk.Personalities.Opportunist".Translate(),
            "RimTalk.Personalities.OptimisticDreamer".Translate(),
            "RimTalk.Personalities.Pessimist".Translate(),
            "RimTalk.Personalities.StoicSoldier".Translate(),
            "RimTalk.Personalities.FreeSpirit".Translate(),
            "RimTalk.Personalities.Workaholic".Translate(),
            "RimTalk.Personalities.Slacker".Translate(),
            "RimTalk.Personalities.NobleIdealist".Translate(),
            "RimTalk.Personalities.StreetwiseSurvivor".Translate(),
            "RimTalk.Personalities.Scholar".Translate(),
            "RimTalk.Personalities.Jokester".Translate(),
            "RimTalk.Personalities.MelancholicPoet".Translate(),
            "RimTalk.Personalities.Paranoid".Translate(),
            "RimTalk.Personalities.Commander".Translate(),
            "RimTalk.Personalities.Coward".Translate(),
            "RimTalk.Personalities.ArrogantNoble".Translate(),
            "RimTalk.Personalities.LoyalCompanion".Translate(),
            "RimTalk.Personalities.CuriousExplorer".Translate(),
            "RimTalk.Personalities.ColdRationalist".Translate(),
            "RimTalk.Personalities.FlirtatiousCharmer".Translate(),
            "RimTalk.Personalities.BitterOutcast".Translate(),
            "RimTalk.Personalities.Zealot".Translate(),
            "RimTalk.Personalities.Trickster".Translate(),
            "RimTalk.Personalities.DeadpanRealist".Translate(),
            "RimTalk.Personalities.ChildAtHeart".Translate(),
            "RimTalk.Personalities.SkepticalScientist".Translate(),
            "RimTalk.Personalities.Martyr".Translate(),
            "RimTalk.Personalities.Manipulator".Translate(),
            "RimTalk.Personalities.Rebel".Translate(),
            "RimTalk.Personalities.Oddball".Translate(),
            "RimTalk.Personalities.GreedyMerchant".Translate(),
            "RimTalk.Personalities.Romantic".Translate(),
            "RimTalk.Personalities.BattleManiac".Translate(),
            "RimTalk.Personalities.GrumpyElder".Translate(),
            "RimTalk.Personalities.AmbitiousClimber".Translate(),
            "RimTalk.Personalities.Mediator".Translate(),
            "RimTalk.Personalities.Gambler".Translate(),
            "RimTalk.Personalities.ArtisticSoul".Translate(),
            "RimTalk.Personalities.Drifter".Translate(),
            "RimTalk.Personalities.Perfectionist".Translate(),
            "RimTalk.Personalities.Vengeful".Translate()
        };
    }
}