using Verse;

namespace RimTalk.Data
{
    public static class Constant
    {
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
            (string.IsNullOrWhiteSpace(Settings.Get().customInstruction)
                ? DefaultInstruction
                : Settings.Get().customInstruction) + JsonInstruction;

        public const string Prompt =
            "Act based on context: continue conversation with nearby people, show concern for nearby's critical conditions, or start new topic. Be natural, no repetition.";

        public static readonly string PersonaGenInstruction =
            $@"Create a unique persona (to be used as conversation style) in {Lang}. Must be short in 1 sentence.
Include: how they speak, their main attitude, and one weird quirk that makes them memorable.
Be specific and bold, avoid boring traits.
Also determine chattiness: 0.1-0.5 (quiet), 0.6-1.4 (normal), 1.5-2.0 (chatty).
Return JSON with fields 'persona' (string) and 'chattiness' (float). 
Character:";
        
        public static readonly PersonalityData[] Personalities =
        {
            new PersonalityData { persona ="RimTalk.Persona.CheerfulHelper".Translate(), chattiness =1.5f },
            new PersonalityData { persona ="RimTalk.Persona.CynicalRealist".Translate(), chattiness =0.8f },
            new PersonalityData { persona ="RimTalk.Persona.ShyThinker".Translate(), chattiness =0.3f },
            new PersonalityData { persona ="RimTalk.Persona.Hothead".Translate(), chattiness =1.2f },
            new PersonalityData { persona ="RimTalk.Persona.Philosopher".Translate(), chattiness =1.6f },
            new PersonalityData { persona ="RimTalk.Persona.DarkHumorist".Translate(), chattiness =1.4f },
            new PersonalityData { persona ="RimTalk.Persona.Caregiver".Translate(), chattiness =1.5f },
            new PersonalityData { persona ="RimTalk.Persona.Opportunist".Translate(), chattiness =1.3f },
            new PersonalityData { persona ="RimTalk.Persona.OptimisticDreamer".Translate(), chattiness =1.6f },
            new PersonalityData { persona ="RimTalk.Persona.Pessimist".Translate(), chattiness =0.7f },
            new PersonalityData { persona ="RimTalk.Persona.StoicSoldier".Translate(), chattiness =0.4f },
            new PersonalityData { persona ="RimTalk.Persona.FreeSpirit".Translate(), chattiness =1.7f },
            new PersonalityData { persona ="RimTalk.Persona.Workaholic".Translate(), chattiness =0.5f },
            new PersonalityData { persona ="RimTalk.Persona.Slacker".Translate(), chattiness =1.1f },
            new PersonalityData { persona ="RimTalk.Persona.NobleIdealist".Translate(), chattiness =1.5f },
            new PersonalityData { persona ="RimTalk.Persona.StreetwiseSurvivor".Translate(), chattiness =1.0f },
            new PersonalityData { persona ="RimTalk.Persona.Scholar".Translate(), chattiness =1.6f },
            new PersonalityData { persona ="RimTalk.Persona.Jokester".Translate(), chattiness =1.8f },
            new PersonalityData { persona ="RimTalk.Persona.MelancholicPoet".Translate(), chattiness =0.4f },
            new PersonalityData { persona ="RimTalk.Persona.Paranoid".Translate(), chattiness =0.6f },
            new PersonalityData { persona ="RimTalk.Persona.Commander".Translate(), chattiness =1.0f },
            new PersonalityData { persona ="RimTalk.Persona.Coward".Translate(), chattiness =0.7f },
            new PersonalityData { persona ="RimTalk.Persona.ArrogantNoble".Translate(), chattiness =1.4f },
            new PersonalityData { persona ="RimTalk.Persona.LoyalCompanion".Translate(), chattiness =1.3f },
            new PersonalityData { persona ="RimTalk.Persona.CuriousExplorer".Translate(), chattiness =1.7f },
            new PersonalityData { persona ="RimTalk.Persona.ColdRationalist".Translate(), chattiness =0.3f },
            new PersonalityData { persona ="RimTalk.Persona.FlirtatiousCharmer".Translate(), chattiness =1.9f },
            new PersonalityData { persona ="RimTalk.Persona.BitterOutcast".Translate(), chattiness =0.5f },
            new PersonalityData { persona ="RimTalk.Persona.Zealot".Translate(), chattiness =1.8f },
            new PersonalityData { persona ="RimTalk.Persona.Trickster".Translate(), chattiness =1.6f },
            new PersonalityData { persona ="RimTalk.Persona.DeadpanRealist".Translate(), chattiness =0.6f },
            new PersonalityData { persona ="RimTalk.Persona.ChildAtHeart".Translate(), chattiness =1.7f },
            new PersonalityData { persona ="RimTalk.Persona.SkepticalScientist".Translate(), chattiness =1.2f },
            new PersonalityData { persona ="RimTalk.Persona.Martyr".Translate(), chattiness =1.3f },
            new PersonalityData { persona ="RimTalk.Persona.Manipulator".Translate(), chattiness =1.5f },
            new PersonalityData { persona ="RimTalk.Persona.Rebel".Translate(), chattiness =1.4f },
            new PersonalityData { persona ="RimTalk.Persona.Oddball".Translate(), chattiness =1.2f },
            new PersonalityData { persona ="RimTalk.Persona.GreedyMerchant".Translate(), chattiness =1.7f },
            new PersonalityData { persona ="RimTalk.Persona.Romantic".Translate(), chattiness =1.6f },
            new PersonalityData { persona ="RimTalk.Persona.BattleManiac".Translate(), chattiness =0.8f },
            new PersonalityData { persona ="RimTalk.Persona.GrumpyElder".Translate(), chattiness =1.0f },
            new PersonalityData { persona ="RimTalk.Persona.AmbitiousClimber".Translate(), chattiness =1.5f },
            new PersonalityData { persona ="RimTalk.Persona.Mediator".Translate(), chattiness =1.4f },
            new PersonalityData { persona ="RimTalk.Persona.Gambler".Translate(), chattiness =1.5f },
            new PersonalityData { persona ="RimTalk.Persona.ArtisticSoul".Translate(), chattiness =0.9f },
            new PersonalityData { persona ="RimTalk.Persona.Drifter".Translate(), chattiness =0.6f },
            new PersonalityData { persona ="RimTalk.Persona.Perfectionist".Translate(), chattiness =0.8f },
            new PersonalityData { persona ="RimTalk.Persona.Vengeful".Translate(), chattiness =0.7f }
        };
    }
}