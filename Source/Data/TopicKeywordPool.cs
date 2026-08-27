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
    /// Works seamlessly with any personality (cynical, cheerful, stoic, paranoid, etc.).
    /// </summary>
    public static readonly string[] ApproachKeywords =
    [
        "joke", "tease", "gripe", "opinion", "advice",
        "question", "nostalgia", "speculation", "curiosity", "bragging",
        "rumor", "storytelling", "philosophy", "confession", "praise",
        "observation", "worry", "ambition", "debate", "suggestion",
        "reminiscing", "reflection", "sarcasm", "sympathy", "inquiry",
        "comparison", "banter", "planning", "complaint", "reassurance"
    ];

    /// <summary>
    /// Discussion subjects (1-2 words).
    /// Broad and context-safe: can be discussed via memories, opinions, or casual remarks
    /// without asserting fake in-game facts.
    /// </summary>
    public static readonly string[] SubjectKeywords =
    [
        // Survival & Daily Life
        "food", "meals", "cooking", "farming", "harvest",
        "rations", "alcohol", "drinks", "sleep", "fatigue",
        "chores", "labor", "construction", "crafting", "tools",
        "weapons", "armor", "clothing", "silver", "wealth",
        "trading", "merchants", "caravans", "recreation", "music",
        "art", "games", "hobbies", "health", "injuries",

        // People & Personal Lore
        "family", "friends", "childhood", "past job", "hometown",
        "teamwork", "leadership", "secrets", "romance", "loneliness",
        "trust", "habits", "quirks", "aging", "nightmares",
        "dreams", "luck", "destiny", "superstitions", "rumors",

        // World & Nature
        "starships", "glitterworlds", "outer space", "wanderers", "visitors",
        "factions", "ancient ruins", "lost tech", "mechanoids", "insects",
        "raiders", "wild animals", "pets", "hunting", "wilderness",
        "weather", "seasons", "night sky", "stars", "survival"
    ];

    /// <summary>
    /// Concise keywords for solo monologue/muttering (1-2 words).
    /// </summary>
    public static readonly string[] MonologueKeywords =
    [
        "chores", "old memories", "meals", "rest", "starships",
        "fatigue", "body aches", "spacing out", "tomorrow", "personal goals",
        "the weather", "strange thoughts", "loneliness", "weapons", "hometown",
        "humming", "sighing", "future dreams", "curiosity", "survival",
        "family", "crafting", "luck", "the quiet", "patience"
    ];
}
