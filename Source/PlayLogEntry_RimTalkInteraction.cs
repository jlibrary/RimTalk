using System.Collections.Generic;
using RimWorld;
using Verse;
using RimTalk.Service;

namespace RimTalk
{
    public class PlayLogEntry_RimTalkInteraction : PlayLogEntry_Interaction
    {
        private string _talkContent;

        public PlayLogEntry_RimTalkInteraction() { }

        public PlayLogEntry_RimTalkInteraction(InteractionDef interactionDef, Pawn initiator, Pawn recipient, List<RulePackDef> rules)
            : base(interactionDef, initiator, recipient, rules)
        {
            _talkContent = TalkService.GetTalk(initiator);
        }

        // Override this method to customize the log message
        protected override string ToGameStringFromPOV_Worker(Thing pov, bool forceLog)
        {
            return _talkContent;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _talkContent, "talkContent");
        }
    }
}
