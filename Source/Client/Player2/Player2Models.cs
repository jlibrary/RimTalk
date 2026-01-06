using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimTalk.Client.Player2;

[DataContract]
public class Player2Request
{
    [DataMember(Name = "messages")]
    public List<Message> Messages { get; set; } = [];

    [DataMember(Name = "stream", EmitDefaultValue = false)]
    public bool? Stream { get; set; }
}

[DataContract]
public class Message
{
    [DataMember(Name = "role")]
    public string Role { get; set; }

    [DataMember(Name = "content")]
    public string Content { get; set; }
}

[DataContract]
public class Player2Response
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "choices")]
    public List<Choice> Choices { get; set; }

    [DataMember(Name = "usage")]
    public Usage Usage { get; set; }
}

[DataContract]
public class Choice
{
    [DataMember(Name = "message")]
    public Message Message { get; set; }

    [DataMember(Name = "delta")]
    public Delta Delta { get; set; }

    [DataMember(Name = "finish_reason")]
    public string FinishReason { get; set; }
}

[DataContract]
public class Delta
{
    [DataMember(Name = "content")]
    public string Content { get; set; }
}

[DataContract]
public class Usage
{
    [DataMember(Name = "total_tokens")]
    public int TotalTokens { get; set; }
}

[DataContract]
public class Player2StreamChunk
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "choices")]
    public List<Choice> Choices { get; set; }

    [DataMember(Name = "usage")]
    public Usage Usage { get; set; }
    
    [DataMember(Name = "error")]
    public string Error { get; set; }
}