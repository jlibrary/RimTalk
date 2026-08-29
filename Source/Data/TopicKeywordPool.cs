namespace RimTalk.Data;

/// <summary>
/// Large, rich pools of concise (1-2 words) thematic keywords.
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
    /// Pure narrative discussion subjects (1-2 words).
    /// Focuses strictly on lore, past memories, philosophy, quirks, and self-contained musings
    /// that are completely independent of current events, combat, or the presence of others.
    /// </summary>
    public static readonly string[] SubjectKeywords =
    [
        // Past Life & Origins
        "childhood", "hometown", "past job", "family memories", "old mentors",
        "forgotten skills", "school days", "past mistakes", "first journey", "family heirlooms",
        "childhood games", "lost keepsakes", "cryptosleep stories", "accent and dialect",
        "past celebrations", "earliest memory", "life before landing", "old friends",

        // Tastes, Habits & Quirks
        "taste in music", "favorite flavors", "bad habits", "useless skills", "meaning of names",
        "superstitions", "personal rituals", "hidden talents", "things people misunderstand", "sense of humor",
        "pet peeves", "definition of home", "awkward memories", "guilty pleasures", "personal pride",

        // Inner Mind & Psychology
        "trust", "secrets", "loyalty", "forgiveness", "loneliness",
        "guilt and regrets", "stubbornness", "patience", "fears", "peace of mind",

        // Philosophy, Values & Future Aspirations
        "future dreams", "retirement dreams", "luck and fate", "fate vs choice", "meaning of survival",
        "what comes next", "value of money", "fear of aging", "hope", "human nature",
        "legacy", "good luck charms", "second chances", "justice", "curiosity about space",

        // RimWorld Lore, Legends & Frontier Rumors
        "glitterworlds", "ancient legends", "tribal myths", "old earth tales", "space travel stories",
        "bionic philosophy", "drifter stories", "strange rumors", "survival wisdom", "deep space myths",
        "frontier legends", "lost colony rumors"
    ];
}
