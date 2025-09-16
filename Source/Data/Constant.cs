using Verse;

namespace RimTalk.Data
{
    public static class Constant
    {
        public const string ModTag = "[RimTalk]";
        public const string DefaultCloudModel = "gemma-3-27b-it";
        public const string FallbackCloudModel = "gemma-3-12b-it";
        public const string ChooseModel = "(choose model)";

        public static readonly string Lang = LanguageDatabase.activeLanguage.info.friendlyNameNative;

        public static readonly string DefaultInstruction =
            $@"Role-play RimWorld character per profile.

Portrayal:
Age/personality appropriate speech
Skills: max 20, mood: 0-100
Young: impulsive, respect elders
Old: wise, formal

For non-colonist:
Prisoner: In cell. submissive, wary, call colonist ""master"". Speak quietly, short/hesitant, mention confinement, plead or bargain.
Slave: Owned, forced labor. Fearful, obedient, call owner ""master"". Mention work duties, show exhaustion.
Visitor: polite, curious, deferential. Say ""visiting/just passing through"", ask questions, avoid assuming colony knowledge.
Invader: aggressive, hostile. In combat use terse commands/threats; no trivial talk.
Combat override: any role => short, urgent survival/command speech.

Rules:
Keep conversation order
Original names, no translation
Keep dialogue short, {Lang} only, 1-2 sentences
Harsh when fighting
Concern for sick/mental issues
If no one nearby, ONLY generate monologue";
        
        private const string JsonInstruction = @"

Return JSON array with objects containing ""name"" and ""text"" string keys.";

        // Get the current instruction from settings or fallback to default, always append JSON instruction
        public static string Instruction =>
            (string.IsNullOrWhiteSpace(Settings.Get().CustomInstruction)
                ? DefaultInstruction
                : Settings.Get().CustomInstruction) + JsonInstruction;

        public const string Prompt =
            "Act based on context. Be natural. no repetition.";

        public static readonly string PersonaGenInstruction =
            $@"Create a unique persona (to be used as conversation style) in {Lang}. Must be short in 1 sentence.
Include: how they speak, their main attitude, and one weird quirk that makes them memorable.
Be specific and bold, avoid boring traits.
Also determine chattiness: 0.1-0.5 (quiet), 0.6-1.4 (normal), 1.5-2.0 (chatty).
Return JSON with fields 'persona' (string) and 'chattiness' (float)";
        
