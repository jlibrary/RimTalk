using System;
using System.Runtime.Serialization;
using RimTalk.Source.Data;

namespace RimTalk.Data;

[DataContract]
public class TalkResponse(TalkType talkType, string name, string text) : IJsonData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TalkType TalkType { get; set; } = talkType;

    [DataMember(Name = "name")] public string Name { get; set; } = name;

    [DataMember(Name = "text")] public string Text { get; set; } = text;

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