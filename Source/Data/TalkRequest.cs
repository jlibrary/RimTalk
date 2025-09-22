using RimTalk.Util;
using Verse;

namespace RimTalk.Data
{
    public class TalkRequest
    {
        public enum Type { Battle, Hediff, LevelUp, Chitchat, Event, Other }

        public Type RequestType { get; set; }
        public string Prompt { get; set; }
        public Pawn Initiator { get; set; }
        public Pawn Recipient { get; set; }
        public int MapId { get; set; }
        public int CreatedTick { get; set; }

        public TalkRequest(string prompt, Pawn initiator, Pawn recipient = null, Type type = Type.Other)
        {
            RequestType = type;
            Prompt = prompt;
            Initiator = initiator;
            Recipient = recipient;
            CreatedTick = GenTicks.TicksGame;
        }

        public bool IsExpired()
        {
            int duration = 10;
            switch (RequestType)
            {
                case Type.Battle:
                    duration = 5;
                    break;
            }
            return GenTicks.TicksGame - CreatedTick > CommonUtil.GetTicksForDuration(duration);
        }
    }
}