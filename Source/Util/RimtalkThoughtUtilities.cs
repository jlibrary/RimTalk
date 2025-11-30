using System;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

namespace RimTalk.Util;

/// <summary>
/// Utility for applying simple, non-social mood thoughts
/// based on the InteractionType returned by the LLM.
/// </summary>
public static class RimTalkThoughtUtility
{
    private static readonly ThoughtDef KindWordsDef =
        DefDatabase<ThoughtDef>.GetNamedSilentFail("RimTalk_KindWords");

    private static readonly ThoughtDef ChitchatDef =
        DefDatabase<ThoughtDef>.GetNamedSilentFail("RimTalk_Chitchat");

    private static readonly ThoughtDef InsultedMoodDef =
        DefDatabase<ThoughtDef>.GetNamedSilentFail("RimTalk_InsultedMood");

    /// <summary>
    /// Apply non-social mood effects for a RimTalk interaction.
    /// This is deliberately separate from vanilla social thoughts:
    /// - We NEVER require otherPawn (so no ISocialThought / otherPawn null issues).
    /// - We only use InteractionType (Kind / Chat / Insult / Slight).
    /// </summary>
    /// <param name="initiator">The pawn who is speaking.</param>
    /// <param name="recipient">The pawn who is targeted or hearing the line. May be the same as initiator.</param>
    /// <param name="interactionType">Interaction type parsed from TalkResponse.act.</param>
    public static void ApplyNonSocialMoodEffects(Pawn initiator, Pawn recipient, InteractionType interactionType)
    {
        if (interactionType == InteractionType.None)
        {
            return;
        }

        bool initiatorHasMood = HasMood(initiator);
        bool recipientHasMood = HasMood(recipient);

        if (!initiatorHasMood && !recipientHasMood)
        {
            return;
        }

        try
        {
            switch (interactionType)
            {
                case InteractionType.Chat:
                    if (initiatorHasMood)
                    {
                        TryGainMemory(initiator, ChitchatDef);
                    }

                    if (recipientHasMood && recipient != initiator)
                    {
                        TryGainMemory(recipient, ChitchatDef);
                    }

                    break;

                case InteractionType.Kind:
                    if (recipientHasMood)
                    {
                        TryGainMemory(recipient, KindWordsDef);
                    }

                    if (initiatorHasMood && initiator != recipient)
                    {
                        TryGainMemory(initiator, ChitchatDef); 
                    }

                    break;

                case InteractionType.Insult:
                case InteractionType.Slight:
                    if (recipientHasMood)
                    {
                        TryGainMemory(recipient, InsultedMoodDef);
                    }

                    break;

                default:
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[RimTalk] Failed to apply non-social mood for {initiator} -> {recipient} ({interactionType}): {e}");
        }
    }

    private static bool HasMood(Pawn pawn)
    {
        return pawn?.needs?.mood?.thoughts?.memories != null;
    }

    private static void TryGainMemory(Pawn pawn, ThoughtDef def)
    {
        if (pawn == null || def == null)
        {
            return;
        }

        try
        {
            pawn.needs.mood.thoughts.memories.TryGainMemory(def);
        }
        catch (Exception e)
        {
            Logger.Error($"[RimTalk] Error while gaining RimTalk mood thought {def.defName} for {pawn}: {e}");
        }
    }
}
