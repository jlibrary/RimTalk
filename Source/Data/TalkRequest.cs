using Verse;

namespace RimTalk.Data
{
    public class TalkRequest
    {
        public string Prompt { get; set; }
        public Pawn Initiator { get; set; }
        public Pawn Recipient { get; set; }

        public TalkRequest(string prompt, Pawn initiator, Pawn recipient = null)
        {
            Prompt = prompt;
            Initiator = initiator;
            Recipient = recipient;
        }
    }
}