        public static readonly PersonalityData[] Personalities =
        {
            new PersonalityData { Persona ="RimTalk.Persona.CheerfulHelper".Translate(), Chattiness =1.5f },
            new PersonalityData { Persona ="RimTalk.Persona.CynicalRealist".Translate(), Chattiness =0.8f },
            new PersonalityData { Persona ="RimTalk.Persona.ShyThinker".Translate(), Chattiness =0.3f },
            new PersonalityData { Persona ="RimTalk.Persona.Hothead".Translate(), Chattiness =1.2f },
            new PersonalityData { Persona ="RimTalk.Persona.Philosopher".Translate(), Chattiness =1.6f },
            new PersonalityData { Persona ="RimTalk.Persona.DarkHumorist".Translate(), Chattiness =1.4f },
            new PersonalityData { Persona ="RimTalk.Persona.Caregiver".Translate(), Chattiness =1.5f },
            new PersonalityData { Persona ="RimTalk.Persona.Opportunist".Translate(), Chattiness =1.3f },
            new PersonalityData { Persona ="RimTalk.Persona.OptimisticDreamer".Translate(), Chattiness =1.6f },
            new PersonalityData { Persona ="RimTalk.Persona.Pessimist".Translate(), Chattiness =0.7f },
            new PersonalityData { Persona ="RimTalk.Persona.StoicSoldier".Translate(), Chattiness =0.4f },
            new PersonalityData { Persona ="RimTalk.Persona.FreeSpirit".Translate(), Chattiness =1.7f },
            new PersonalityData { Persona ="RimTalk.Persona.Workaholic".Translate(), Chattiness =0.5f },
            new PersonalityData { Persona ="RimTalk.Persona.Slacker".Translate(), Chattiness =1.1f },
            new PersonalityData { Persona ="RimTalk.Persona.NobleIdealist".Translate(), Chattiness =1.5f },
            new PersonalityData { Persona ="RimTalk.Persona.StreetwiseSurvivor".Translate(), Chattiness =1.0f },
            new PersonalityData { Persona ="RimTalk.Persona.Scholar".Translate(), Chattiness =1.6f },
            new PersonalityData { Persona ="RimTalk.Persona.Jokester".Translate(), Chattiness =1.8f },
            new PersonalityData { Persona ="RimTalk.Persona.MelancholicPoet".Translate(), Chattiness =0.4f },
            new PersonalityData { Persona ="RimTalk.Persona.Paranoid".Translate(), Chattiness =0.6f },
            new PersonalityData { Persona ="RimTalk.Persona.Commander".Translate(), Chattiness =1.0f },
            new PersonalityData { Persona ="RimTalk.Persona.Coward".Translate(), Chattiness =0.7f },
            new PersonalityData { Persona ="RimTalk.Persona.ArrogantNoble".Translate(), Chattiness =1.4f },
            new PersonalityData { Persona ="RimTalk.Persona.LoyalCompanion".Translate(), Chattiness =1.3f },
            new PersonalityData { Persona ="RimTalk.Persona.CuriousExplorer".Translate(), Chattiness =1.7f },
            new PersonalityData { Persona ="RimTalk.Persona.ColdRationalist".Translate(), Chattiness =0.3f },
            new PersonalityData { Persona ="RimTalk.Persona.FlirtatiousCharmer".Translate(), Chattiness =1.9f },
            new PersonalityData { Persona ="RimTalk.Persona.BitterOutcast".Translate(), Chattiness =0.5f },
            new PersonalityData { Persona ="RimTalk.Persona.Zealot".Translate(), Chattiness =1.8f },
            new PersonalityData { Persona ="RimTalk.Persona.Trickster".Translate(), Chattiness =1.6f },
            new PersonalityData { Persona ="RimTalk.Persona.DeadpanRealist".Translate(), Chattiness =0.6f },
            new PersonalityData { Persona ="RimTalk.Persona.ChildAtHeart".Translate(), Chattiness =1.7f },
            new PersonalityData { Persona ="RimTalk.Persona.SkepticalScientist".Translate(), Chattiness =1.2f },
            new PersonalityData { Persona ="RimTalk.Persona.Martyr".Translate(), Chattiness =1.3f },
            new PersonalityData { Persona ="RimTalk.Persona.Manipulator".Translate(), Chattiness =1.5f },
            new PersonalityData { Persona ="RimTalk.Persona.Rebel".Translate(), Chattiness =1.4f },
            new PersonalityData { Persona ="RimTalk.Persona.Oddball".Translate(), Chattiness =1.2f },
            new PersonalityData { Persona ="RimTalk.Persona.GreedyMerchant".Translate(), Chattiness =1.7f },
            new PersonalityData { Persona ="RimTalk.Persona.Romantic".Translate(), Chattiness =1.6f },
            new PersonalityData { Persona ="RimTalk.Persona.BattleManiac".Translate(), Chattiness =0.8f },
            new PersonalityData { Persona ="RimTalk.Persona.GrumpyElder".Translate(), Chattiness =1.0f },
            new PersonalityData { Persona ="RimTalk.Persona.AmbitiousClimber".Translate(), Chattiness =1.5f },
            new PersonalityData { Persona ="RimTalk.Persona.Mediator".Translate(), Chattiness =1.4f },
            new PersonalityData { Persona ="RimTalk.Persona.Gambler".Translate(), Chattiness =1.5f },
            new PersonalityData { Persona ="RimTalk.Persona.ArtisticSoul".Translate(), Chattiness =0.9f },
            new PersonalityData { Persona ="RimTalk.Persona.Drifter".Translate(), Chattiness =0.6f },
            new PersonalityData { Persona ="RimTalk.Persona.Perfectionist".Translate(), Chattiness =0.8f },
            new PersonalityData { Persona ="RimTalk.Persona.Vengeful".Translate(), Chattiness =0.7f }
        };
    }
}