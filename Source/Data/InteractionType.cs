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
    public static InteractionDef? ToInteractionDef(this InteractionType type)
    {
        return type switch
        {
            InteractionType.Insult => InteractionDefOf.Insult,
            InteractionType.Chat => InteractionDefOf.Chitchat,
            InteractionType.Slight => DefDatabase<InteractionDef>.GetNamed("Slight", false),
            InteractionType.Kind => DefDatabase<InteractionDef>.GetNamed("KindWords", false),
            _ => null
        };
    }
}