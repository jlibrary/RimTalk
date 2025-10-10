using System.Collections.Generic;
using RimWorld;
using Verse;
using RimTalk.Service;

namespace RimTalk;

public class PlayLogEntry_RimTalkInteraction : PlayLogEntry_Interaction
{
    private string _cachedString;

    public PlayLogEntry_RimTalkInteraction()
    {
        // Parameterless constructor required for Scribing (loading from save)
    }

    public PlayLogEntry_RimTalkInteraction(
        InteractionDef interactionDef,
        Pawn initiator,
        Pawn recipient,
        List<RulePackDef> rules)
        : base(interactionDef, initiator, recipient, rules)
    {
        _cachedString = TalkService.GetTalk(initiator);
    }

    public Pawn Initiator => initiator;
    public Pawn Recipient => recipient;
    public List<RulePackDef> ExtraSentencePacks => extraSentencePacks;
    public string CachedString => _cachedString;
    public int TicksAbs => ticksAbs;

    // Override this method to customize the log message
    protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
    {
        return _cachedString;
    }
}