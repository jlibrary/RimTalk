using System;
using System.Runtime.Serialization;
using RimTalk.Source.Data;

namespace RimTalk.Data;

[DataContract]
public class TalkResponse : IJsonData
{
    public Guid Id { get; set; }
    
    public TalkType TalkType { get; set; }

    [DataMember(Name = "name")] public string Name { get; set; }

    [DataMember(Name = "text")] public string Text { get; set; }

    public Guid ParentTalkId { get; set; }
    
    public bool IsReply()
    {
        return ParentTalkId != Guid.Empty;
    }
        
    public override string ToString()
    {
        return Text;
    }
}