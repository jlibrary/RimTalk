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
            InteractionType.Insult => DefDatabase<ThoughtDef>.GetNamed("RimTalk_Slighted"),
            InteractionType.Slight => DefDatabase<ThoughtDef>.GetNamed("RimTalk_Slighted"),
            InteractionType.Chat => DefDatabase<ThoughtDef>.GetNamed("RimTalk_Chitchat"),
            InteractionType.Kind => DefDatabase<ThoughtDef>.GetNamed("RimTalk_KindWords"),
            _ => null
        };
    }
}