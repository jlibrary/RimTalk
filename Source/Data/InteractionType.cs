#nullable enable
using RimWorld;
using Verse;

namespace RimTalk.Source.Data;

public enum InteractionType
{
    None, Insult, Slight, Chat, Kind
}

public static class InteractionExtensions
{
    public static ThoughtDef? GetThoughtDef(this InteractionType type)
    {
        return type switch
        {
            InteractionType.Insult => DefDatabase<ThoughtDef>.GetNamed("Slighted"),
            InteractionType.Slight => DefDatabase<ThoughtDef>.GetNamed("Slighted"),
            InteractionType.Chat => DefDatabase<ThoughtDef>.GetNamed("Chitchat"),
            InteractionType.Kind => DefDatabase<ThoughtDef>.GetNamed("KindWords"),
            _ => null
        };
    }
}