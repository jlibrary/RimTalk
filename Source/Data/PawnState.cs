using System.Collections.Generic;
using System.Linq;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using Verse;

namespace RimTalk.Data
{
    public class PawnState
    {
        public Pawn Pawn;

        public string Context { get; set; }
        public int LastTalkTick { get; set; }
        public string LastStatus { get; set; }
        public int RejectCount { get; set; }
        public JobDef CurrentJob { get; set; }
        public readonly Queue<Talk> TalkQueue = new Queue<Talk>();
        public bool IsGeneratingTalk { get; set; }
        public int TalkInterval;
        public int ReplyInterval { get; set; } = 3;
        public TalkRequest TalkRequest { get; set; }
        public Dictionary<string, float> Thoughts { get; set; }
        public HashSet<Hediff> Hediffs { get; set; }
        
        public string Personality => Current.Game.GetComponent<PersonaManager>()?.GetPersonality(Pawn);
        public double TalkInitiationWeight => Current.Game.GetComponent<PersonaManager>().GetTalkInitiationWeight(Pawn);

        public PawnState(Pawn pawn)
        {
            this.Pawn = pawn;
            TalkInterval = Settings.Get().talkInterval;
            LastTalkTick = Find.TickManager.TicksGame;
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
                Thoughts = PawnService.GetThoughts(Pawn).ToDictionary(kvp => kvp.Key.def.defName, kvp => kvp.Value);
        }

        public void AddTalkRequest(string prompt, Pawn recipient = null)
        {
            TalkRequest = new TalkRequest(prompt, Pawn, recipient);
        }

        public bool CanDisplayTalk()
        {
            if (!Settings.Get().displayTalkWhenDrafted && Pawn.Drafted)
                return false;
            
            return Pawn.Awake()
                   && Pawn.CurJobDef != JobDefOf.LayDown
                   && Pawn.CurJobDef != JobDefOf.LayDownAwake
                   && Pawn.CurJobDef != JobDefOf.LayDownResting
                   && !IsGeneratingTalk 
                   && TalkInitiationWeight > 0 
                   && Find.TickManager.TicksGame - LastTalkTick >
                   CommonUtil.GetTicksForDuration(TalkInterval);
        }
        
        public bool CanGenerateTalk(bool noInvader = false)
        {
            if (noInvader && PawnService.IsInvader(Pawn))
                return false;
            return CanDisplayTalk() && TalkQueue.Empty();
        }
    }
}