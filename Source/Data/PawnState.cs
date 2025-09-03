using System.Collections.Generic;
using RimTalk.Service;
using System.Linq;
using RimTalk.Util;
using Verse;
using RimWorld;

namespace RimTalk.Data
{
    public class PawnState
    {
        public Pawn pawn;

        public string Context { get; set; }
        public int LastTalkTick { get; set; }
        public string LastPrompt { get; set; }
        public int RejectCount { get; set; }
        public JobDef CurrentJob { get; set; }
        public readonly Queue<Talk> TalkQueue = new Queue<Talk>();
        public bool IsGeneratingTalk { get; set; }
        public int TalkInterval => Settings.Get().talkInterval;
        public int ReplyInternal { get; set; } = 2;
        public Dictionary<string, float> Thoughts { get; set; }
        public HashSet<Hediff> Hediffs { get; set; }
        
        public string Personality => Current.Game.GetComponent<PersonalityManager>()?.GetPersonality(pawn);

        public PawnState(Pawn pawn)
        {
            this.pawn = pawn;
            UpdateThoughts();
            Hediffs = PawnService.GetHediffs(pawn);
        }

        public void UpdateThoughts() {
            UpdateThoughts(new KeyValuePair<Thought, float>(null, 0.0f));
        }
        public void UpdateThoughts(KeyValuePair<Thought, float> thought)
        {
            if (thought.Key != null)
                Thoughts[thought.Key.def.defName] = thought.Value;
            else
                Thoughts = PawnService.GetThoughts(pawn).ToDictionary(kvp => kvp.Key.def.defName, kvp => kvp.Value);
        }
        
        public bool CanGenerateTalk()
        {
            return TalkQueue.Empty() && 
                   !IsGeneratingTalk && 
                   Find.TickManager.TicksGame - LastTalkTick >
                   CommonUtil.GetTicksForDuration(TalkInterval);
        }

    }
}