using Verse;

namespace RimTalk.Data;

public static class Constant
{
    public const string DefaultCloudModel = "gemma-3-27b-it";
    public const string FallbackCloudModel = "gemma-3-12b-it";
    public const string ChooseModel = "(choose model)";

    public static string Lang => LanguageDatabase.activeLanguage?.info?.friendlyNameNative ?? "English";
    public static HediffDef VocalLinkDef => DefDatabase<HediffDef>.GetNamedSilentFail("VocalLinkImplant");

    public static string DefaultInstruction =>
        $"""
         Role-play RimWorld character per profile

         Rules:
         Preserve original names (no translation)
         Keep dialogue short ({Lang} only, 1-2 sentences)

         Roles:
         Prisoner: wary, hesitant; mention confinement; plead or bargain
         Slave: fearful, obedient; reference forced labor and exhaustion; call colonists "master"
         Visitor: polite, curious, deferential; treat other visitors in the same group as companions
         Enemy: hostile, aggressive; terse commands/threats

         Monologue = 1 turn. Conversation = 4-8 short turns
         """;

    private const string JsonInstruction = """
                                           Output JSONL.
                                           Required keys: "name", "text".
                                           """;
    
    private const string SocialInstruction = """
                                           Optional keys (Include only if social interaction occurs):
                                           "act": Insult, Slight, Chat, Kind
                                           "target": targetName
                                           """;

    // Get the current instruction from settings or fallback to default, always append JSON instruction
    // NOTE: This is now primarily used as a fallback. The new PromptManager system is preferred.
    public static string Instruction
    {
        get
        {
            var settings = Settings.Get();
            var baseInstruction = string.IsNullOrWhiteSpace(settings.CustomInstruction)
                ? DefaultInstruction
                : settings.CustomInstruction;
        
            return baseInstruction + "\n" + JsonInstruction + (settings.ApplyMoodAndSocialEffects ? "\n" + SocialInstruction : "");
        }
    }
    
    // JSON instruction for use by PromptManager
    public static string GetJsonInstruction(bool includeSocialEffects)
    {
        return JsonInstruction + (includeSocialEffects ? "\n" + SocialInstruction : "");
    }

    public static string PersonaGenInstruction =>
        $"""
         Create a funny persona (to be used as conversation style) in {Lang}. Must be short in 1 sentence.
         Include: how they speak, their main attitude, and one weird quirk that makes them memorable.
         Be specific and bold, avoid boring traits.
         Also determine chattiness: 0.1-0.3 (quiet), 0.4-0.7 (normal), 0.8-1.0 (chatty).
         Must return JSON only, with fields 'persona' (string) and 'chattiness' (float).
         """;

    private static PersonalityData[] _personalities;
    public static PersonalityData[] Personalities => _personalities ??=
    [
        new("RimTalk.Persona.CheerfulHelper".Translate(), 0.75f),
        new("RimTalk.Persona.CynicalRealist".Translate(), 0.4f),
        new("RimTalk.Persona.ShyThinker".Translate(), 0.15f),
        new("RimTalk.Persona.Hothead".Translate(), 0.6f),
        new("RimTalk.Persona.Philosopher".Translate(), 0.8f),
        new("RimTalk.Persona.DarkHumorist".Translate(), 0.7f),
        new("RimTalk.Persona.Caregiver".Translate(), 0.75f),
        new("RimTalk.Persona.Opportunist".Translate(), 0.65f),
        new("RimTalk.Persona.OptimisticDreamer".Translate(), 0.8f),
        new("RimTalk.Persona.Pessimist".Translate(), 0.35f),
        new("RimTalk.Persona.StoicSoldier".Translate(), 0.2f),
        new("RimTalk.Persona.FreeSpirit".Translate(), 0.85f),
        new("RimTalk.Persona.Workaholic".Translate(), 0.25f),
        new("RimTalk.Persona.Slacker".Translate(), 0.55f),
        new("RimTalk.Persona.NobleIdealist".Translate(), 0.75f),
        new("RimTalk.Persona.StreetwiseSurvivor".Translate(), 0.5f),
        new("RimTalk.Persona.Scholar".Translate(), 0.8f),
        new("RimTalk.Persona.Jokester".Translate(), 0.9f),
        new("RimTalk.Persona.MelancholicPoet".Translate(), 0.2f),
        new("RimTalk.Persona.Paranoid".Translate(), 0.3f),
        new("RimTalk.Persona.Commander".Translate(), 0.5f),
        new("RimTalk.Persona.Coward".Translate(), 0.35f),
        new("RimTalk.Persona.ArrogantNoble".Translate(), 0.7f),
        new("RimTalk.Persona.LoyalCompanion".Translate(), 0.65f),
        new("RimTalk.Persona.CuriousExplorer".Translate(), 0.85f),
        new("RimTalk.Persona.ColdRationalist".Translate(), 0.15f),
        new("RimTalk.Persona.FlirtatiousCharmer".Translate(), 0.95f),
        new("RimTalk.Persona.BitterOutcast".Translate(), 0.25f),
        new("RimTalk.Persona.Zealot".Translate(), 0.9f),
        new("RimTalk.Persona.Trickster".Translate(), 0.8f),
        new("RimTalk.Persona.DeadpanRealist".Translate(), 0.3f),
        new("RimTalk.Persona.ChildAtHeart".Translate(), 0.85f),
        new("RimTalk.Persona.SkepticalScientist".Translate(), 0.6f),
        new("RimTalk.Persona.Martyr".Translate(), 0.65f),
        new("RimTalk.Persona.Manipulator".Translate(), 0.75f),
        new("RimTalk.Persona.Rebel".Translate(), 0.7f),
        new("RimTalk.Persona.Oddball".Translate(), 0.6f),
        new("RimTalk.Persona.GreedyMerchant".Translate(), 0.85f),
        new("RimTalk.Persona.Romantic".Translate(), 0.8f),
        new("RimTalk.Persona.BattleManiac".Translate(), 0.4f),
        new("RimTalk.Persona.GrumpyElder".Translate(), 0.5f),
        new("RimTalk.Persona.AmbitiousClimber".Translate(), 0.75f),
        new("RimTalk.Persona.Mediator".Translate(), 0.7f),
        new("RimTalk.Persona.Gambler".Translate(), 0.75f),
        new("RimTalk.Persona.ArtisticSoul".Translate(), 0.45f),
        new("RimTalk.Persona.Drifter".Translate(), 0.3f),
        new("RimTalk.Persona.Perfectionist".Translate(), 0.4f),
        new("RimTalk.Persona.Vengeful".Translate(), 0.35f)
    ];

    private static PersonalityData _personaAnimal;
    public static PersonalityData PersonaAnimal => _personaAnimal ??= new("RimTalk.Persona.Animal".Translate(), 0.2f);

    private static PersonalityData _personaMech;
    public static PersonalityData PersonaMech => _personaMech ??= new("RimTalk.Persona.Mech".Translate(), 0.2f);

    private static PersonalityData _personaNonHuman;
    public static PersonalityData PersonaNonHuman => _personaNonHuman ??= new("RimTalk.Persona.NonHuman".Translate(), 0.2f);
}