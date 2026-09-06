using System.Collections.Generic;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimWorld;
using Verse;

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
    public int ConversationId { get; set; } = -1;
    public InteractionType InteractionType { get; set; } = InteractionType.None;
    public TalkType TalkType { get; set; } = TalkType.Other;

    // Override this method to customize the log message
    protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
    {
        return _cachedString;
    }
}