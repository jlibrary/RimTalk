using System.Collections.Generic;
using System.Linq;
using RimTalk.Service;
using RimTalk.Util;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimTalk.Data
{
    public class PawnState
    {
        public readonly Pawn Pawn;
        public string Context { get; set; }
        public int LastTalkTick { get; set; }
        public string LastStatus { get; set; }
        public int RejectCount { get; set; }
        public readonly Queue<TalkResponse> TalkQueue = new Queue<TalkResponse>();
        public bool IsGeneratingTalk { get; set; }
        public int ReplyInterval { get; set; } = 3;
        public TalkRequest TalkRequest { get; set; }
        public Dictionary<string, float> Thoughts { get; set; }
        public HashSet<Hediff> Hediffs { get; set; }
        
        public string Personality => PersonaService.GetPersonality(Pawn);
        public double TalkInitiationWeight => PersonaService.GetTalkInitiationWeight(Pawn);

        public PawnState(Pawn pawn)
        {
            Pawn = pawn;
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
            if (WorldRendererUtility.WorldSelected || Find.CurrentMap == null || Pawn.Map != Find.CurrentMap || !Pawn.Spawned)
            {
                return false;
            }
            
            if (!Settings.Get().DisplayTalkWhenDrafted && Pawn.Drafted)
                return false;
            
            return Pawn.Awake()
                   && !Pawn.DeadOrDowned
                   && Pawn.CurJobDef != JobDefOf.LayDown
                   && Pawn.CurJobDef != JobDefOf.LayDownAwake
                   && Pawn.CurJobDef != JobDefOf.LayDownResting
                   && TalkInitiationWeight > 0 
                   && Find.TickManager.TicksGame - LastTalkTick >
                   CommonUtil.GetTicksForDuration(Settings.Get().ReplyInterval);
        }
        
        public bool CanGenerateTalk(bool noInvader = false)
        {
            if (noInvader && PawnService.IsInvader(Pawn))
                return false;
            return !IsGeneratingTalk && CanDisplayTalk() && TalkQueue.Empty();
        }
    }
}