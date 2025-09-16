using System;
using System.Runtime.Serialization;

namespace RimTalk.Data
{
    [DataContract]
    public class TalkResponse : IJsonData
    {
        public Guid Id { get; set; }

        [DataMember(Name = "name")] public string Name { get; set; }

        [DataMember(Name = "text")] public string Text { get; set; }

        public Guid ParentTalkId { get; set; }

        public string ResponsePayload { get; set; }

        public bool IsReply()
        {
            return ParentTalkId != Guid.Empty;
        }
        
        public override string ToString()
        {
            return Text;
        }
    }
}