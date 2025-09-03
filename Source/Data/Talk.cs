using System;
using System.Runtime.Serialization;

namespace RimTalk.Data
{
    [DataContract]
    public class Talk
    {
        public Guid Id { get; set; }
        
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }
        
        public Guid ReplyToTalkId { get; set; }
    }
    
}