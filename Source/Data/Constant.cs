using Verse;

namespace RimTalk.Data;

public static class Constant
{
    public const string ModTag = "[RimTalk]";
    public const string DefaultCloudModel = "gemma-3-27b-it";
    public const string FallbackCloudModel = "gemma-3-12b-it";
    public const string ChooseModel = "(choose model)";

    public static readonly string Lang = LanguageDatabase.activeLanguage.info.friendlyNameNative;

    public static readonly string DefaultInstruction =
        $@"Role-play RimWorld character per profile

Rules:
Preserve original names (no translation)
Keep dialogue short ({Lang} only, 1–2 sentences)
Show concern for sick/mental issues
Never mention another character's personal name unless they share the same role

Roles:
Prisoner: wary, hesitant; mention confinement; plead or bargain
Slave: fearful, obedient; reference forced labor and exhaustion; call colonists ""master""
Visitor: polite, curious, deferential; treat other visitors in the same group as companions
Enemy: hostile, aggressive; terse commands/threats

Monologue = 1 turn. Conversation = 4–8 short turns";
        
    private const string JsonInstruction = @"

Return JSONL/NDJSON only, with objects containing ""name"" and ""text"" string keys";

    // Get the current instruction from settings or fallback to default, always append JSON instruction
    public static string Instruction =>
        (string.IsNullOrWhiteSpace(Settings.Get().CustomInstruction)
            ? DefaultInstruction
            : Settings.Get().CustomInstruction) + JsonInstruction;

    public const string Prompt =
        "Act based on role and context";

    public static readonly string PersonaGenInstruction =
        $@"Create a funny persona (to be used as conversation style) in {Lang}. Must be short in 1 sentence.
Include: how they speak, their main attitude, and one weird quirk that makes them memorable.
Be specific and bold, avoid boring traits.
Also determine chattiness: 0.1-0.5 (quiet), 0.6-1.4 (normal), 1.5-2.0 (chatty).
Must return JSON only, with fields 'persona' (string) and 'chattiness' (float).";
        
    public static readonly PersonalityData[] Personalities =
    {
        new() { Persona ="RimTalk.Persona.CheerfulHelper".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.CynicalRealist".Translate(), Chattiness =0.8f },
        new() { Persona ="RimTalk.Persona.ShyThinker".Translate(), Chattiness =0.3f },
        new() { Persona ="RimTalk.Persona.Hothead".Translate(), Chattiness =1.2f },
        new() { Persona ="RimTalk.Persona.Philosopher".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.DarkHumorist".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.Caregiver".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.Opportunist".Translate(), Chattiness =1.3f },
        new() { Persona ="RimTalk.Persona.OptimisticDreamer".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.Pessimist".Translate(), Chattiness =0.7f },
        new() { Persona ="RimTalk.Persona.StoicSoldier".Translate(), Chattiness =0.4f },
        new() { Persona ="RimTalk.Persona.FreeSpirit".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.Workaholic".Translate(), Chattiness =0.5f },
        new() { Persona ="RimTalk.Persona.Slacker".Translate(), Chattiness =1.1f },
        new() { Persona ="RimTalk.Persona.NobleIdealist".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.StreetwiseSurvivor".Translate(), Chattiness =1.0f },
        new() { Persona ="RimTalk.Persona.Scholar".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.Jokester".Translate(), Chattiness =1.8f },
        new() { Persona ="RimTalk.Persona.MelancholicPoet".Translate(), Chattiness =0.4f },
        new() { Persona ="RimTalk.Persona.Paranoid".Translate(), Chattiness =0.6f },
        new() { Persona ="RimTalk.Persona.Commander".Translate(), Chattiness =1.0f },
        new() { Persona ="RimTalk.Persona.Coward".Translate(), Chattiness =0.7f },
        new() { Persona ="RimTalk.Persona.ArrogantNoble".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.LoyalCompanion".Translate(), Chattiness =1.3f },
        new() { Persona ="RimTalk.Persona.CuriousExplorer".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.ColdRationalist".Translate(), Chattiness =0.3f },
        new() { Persona ="RimTalk.Persona.FlirtatiousCharmer".Translate(), Chattiness =1.9f },
        new() { Persona ="RimTalk.Persona.BitterOutcast".Translate(), Chattiness =0.5f },
        new() { Persona ="RimTalk.Persona.Zealot".Translate(), Chattiness =1.8f },
        new() { Persona ="RimTalk.Persona.Trickster".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.DeadpanRealist".Translate(), Chattiness =0.6f },
        new() { Persona ="RimTalk.Persona.ChildAtHeart".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.SkepticalScientist".Translate(), Chattiness =1.2f },
        new() { Persona ="RimTalk.Persona.Martyr".Translate(), Chattiness =1.3f },
        new() { Persona ="RimTalk.Persona.Manipulator".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.Rebel".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.Oddball".Translate(), Chattiness =1.2f },
        new() { Persona ="RimTalk.Persona.GreedyMerchant".Translate(), Chattiness =1.7f },
        new() { Persona ="RimTalk.Persona.Romantic".Translate(), Chattiness =1.6f },
        new() { Persona ="RimTalk.Persona.BattleManiac".Translate(), Chattiness =0.8f },
        new() { Persona ="RimTalk.Persona.GrumpyElder".Translate(), Chattiness =1.0f },
        new() { Persona ="RimTalk.Persona.AmbitiousClimber".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.Mediator".Translate(), Chattiness =1.4f },
        new() { Persona ="RimTalk.Persona.Gambler".Translate(), Chattiness =1.5f },
        new() { Persona ="RimTalk.Persona.ArtisticSoul".Translate(), Chattiness =0.9f },
        new() { Persona ="RimTalk.Persona.Drifter".Translate(), Chattiness =0.6f },
        new() { Persona ="RimTalk.Persona.Perfectionist".Translate(), Chattiness =0.8f },
        new() { Persona ="RimTalk.Persona.Vengeful".Translate(), Chattiness =0.7f }
    };
}