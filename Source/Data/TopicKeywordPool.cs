namespace RimTalk.Data;

/// <summary>
/// Pools of concise thematic keywords used as conversational catalysts.
/// Short keywords maximize LLM creativity, prevent stiff/repetitive phrasing,
/// and ensure zero conflict with pawn personas, weather, or DLC lore.
/// </summary>
public static class TopicKeywordPool
{
    /// <summary>
    /// Conversational approach/angle (1-2 words).
    /// Works seamlessly in both solo monologue and multi-pawn dialogues with any personality.
    /// </summary>
    public static readonly string[] ApproachKeywords =
    [
        "banter", "complaint", "nostalgia", "philosophy", "curiosity",
        "worry", "storytelling", "bragging", "rumor", "confession",
        "praise", "observation", "ambition", "debate", "advice",
        "sarcasm", "sympathy", "daydreaming", "reassurance", "speculation",
        "boredom", "self-mockery", "relief", "caution", "skepticism",
        "gratitude", "hesitation", "enthusiasm", "resignation", "morbid humor",
        "envy", "awkwardness", "determination", "suspicion", "shyness",
        "fascination", "fondness", "cynicism", "impatience", "indifference",
        "provocation", "regret", "lightheartedness", "melancholy", "amazement",
        "earnestness", "irritation", "playfulness", "solemnity", "yearning"
    ];

    /// <summary>
    /// Criteria for subject keyword selection (all must be satisfied):
    /// 1. Zero Game-State Conflict: Never reference physical apparel, body injuries/scars,
    ///    specific architecture, or biome-dependent weather/terrain. The keyword must remain
    ///    valid whether a pawn is naked, uninjured, in a desert, or inside a mountain.
    /// 2. Universal Pawn Compatibility: Must be natural for any pawn—4-year-old children,
    ///    spacer nobles, neolithic tribals, transhumanists, psychopaths, or cannibals.
    /// 3. Anti-Melodrama: No heavy existential/psychological therapy themes (guilt, forgiveness,
    ///    loneliness, fate vs choice, human nature). Keep tone casual and conversational—
    ///    tastes, habits, superstitions, rumors, amusements, light curiosities.
    /// 4. Anti-Clustering: No redundant synonyms within or across domains. Each keyword must
    ///    occupy a distinct semantic space to prevent LLM output convergence.
    /// 5. Concise (1-3 words, &lt;= 40 chars): Short anchors spark dialogue without prompt leakage.
    /// </summary>
    public static readonly string[] SubjectKeywords =
    [
        // Domain 1: Tastes, Preferences & Sensory (25)
        "favorite flavors", "comfort foods", "acquired tastes", "cooking opinions", "picky eating",
        "favorite scents", "soothing sounds", "irritating noises", "taste in music", "favorite colors",
        "sense of style", "sleeping positions", "favorite time of day", "personal space", "guilty pleasures",
        "simple comforts", "noise tolerance", "preferred pace", "small indulgences", "aesthetic tastes",
        "favorite textures", "neat versus messy", "creature comforts", "strong opinions", "odd preferences",

        // Domain 2: Habits, Routines & Personal Quirks (25)
        "bad habits", "morning routines", "night owl tendencies", "daydreaming habits", "talking to oneself",
        "fidgeting", "pacing while thinking", "hoarding tendencies", "forgetfulness", "stubborn routines",
        "odd personal rituals", "useless skills", "hidden talents", "nervous tics", "comfort behaviors",
        "work habits", "handling boredom", "patience levels", "pet peeves", "curious obsessions",
        "procrastination", "attention to detail", "unconscious habits", "impulsive tendencies", "daily rituals",

        // Domain 3: Social Dynamics & Communication (25)
        "meaning of names", "embarrassing nicknames", "accent and dialect", "speaking habits", "strange gestures",
        "awkward silences", "first impressions", "reading people", "blunt honesty", "keeping secrets",
        "trusting strangers", "social blunders", "unspoken rules", "teasing habits", "polite lies",
        "giving advice", "handling criticism", "sharing habits", "personal boundaries", "conversation starters",
        "listening skills", "saying goodbye", "apology styles", "compliment giving", "gossip tendencies",

        // Domain 4: Superstitions, Folk Beliefs & Traditions (25)
        "superstitions", "good luck charms", "lucky numbers", "fortune telling", "old sayings",
        "folk remedies", "warding off bad luck", "ancestor wisdom", "strange coincidences", "gut feelings",
        "dream meanings", "star reading", "harvest rituals", "naming customs", "blessing rituals",
        "taboo subjects", "sacred animals", "prayer habits", "fortune signs", "protective symbols",
        "festival beliefs", "ritual greetings", "omen reading", "charm making", "prophecy tales",

        // Domain 5: Humor, Amusements & Play (25)
        "childhood games", "campfire songs", "sense of humor", "ridiculous bets", "silly arguments",
        "harmless pranks", "tall tales", "guessing games", "contests of strength", "riddles and puzzles",
        "funny mistakes", "embarrassing moments", "bad advice stories", "humorous excuses", "parody and mimicry",
        "witty comebacks", "absurd dares", "made-up games", "storytelling contests", "mock debates",
        "funny impressions", "word games", "playful insults", "comedy timing", "running jokes",

        // Domain 6: Frontier Lore, Legends & Rumors (25)
        "ancient legends", "frontier legends", "drifter stories", "lost settlement tales", "glitterworld rumors",
        "space travel stories", "deep space myths", "old earth tales", "tribal myths", "tales of machines",
        "tales of ruins", "beast folklore", "cryptosleep legends", "distant colony rumors", "caravan gossip",
        "strange encounter tales", "survival stories", "wanderer wisdom", "trade route legends", "ghost ship tales",
        "forbidden technology", "ancient artifacts", "lost expedition tales", "outlaw legends", "first contact stories",

        // Domain 7: Curiosity, Wonder & Imagination (25)
        "curiosity about space", "imaginary places", "what-if scenarios", "curious creatures", "shapes in clouds",
        "collecting habits", "unusual hobbies", "tinkering ideas", "impossible inventions", "strange facts",
        "animal behaviors", "plant oddities", "mineral curiosities", "map fantasies", "language differences",
        "counting the stars", "naming constellations", "peculiar customs", "world beyond horizon", "underground wonders",
        "ocean mysteries", "mirage stories", "sky phenomena", "nature's patterns", "unexplored places",

        // Domain 8: Memories, Origins & Life Experiences (25)
        "earliest memory", "childhood antics", "family traditions", "old mentors", "forgotten teachings",
        "first journeys", "memorable meals", "lost keepsakes", "past celebrations", "first encounters",
        "lessons learned hard", "former trades", "travel companions", "old rivalries", "escaped dangers",
        "unusual teachers", "childhood heroes", "family sayings", "memorable strangers", "past hobbies",
        "old competitions", "learning experiences", "past adventures", "formative moments", "tales of youth"
    ];
}
